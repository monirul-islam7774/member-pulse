using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Api.Controllers;
using MemberPulse.Domain.Events;
using MemberPulse.Infrastructure.Messaging;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemberPulse.Tests.Api;

public class OutboxApiTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private RecordingEventPublisher Publisher => factory.Services.GetRequiredService<RecordingEventPublisher>();

    private async Task<AccountDto> NewAccountAsync(string type, decimal deposit)
    {
        var create = await _client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Out", lastName = "Box", email = $"o-{Guid.NewGuid():N}@example.com", dateOfBirth = "1990-01-01",
        });
        var member = await create.Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        var open = await _client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts", new { type, initialDeposit = deposit });
        return (await open.Content.ReadFromJsonAsync<AccountDto>(Json))!;
    }

    /// <summary>Runs the dispatcher's work once, until the outbox is drained or a publish fails.</summary>
    private async Task DrainAsync(int maxAttempts = 20)
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        while (await processor.ProcessBatchAsync(50, maxAttempts, CancellationToken.None) > 0) { }
    }

    private List<TransactionPosted> EventsFor(Guid accountId) =>
        Publisher.Published
            .Where(p => p.Key == accountId.ToString())
            .Select(p => JsonSerializer.Deserialize<TransactionPosted>(p.Payload, Json)!)
            .ToList();

    [Fact]
    public async Task Posted_transaction_is_published_keyed_by_account()
    {
        var account = await NewAccountAsync("Checking", 1000);
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions",
            new { amount = -40, description = "Coffee beans", merchantCategory = "Grocery" });

        await DrainAsync();

        var events = EventsFor(account.Id);
        Assert.Equal(2, events.Count); // initial deposit, then the purchase
        var purchase = events.Single(e => e.Amount < 0);
        Assert.Equal(960m, purchase.BalanceAfter);
        Assert.Equal("Grocery", purchase.MerchantCategory);
        Assert.Equal(account.Id, purchase.AccountId);
        Assert.All(Publisher.Published.Where(p => p.Key == account.Id.ToString()), p =>
        {
            Assert.Equal("memberpulse.transactions", p.Topic);
            Assert.Equal("TransactionPosted", p.Type);
        });
    }

    [Fact]
    public async Task Account_opened_without_deposit_publishes_nothing()
    {
        var account = await NewAccountAsync("Savings", 0);

        await DrainAsync();

        Assert.Empty(EventsFor(account.Id));
    }

    [Fact]
    public async Task Rejected_transaction_publishes_nothing()
    {
        var account = await NewAccountAsync("Checking", 100);
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new { amount = -500, description = "Too much" });

        await DrainAsync();

        Assert.Single(EventsFor(account.Id)); // only the initial deposit
    }

    [Fact]
    public async Task Failed_publish_is_retried_later_in_order_without_loss_or_duplicates()
    {
        var account = await NewAccountAsync("Checking", 500);
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new { amount = -10, description = "First" });
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new { amount = -20, description = "Second" });

        Publisher.Fail = true;
        try { await DrainAsync(); } finally { Publisher.Fail = false; }
        Assert.Empty(EventsFor(account.Id));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemberPulseDbContext>();
            Assert.Contains(await db.OutboxMessages.ToListAsync(), m => m.ProcessedAt is null && m.Attempts > 0 && m.LastError is not null);
        }

        await DrainAsync();
        Assert.Empty(EventsFor(account.Id)); // still backing off: no hammering a broker that is down

        using (factory.Clock.Advance(TimeSpan.FromMinutes(1)))
        {
            await DrainAsync();
            await DrainAsync(); // nothing left, must not re-publish
        }

        var events = EventsFor(account.Id);
        Assert.Equal([500m, -10m, -20m], events.Select(e => e.Amount));
    }

    [Fact]
    public async Task One_accounts_failure_does_not_hold_back_other_accounts()
    {
        var broken = await NewAccountAsync("Checking", 100);
        var healthy = await NewAccountAsync("Checking", 200);
        Publisher.FailKey(broken.Id.ToString());
        try
        {
            await DrainAsync();
        }
        finally
        {
            Publisher.FailKey(broken.Id.ToString(), fail: false);
        }

        Assert.Empty(EventsFor(broken.Id));
        Assert.Single(EventsFor(healthy.Id));
    }

    [Fact]
    public async Task Later_messages_for_an_account_wait_for_an_earlier_one_that_is_backing_off()
    {
        var account = await NewAccountAsync("Checking", 500);
        Publisher.Fail = true;
        try { await DrainAsync(); } finally { Publisher.Fail = false; }

        // A new transaction arrives while the earlier event is still in backoff: it must not overtake it.
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new { amount = -5, description = "Newer" });
        await DrainAsync();
        Assert.Empty(EventsFor(account.Id));

        using (factory.Clock.Advance(TimeSpan.FromMinutes(1)))
        {
            await DrainAsync();
        }

        Assert.Equal([500m, -5m], EventsFor(account.Id).Select(e => e.Amount));
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_dead_lettered_and_stops_blocking_its_account()
    {
        var account = await NewAccountAsync("Checking", 300);
        Publisher.Fail = true;
        try
        {
            // 3 attempts allowed: each needs the previous backoff to have passed.
            await DrainAsync(maxAttempts: 3);
            using (factory.Clock.Advance(TimeSpan.FromMinutes(10))) await DrainAsync(maxAttempts: 3);
            using (factory.Clock.Advance(TimeSpan.FromMinutes(20))) await DrainAsync(maxAttempts: 3);
        }
        finally
        {
            Publisher.Fail = false;
        }

        // The broker is back. The poisoned deposit stays dead; a new transaction is no longer stuck behind it.
        await _client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new { amount = -50, description = "After" });
        await DrainAsync();

        Assert.Equal([-50m], EventsFor(account.Id).Select(e => e.Amount));

        var admin = factory.CreateAuthenticatedClient(MemberPulse.Domain.Staff.StaffRole.Admin);
        var dead = await admin.GetFromJsonAsync<List<DeadLetterDto>>("/api/admin/outbox/dead", Json);
        var letter = Assert.Single(dead!, d => d.Key == account.Id.ToString());
        Assert.Equal(3, letter.Attempts);
        Assert.Contains("broker unavailable", letter.LastError);

        // An admin can put it back in the queue once the cause is fixed.
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/outbox/{letter.Id}/retry", null)).StatusCode);
        await DrainAsync();
        // It arrives late, after the newer event: the price of not letting one bad message block the account forever.
        Assert.Equal([-50m, 300m], EventsFor(account.Id).Select(e => e.Amount));
        Assert.DoesNotContain(await admin.GetFromJsonAsync<List<DeadLetterDto>>("/api/admin/outbox/dead", Json) ?? [], d => d.Id == letter.Id);
    }

    [Fact]
    public async Task Dead_letter_admin_endpoints_are_admin_only_and_retry_of_an_unknown_message_is_404()
    {
        var staff = factory.CreateAuthenticatedClient(MemberPulse.Domain.Staff.StaffRole.Staff);
        var admin = factory.CreateAuthenticatedClient(MemberPulse.Domain.Staff.StaffRole.Admin);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/outbox/dead")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/admin/outbox/{Guid.NewGuid()}/retry", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/outbox/{Guid.NewGuid()}/retry", null)).StatusCode);
    }

    [Fact]
    public async Task Published_messages_are_purged_after_the_retention_period_but_pending_and_dead_ones_stay()
    {
        var account = await NewAccountAsync("Checking", 100);
        await DrainAsync();                         // the deposit is published
        var survivor = await NewAccountAsync("Checking", 100);
        Publisher.FailKey(survivor.Id.ToString());
        try { await DrainAsync(maxAttempts: 1); }   // its deposit is dead-lettered straight away
        finally { Publisher.FailKey(survivor.Id.ToString(), fail: false); }

        int purged;
        using (factory.Clock.Advance(TimeSpan.FromDays(8)))
        {
            using var scope = factory.Services.CreateScope();
            purged = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>()
                .PurgeProcessedAsync(TimeSpan.FromDays(7), CancellationToken.None);
        }

        Assert.True(purged >= 1);
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<MemberPulseDbContext>();
        Assert.DoesNotContain(await db.OutboxMessages.ToListAsync(), m => m.Key == account.Id.ToString());
        Assert.Contains(await db.OutboxMessages.ToListAsync(), m => m.Key == survivor.Id.ToString() && m.DeadLetteredAt != null);
    }

    [Fact]
    public async Task Recently_published_messages_are_not_purged()
    {
        var account = await NewAccountAsync("Checking", 100);
        await DrainAsync();

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().PurgeProcessedAsync(TimeSpan.FromDays(7), CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<MemberPulseDbContext>();
        Assert.Contains(await db.OutboxMessages.ToListAsync(), m => m.Key == account.Id.ToString());
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
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
    private async Task DrainAsync()
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        while (await processor.ProcessBatchAsync(50, CancellationToken.None) > 0) { }
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
        await DrainAsync(); // nothing left, must not re-publish

        var events = EventsFor(account.Id);
        Assert.Equal([500m, -10m, -20m], events.Select(e => e.Amount));
    }
}

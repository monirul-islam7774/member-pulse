using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Api.Controllers;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Events;
using MemberPulse.Domain.Staff;
using MemberPulse.Infrastructure.Campaigns;
using MemberPulse.Infrastructure.Messaging;
using MemberPulse.Infrastructure.Persistence;
using MemberPulse.Tests.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MemberPulse.Tests.Integration;

/// <summary>
/// Real SQL Server, but the clock, publisher and message sender are ours and the background loops are off,
/// so the retry/lockout/dead-letter queries can be driven step by step.
/// </summary>
public sealed class ControlledRealFactory : RealInfrastructureFactory
{
    public OffsetTimeProvider Clock { get; } = new();
    public ControllableMessageSender Sender { get; } = new();

    protected override void ConfigureOverrides(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "",
            ["Kafka:DispatcherEnabled"] = "false",
            ["Campaigns:RetryWorkerEnabled"] = "false",
            ["Security:LoginPermitsPerMinute"] = "1000",
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<RecordingEventPublisher>();
            services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<RecordingEventPublisher>());
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IMessageSender>();
            services.AddSingleton<IMessageSender>(Sender);
        });
    }
}

public class RealSqlHardeningTests(ControlledRealFactory factory) : IClassFixture<ControlledRealFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private RecordingEventPublisher Publisher => factory.Services.GetRequiredService<RecordingEventPublisher>();

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@memberpulse.local", password = "Admin_Dev123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>(Json))!.AccessToken);
        return client;
    }

    private static async Task<AccountDto> NewAccountAsync(HttpClient client, decimal deposit)
    {
        var member = await (await client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Real", lastName = "Hardening", email = $"h-{Guid.NewGuid():N}@example.com", dateOfBirth = "1990-01-01",
        })).Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        var open = await client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts", new { type = "Checking", initialDeposit = deposit });
        return (await open.Content.ReadFromJsonAsync<AccountDto>(Json))!;
    }

    private async Task DrainAsync(int maxAttempts = 20)
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        while (await processor.ProcessBatchAsync(50, maxAttempts, CancellationToken.None) > 0) { }
    }

    private List<decimal> AmountsFor(Guid accountId) => Publisher.Published
        .Where(p => p.Key == accountId.ToString())
        .Select(p => JsonSerializer.Deserialize<TransactionPosted>(p.Payload, Json)!.Amount)
        .ToList();

    [IntegrationFact]
    public async Task Outbox_backoff_ordering_dead_letters_and_purge_work_on_sql_server()
    {
        var admin = await AdminAsync();
        var flaky = await NewAccountAsync(admin, 500);
        var healthy = await NewAccountAsync(admin, 700);

        // One account's publishes fail: it backs off while the other carries on.
        Publisher.FailKey(flaky.Id.ToString());
        try { await DrainAsync(); }
        finally { Publisher.FailKey(flaky.Id.ToString(), fail: false); }
        Assert.Empty(AmountsFor(flaky.Id));
        Assert.Equal([700m], AmountsFor(healthy.Id));

        // A newer transaction must not overtake the one that is backing off.
        await admin.PostAsJsonAsync($"/api/accounts/{flaky.Id}/transactions", new { amount = -5, description = "Newer" });
        await DrainAsync();
        Assert.Empty(AmountsFor(flaky.Id));

        // Once the backoff has passed, both go out in order.
        using (factory.Clock.Advance(TimeSpan.FromMinutes(1))) await DrainAsync();
        Assert.Equal([500m, -5m], AmountsFor(flaky.Id));

        // A message that keeps failing is dead-lettered and shows up for admins.
        var doomed = await NewAccountAsync(admin, 900);
        Publisher.FailKey(doomed.Id.ToString());
        try { await DrainAsync(maxAttempts: 1); }
        finally { Publisher.FailKey(doomed.Id.ToString(), fail: false); }
        var dead = await admin.GetFromJsonAsync<List<DeadLetterDto>>("/api/admin/outbox/dead", Json);
        var letter = Assert.Single(dead!, d => d.Key == doomed.Id.ToString());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/outbox/{letter.Id}/retry", null)).StatusCode);
        await DrainAsync();
        Assert.Equal([900m], AmountsFor(doomed.Id));

        // Published rows are purged after the retention period; nothing pending is lost.
        using (factory.Clock.Advance(TimeSpan.FromDays(8)))
        {
            using var scope = factory.Services.CreateScope();
            var purged = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>()
                .PurgeProcessedAsync(TimeSpan.FromDays(7), CancellationToken.None);
            Assert.True(purged >= 4);
        }
    }

    [IntegrationFact]
    public async Task Login_lockout_and_unlock_work_on_sql_server()
    {
        var admin = await AdminAsync();
        var email = $"locked-{Guid.NewGuid():N}@example.com";
        const string password = "A-Long-Password-1";
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync("/api/users", new { email, password, role = "Staff" })).StatusCode);
        var client = factory.CreateClient();

        for (var i = 0; i < StaffUser.MaxFailedLogins; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "nope-nope-nope" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);

        using (factory.Clock.Advance(StaffUser.LockoutDuration + TimeSpan.FromMinutes(1)))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
        }

        // Lock again, then clear it as an admin.
        for (var i = 0; i < StaffUser.MaxFailedLogins; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "nope-nope-nope" });
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/users/unlock", new { email })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
    }

    [IntegrationFact]
    public async Task Failed_campaign_sends_are_retried_on_sql_server_and_reported_as_pending_until_sent()
    {
        var admin = await AdminAsync();
        var account = await NewAccountAsync(admin, 900_000);
        var run = Guid.NewGuid().ToString("N")[..8];
        var segment = await (await admin.PostAsJsonAsync("/api/segments", new
        {
            name = $"Hardening whales {run}",
            criteria = new[] { new { metric = "TotalBalance", @operator = "GreaterThanOrEqual", value = 500_000 } },
        })).Content.ReadFromJsonAsync<SegmentDto>(Json);
        var campaign = await (await admin.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"Hardening campaign {run}", segmentId = segment!.Id, channel = "Sms",
            messageTemplate = "Hello {FirstName}", direction = "Debit",
        })).Content.ReadFromJsonAsync<CampaignDto>(Json);

        factory.Sender.Fail = true;
        try
        {
            await admin.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new { amount = -25, description = "Purchase" });
            await DrainAsync();
            using var scope = factory.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<TransactionEventHandler>();
            foreach (var p in Publisher.Published.Where(p => p.Key == account.Id.ToString()))
            {
                await handler.HandleAsync(JsonSerializer.Deserialize<TransactionPosted>(p.Payload, Json)!, CancellationToken.None);
            }
        }
        finally { factory.Sender.Fail = false; }

        var pending = (await admin.GetFromJsonAsync<CampaignDetailReportDto>($"/api/reports/campaigns/{campaign!.Id}", Json))!.Campaign.Kpis;
        Assert.Equal((0, 1), (pending.Sent, pending.Pending));

        using (factory.Clock.Advance(TimeSpan.FromMinutes(1)))
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DeliveryRetrier>().ProcessDueAsync(100, CancellationToken.None);
        }

        var sent = (await admin.GetFromJsonAsync<CampaignDetailReportDto>($"/api/reports/campaigns/{campaign.Id}", Json))!.Campaign.Kpis;
        Assert.Equal((1, 0), (sent.Sent, sent.Pending));
    }
}

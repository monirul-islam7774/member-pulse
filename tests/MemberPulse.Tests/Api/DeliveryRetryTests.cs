using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Events;
using MemberPulse.Infrastructure.Campaigns;
using MemberPulse.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MemberPulse.Tests.Api;

public class DeliveryRetryTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    /// <summary>Member in a one-off campaign that messages them on any purchase. Returns the account and campaign.</summary>
    private async Task<(Guid AccountId, Guid CampaignId)> SetUpAsync()
    {
        var create = await _client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Retry", lastName = "Test", email = $"r-{Guid.NewGuid():N}@example.com", dateOfBirth = "1990-01-01",
        });
        var member = await create.Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        var open = await _client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts", new { type = "Checking", initialDeposit = 900_000 });
        var account = await open.Content.ReadFromJsonAsync<AccountDto>(Json);

        var seg = await _client.PostAsJsonAsync("/api/segments", new
        {
            name = $"Retry whales {Guid.NewGuid():N}",
            criteria = new[] { new { metric = "TotalBalance", @operator = "GreaterThanOrEqual", value = 500_000 } },
        });
        var segmentId = (await seg.Content.ReadFromJsonAsync<SegmentDto>(Json))!.Id;

        var camp = await _client.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"Retry campaign {Guid.NewGuid():N}", segmentId, channel = "Email",
            messageTemplate = "Hello {FirstName}", direction = "Debit",
        });
        return (account!.Id, (await camp.Content.ReadFromJsonAsync<CampaignDto>(Json))!.Id);
    }

    private async Task SpendAsync(Guid accountId, decimal amount) =>
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/transactions", new { amount = -amount, description = "Purchase" });

    /// <summary>Publishes the account's pending events and runs them through the event handler.</summary>
    private async Task HandleEventsAsync(Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(100, 20, CancellationToken.None);

        var handler = scope.ServiceProvider.GetRequiredService<TransactionEventHandler>();
        var events = factory.Services.GetRequiredService<RecordingEventPublisher>().Published
            .Where(p => p.Key == accountId.ToString())
            .Select(p => JsonSerializer.Deserialize<TransactionPosted>(p.Payload, Json)!)
            .OrderBy(e => e.OccurredAt);
        foreach (var e in events)
        {
            try { await handler.HandleAsync(e, CancellationToken.None); }
            catch (OperationCanceledException) { /* a simulated crash */ }
        }
    }

    /// <summary>Runs the retry worker once. It handles every due delivery in the app, so tests check their own delivery.</summary>
    private async Task RetryAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DeliveryRetrier>().ProcessDueAsync(100, CancellationToken.None);
    }

    private async Task<DeliveryDto> DeliveryAsync(Guid campaignId) =>
        Assert.Single((await _client.GetFromJsonAsync<PagedResult<DeliveryDto>>($"/api/campaigns/{campaignId}/deliveries", Json))!.Items);

    private Task<CampaignDetailReportDto?> ReportAsync(Guid campaignId) =>
        _client.GetFromJsonAsync<CampaignDetailReportDto>($"/api/reports/campaigns/{campaignId}", Json);

    [Fact]
    public async Task A_failed_send_is_kept_pending_then_retried_when_due()
    {
        var (accountId, campaignId) = await SetUpAsync();
        factory.Sender.Fail = true;
        try
        {
            await SpendAsync(accountId, 25);
            await HandleEventsAsync(accountId);
        }
        finally { factory.Sender.Fail = false; }

        var first = await DeliveryAsync(campaignId);
        Assert.Equal(DeliveryStatus.Pending, first.Status);
        Assert.Equal(1, first.Attempts);
        Assert.Contains("provider unavailable", first.Error);
        var report = (await ReportAsync(campaignId))!.Campaign.Kpis;
        Assert.Equal((0, 0, 1), (report.Sent, report.Failed, report.Pending));

        await RetryAsync(); // not due yet: first backoff is 30 seconds
        Assert.Equal(1, (await DeliveryAsync(campaignId)).Attempts);

        using (factory.Clock.Advance(TimeSpan.FromSeconds(31)))
        {
            await RetryAsync();
        }

        var retried = await DeliveryAsync(campaignId);
        Assert.Equal(DeliveryStatus.Sent, retried.Status);
        Assert.Equal(2, retried.Attempts);
        Assert.Null(retried.Error);
        report = (await ReportAsync(campaignId))!.Campaign.Kpis;
        Assert.Equal((1, 0, 0), (report.Sent, report.Failed, report.Pending));
    }

    [Fact]
    public async Task Giving_up_after_the_last_attempt_marks_the_delivery_failed_and_stops_retrying()
    {
        var (accountId, campaignId) = await SetUpAsync();
        var skipped = new List<IDisposable>();
        factory.Sender.Fail = true;
        try
        {
            await SpendAsync(accountId, 25);
            await HandleEventsAsync(accountId); // attempt 1

            // Attempts 2 to 5, each after the previous backoff (30s, 2m, 8m, 32m). The clock keeps moving forward.
            foreach (var wait in new[] { 31, 121, 481, 1921 })
            {
                skipped.Add(factory.Clock.Advance(TimeSpan.FromSeconds(wait)));
                await RetryAsync();
            }
        }
        finally
        {
            factory.Sender.Fail = false;
            foreach (var s in skipped) s.Dispose();
        }

        var delivery = await DeliveryAsync(campaignId);
        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        Assert.Equal(CampaignDelivery.MaxAttempts, delivery.Attempts);
        var report = (await ReportAsync(campaignId))!.Campaign.Kpis;
        Assert.Equal((0, 1, 0), (report.Sent, report.Failed, report.Pending));

        using (factory.Clock.Advance(TimeSpan.FromDays(1)))
        {
            await RetryAsync();
        }

        Assert.Equal(CampaignDelivery.MaxAttempts, (await DeliveryAsync(campaignId)).Attempts); // gave up for good
    }

    [Fact]
    public async Task A_send_lost_mid_flight_is_recovered_after_the_claim_times_out()
    {
        var (accountId, campaignId) = await SetUpAsync();
        factory.Sender.Crash = true;
        try
        {
            await SpendAsync(accountId, 25);
            await HandleEventsAsync(accountId); // process "dies" while sending
        }
        finally { factory.Sender.Crash = false; }

        Assert.Equal(DeliveryStatus.Pending, (await DeliveryAsync(campaignId)).Status);
        using (factory.Clock.Advance(TimeSpan.FromMinutes(1)))
        {
            await RetryAsync(); // still within the claim window: it may yet finish
        }

        Assert.Equal(DeliveryStatus.Pending, (await DeliveryAsync(campaignId)).Status);

        using (factory.Clock.Advance(TimeSpan.FromMinutes(6)))
        {
            await RetryAsync();
        }

        Assert.Equal(DeliveryStatus.Sent, (await DeliveryAsync(campaignId)).Status);
    }

    [Fact]
    public async Task A_pending_delivery_cannot_convert_until_it_is_actually_sent()
    {
        var (accountId, campaignId) = await SetUpAsync();
        factory.Sender.Fail = true;
        try
        {
            await SpendAsync(accountId, 25);
            await HandleEventsAsync(accountId);
        }
        finally { factory.Sender.Fail = false; }

        await SpendAsync(accountId, 40); // a purchase that would convert a sent message
        await HandleEventsAsync(accountId);

        var delivery = (await _client.GetFromJsonAsync<PagedResult<DeliveryDto>>($"/api/campaigns/{campaignId}/deliveries", Json))!
            .Items.Single(d => d.Status == DeliveryStatus.Pending);
        Assert.Null(delivery.ConvertedAt);
    }
}

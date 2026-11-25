using System.Net;
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

public class CampaignsApiTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private async Task<(Guid MemberId, Guid AccountId)> NewMemberAsync(string type, decimal deposit, string firstName = "Aisha")
    {
        var create = await _client.PostAsJsonAsync("/api/members", new
        {
            firstName, lastName = "Camp", email = $"c-{Guid.NewGuid():N}@example.com", dateOfBirth = "1990-01-01",
        });
        var member = await create.Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        var open = await _client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts", new { type, initialDeposit = deposit });
        var account = await open.Content.ReadFromJsonAsync<AccountDto>(Json);
        return (member.Id, account!.Id);
    }

    /// <summary>A segment only the given member can be in: balance of exactly this amount.</summary>
    private async Task<Guid> SegmentForBalanceAsync(decimal balance)
    {
        var response = await _client.PostAsJsonAsync("/api/segments", new
        {
            name = $"Balance {balance} {Guid.NewGuid():N}",
            criteria = new[] { new { metric = "TotalBalance", @operator = "GreaterThanOrEqual", value = balance } },
        });
        return (await response.Content.ReadFromJsonAsync<SegmentDto>(Json))!.Id;
    }

    private async Task<CampaignDto> NewCampaignAsync(Guid segmentId, object? overrides = null, int cooldownDays = 0)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = $"Campaign {Guid.NewGuid():N}",
            ["segmentId"] = segmentId,
            ["channel"] = "Email",
            ["messageTemplate"] = "Hi {FirstName}: ${Amount} at {MerchantCategory}",
            ["direction"] = "Debit",
            ["cooldownDays"] = cooldownDays,
        };
        if (overrides is not null)
        {
            foreach (var p in overrides.GetType().GetProperties()) body[p.Name] = p.GetValue(overrides);
        }

        var response = await _client.PostAsJsonAsync("/api/campaigns", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CampaignDto>(Json))!;
    }

    private async Task<Guid> SpendAsync(Guid accountId, decimal amount, string category)
    {
        var response = await _client.PostAsJsonAsync($"/api/accounts/{accountId}/transactions",
            new { amount = -amount, description = "Purchase", merchantCategory = category });
        return (await response.Content.ReadFromJsonAsync<TransactionDto>(Json))!.Id;
    }

    /// <summary>Publishes pending events, then feeds the recorded ones to the engine, as the Kafka consumer would.</summary>
    private async Task<int> RunEngineAsync(Guid accountId, int times = 1)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(100, CancellationToken.None);

        var recorder = factory.Services.GetRequiredService<RecordingEventPublisher>();
        var events = recorder.Published
            .Where(p => p.Key == accountId.ToString())
            .Select(p => JsonSerializer.Deserialize<TransactionPosted>(p.Payload, Json)!)
            .OrderBy(e => e.OccurredAt)
            .ToList();

        var engine = scope.ServiceProvider.GetRequiredService<TriggerEngine>();
        var triggered = 0;
        for (var i = 0; i < times; i++)
        {
            foreach (var e in events) triggered += await engine.HandleAsync(e, CancellationToken.None);
        }
        return triggered;
    }

    private async Task<List<DeliveryDto>> DeliveriesAsync(Guid campaignId) =>
        (await _client.GetFromJsonAsync<PagedResult<DeliveryDto>>($"/api/campaigns/{campaignId}/deliveries", Json))!.Items.ToList();

    [Fact]
    public async Task Matching_transaction_triggers_a_rendered_message()
    {
        var (memberId, accountId) = await NewMemberAsync("Checking", 800_000);
        var campaign = await NewCampaignAsync(await SegmentForBalanceAsync(500_000), new { minAmount = 100m, merchantCategory = "Travel" });
        var txId = await SpendAsync(accountId, 1234.5m, "Travel");

        await RunEngineAsync(accountId);

        var delivery = Assert.Single(await DeliveriesAsync(campaign.Id));
        Assert.Equal(memberId, delivery.MemberId);
        Assert.Equal(txId, delivery.TransactionId);
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.Equal("Hi Aisha: $1234.50 at Travel", delivery.Message);
    }

    [Fact]
    public async Task Member_outside_the_segment_gets_nothing()
    {
        var (_, accountId) = await NewMemberAsync("Checking", 50);
        var campaign = await NewCampaignAsync(await SegmentForBalanceAsync(500_000));
        await SpendAsync(accountId, 10, "Dining");

        await RunEngineAsync(accountId);

        Assert.Empty(await DeliveriesAsync(campaign.Id));
    }

    [Fact]
    public async Task Transaction_that_does_not_match_the_filter_triggers_nothing()
    {
        var (_, accountId) = await NewMemberAsync("Checking", 800_000);
        var campaign = await NewCampaignAsync(await SegmentForBalanceAsync(500_000), new { merchantCategory = "Travel" });
        await SpendAsync(accountId, 20, "Dining");

        await RunEngineAsync(accountId);

        Assert.Empty(await DeliveriesAsync(campaign.Id));
    }

    [Fact]
    public async Task Replaying_the_same_events_does_not_send_twice()
    {
        var (_, accountId) = await NewMemberAsync("Checking", 800_000);
        var campaign = await NewCampaignAsync(await SegmentForBalanceAsync(500_000));
        await SpendAsync(accountId, 20, "Dining");

        await RunEngineAsync(accountId, times: 3);

        Assert.Single(await DeliveriesAsync(campaign.Id));
    }

    [Fact]
    public async Task Cooldown_limits_a_member_to_one_message_per_period()
    {
        var (_, accountId) = await NewMemberAsync("Checking", 800_000);
        var campaign = await NewCampaignAsync(await SegmentForBalanceAsync(500_000), cooldownDays: 7);
        await SpendAsync(accountId, 10, "Dining");
        await SpendAsync(accountId, 20, "Dining");
        await SpendAsync(accountId, 30, "Dining");

        await RunEngineAsync(accountId);

        Assert.Single(await DeliveriesAsync(campaign.Id));
    }

    [Fact]
    public async Task Without_a_cooldown_every_matching_transaction_triggers()
    {
        var (_, accountId) = await NewMemberAsync("Checking", 800_000);
        var campaign = await NewCampaignAsync(await SegmentForBalanceAsync(500_000), cooldownDays: 0);
        await SpendAsync(accountId, 10, "Dining");
        await SpendAsync(accountId, 20, "Dining");

        await RunEngineAsync(accountId);

        Assert.Equal(2, (await DeliveriesAsync(campaign.Id)).Count);
    }

    [Fact]
    public async Task Paused_campaign_is_skipped_and_resumes_afterwards()
    {
        var (_, accountId) = await NewMemberAsync("Checking", 800_000);
        var campaign = await NewCampaignAsync(await SegmentForBalanceAsync(500_000));
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/campaigns/{campaign.Id}/pause", null)).StatusCode);
        await SpendAsync(accountId, 10, "Dining");

        await RunEngineAsync(accountId);
        Assert.Empty(await DeliveriesAsync(campaign.Id));

        var resumed = await _client.PostAsync($"/api/campaigns/{campaign.Id}/resume", null);
        Assert.Equal(CampaignStatus.Active, (await resumed.Content.ReadFromJsonAsync<CampaignDto>(Json))!.Status);
        await RunEngineAsync(accountId);
        Assert.Single(await DeliveriesAsync(campaign.Id));
    }

    [Fact]
    public async Task Creating_with_an_unknown_segment_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"Orphan {Guid.NewGuid():N}", segmentId = Guid.NewGuid(), channel = "Sms", messageTemplate = "Hello",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_name_returns_409_and_unknown_campaign_returns_404()
    {
        var campaign = await NewCampaignAsync(await SegmentForBalanceAsync(1));

        var again = await _client.PostAsJsonAsync("/api/campaigns", new
        {
            name = campaign.Name, segmentId = campaign.SegmentId, channel = "Email", messageTemplate = "Hello",
        });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/campaigns/{Guid.NewGuid()}/deliveries")).StatusCode);
    }
}

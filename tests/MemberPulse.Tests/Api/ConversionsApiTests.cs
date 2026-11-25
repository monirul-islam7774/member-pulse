using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Events;
using MemberPulse.Infrastructure.Campaigns;
using MemberPulse.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MemberPulse.Tests.Api;

public class ConversionsApiTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private async Task<(Guid MemberId, Guid AccountId)> NewMemberAsync()
    {
        var create = await _client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Nadia", lastName = "Conv", email = $"v-{Guid.NewGuid():N}@example.com", dateOfBirth = "1990-01-01",
        });
        var member = await create.Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        var open = await _client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts", new { type = "Checking", initialDeposit = 800_000 });
        return (member.Id, (await open.Content.ReadFromJsonAsync<AccountDto>(Json))!.Id);
    }

    /// <summary>Triggers on any dining debit; converts on a travel debit of at least 100 within the window.</summary>
    private async Task<CampaignDto> NewCampaignAsync(int windowDays = 7)
    {
        var segment = await _client.PostAsJsonAsync("/api/segments", new
        {
            name = $"Rich {Guid.NewGuid():N}",
            criteria = new[] { new { metric = "TotalBalance", @operator = "GreaterThanOrEqual", value = 500_000 } },
        });
        var segmentId = (await segment.Content.ReadFromJsonAsync<SegmentDto>(Json))!.Id;

        var response = await _client.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"Travel push {Guid.NewGuid():N}",
            segmentId,
            channel = "Push",
            messageTemplate = "Planning a trip, {FirstName}?",
            direction = "Debit",
            merchantCategory = "Dining",
            cooldownDays = 30,
            conversionWindowDays = windowDays,
            conversionDirection = "Debit",
            conversionMinAmount = 100,
            conversionMerchantCategory = "Travel",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CampaignDto>(Json))!;
    }

    private async Task SpendAsync(Guid accountId, decimal amount, string category) =>
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/transactions",
            new { amount = -amount, description = "Purchase", merchantCategory = category });

    /// <summary>Publishes pending events and feeds them to the handler, as the Kafka consumer would. Safe to repeat.</summary>
    private async Task ProcessAsync(Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(100, 20, CancellationToken.None);

        var events = factory.Services.GetRequiredService<RecordingEventPublisher>().Published
            .Where(p => p.Key == accountId.ToString())
            .Select(p => JsonSerializer.Deserialize<TransactionPosted>(p.Payload, Json)!)
            .OrderBy(e => e.OccurredAt);

        var handler = scope.ServiceProvider.GetRequiredService<TransactionEventHandler>();
        foreach (var e in events) await handler.HandleAsync(e, CancellationToken.None);
    }

    private async Task<List<DeliveryDto>> DeliveriesAsync(Guid campaignId) =>
        (await _client.GetFromJsonAsync<PagedResult<DeliveryDto>>($"/api/campaigns/{campaignId}/deliveries", Json))!.Items.ToList();

    private Task<CampaignDetailReportDto?> DetailAsync(Guid campaignId) =>
        _client.GetFromJsonAsync<CampaignDetailReportDto>($"/api/reports/campaigns/{campaignId}", Json);

    [Fact]
    public async Task Qualifying_purchase_after_the_message_is_recorded_as_a_conversion()
    {
        var (_, accountId) = await NewMemberAsync();
        var campaign = await NewCampaignAsync();
        await SpendAsync(accountId, 30, "Dining");
        await ProcessAsync(accountId);
        Assert.Null(Assert.Single(await DeliveriesAsync(campaign.Id)).ConvertedAt);

        await SpendAsync(accountId, 640.25m, "Travel");
        await ProcessAsync(accountId);
        await ProcessAsync(accountId); // replaying changes nothing

        var delivery = Assert.Single(await DeliveriesAsync(campaign.Id));
        Assert.NotNull(delivery.ConvertedAt);
        Assert.Equal(640.25m, delivery.ConversionAmount);
    }

    [Fact]
    public async Task Purchase_that_misses_the_goal_does_not_convert()
    {
        var (_, accountId) = await NewMemberAsync();
        var campaign = await NewCampaignAsync();
        await SpendAsync(accountId, 30, "Dining");
        await ProcessAsync(accountId);

        await SpendAsync(accountId, 20, "Travel");     // too small
        await SpendAsync(accountId, 900, "Groceries"); // wrong category
        await ProcessAsync(accountId);

        Assert.Null(Assert.Single(await DeliveriesAsync(campaign.Id)).ConvertedAt);
    }

    [Fact]
    public async Task Report_shows_sent_converted_rate_and_amount()
    {
        var campaign = await NewCampaignAsync();
        var (_, converting) = await NewMemberAsync();
        var (_, silent) = await NewMemberAsync();
        await SpendAsync(converting, 30, "Dining");
        await SpendAsync(silent, 30, "Dining");
        await ProcessAsync(converting);
        await ProcessAsync(silent);
        await SpendAsync(converting, 400, "Travel");
        await ProcessAsync(converting);

        var report = await DetailAsync(campaign.Id);

        var kpis = report!.Campaign.Kpis;
        Assert.Equal(2, kpis.Sent);
        Assert.Equal(0, kpis.Failed);
        Assert.Equal(1, kpis.Converted);
        Assert.Equal(0.5m, kpis.ConversionRate);
        Assert.Equal(2, kpis.MembersReached);
        Assert.Equal(400m, kpis.ConvertedAmount);
        var today = Assert.Single(report.Daily);
        Assert.Equal((2, 1), (today.Sent, today.Converted));
    }

    [Fact]
    public async Task Overview_lists_the_campaign_and_totals_cover_it()
    {
        var (_, accountId) = await NewMemberAsync();
        var campaign = await NewCampaignAsync();
        await SpendAsync(accountId, 30, "Dining");
        await ProcessAsync(accountId);

        var report = await _client.GetFromJsonAsync<CampaignReportDto>("/api/reports/campaigns", Json);

        var row = Assert.Single(report!.Campaigns, c => c.CampaignId == campaign.Id);
        Assert.Equal(1, row.Kpis.Sent);
        Assert.True(report.Totals.Sent >= 1);
        Assert.True(report.Totals.MembersReached >= 1);
    }

    [Fact]
    public async Task Campaign_with_no_messages_reports_zeroes_without_dividing_by_zero()
    {
        var campaign = await NewCampaignAsync();

        var report = await DetailAsync(campaign.Id);

        Assert.Equal(new KpiDto(0, 0, 0, 0, 0m, 0, 0m), report!.Campaign.Kpis);
        Assert.Empty(report.Daily);
    }

    [Fact]
    public async Task Messages_outside_the_requested_range_are_excluded()
    {
        var (_, accountId) = await NewMemberAsync();
        var campaign = await NewCampaignAsync();
        await SpendAsync(accountId, 30, "Dining");
        await ProcessAsync(accountId);
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-60).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("O"));

        var report = await _client.GetFromJsonAsync<CampaignDetailReportDto>($"/api/reports/campaigns/{campaign.Id}?from={from}&to={to}", Json);

        Assert.Equal(0, report!.Campaign.Kpis.Sent);
    }

    [Fact]
    public async Task Bad_ranges_return_400_and_unknown_campaign_returns_404()
    {
        var now = DateTimeOffset.UtcNow;
        string Q(DateTimeOffset d) => Uri.EscapeDataString(d.ToString("O"));

        var reversed = await _client.GetAsync($"/api/reports/campaigns?from={Q(now)}&to={Q(now.AddDays(-1))}");
        var tooLong = await _client.GetAsync($"/api/reports/campaigns?from={Q(now.AddDays(-400))}&to={Q(now)}");
        var unknown = await _client.GetAsync($"/api/reports/campaigns/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Invalid_conversion_window_is_rejected()
    {
        var segment = await _client.PostAsJsonAsync("/api/segments", new
        {
            name = $"Any {Guid.NewGuid():N}",
            criteria = new[] { new { metric = "Age", @operator = "GreaterThan", value = 0 } },
        });
        var segmentId = (await segment.Content.ReadFromJsonAsync<SegmentDto>(Json))!.Id;

        var response = await _client.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"Bad {Guid.NewGuid():N}", segmentId, channel = "Email", messageTemplate = "x", conversionWindowDays = 200,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Conversion_direction_Any_is_stored_as_Any()
    {
        var segment = await _client.PostAsJsonAsync("/api/segments", new
        {
            name = $"Any {Guid.NewGuid():N}",
            criteria = new[] { new { metric = "Age", @operator = "GreaterThan", value = 0 } },
        });
        var segmentId = (await segment.Content.ReadFromJsonAsync<SegmentDto>(Json))!.Id;

        var create = await _client.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"Anything {Guid.NewGuid():N}", segmentId, channel = "Email", messageTemplate = "x",
            conversionDirection = "Any", conversionWindowDays = 30,
        });
        var created = await create.Content.ReadFromJsonAsync<CampaignDto>(Json);

        var fetched = await _client.GetFromJsonAsync<CampaignDto>($"/api/campaigns/{created!.Id}", Json);
        Assert.Equal(MemberPulse.Domain.Campaigns.TransactionDirection.Any, fetched!.Conversion.Direction);
        Assert.Equal(30, fetched.Conversion.WindowDays);
    }
}

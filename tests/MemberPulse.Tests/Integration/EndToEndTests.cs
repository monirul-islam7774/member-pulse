using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Campaigns;

namespace MemberPulse.Tests.Integration;

/// <summary>
/// The whole product against real SQL Server and Kafka:
/// transaction -> outbox -> Kafka -> consumer -> trigger engine -> delivery -> conversion -> KPI report.
/// </summary>
public class EndToEndTests : IClassFixture<RealInfrastructureFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90); // topic creation + consumer group join can be slow

    private readonly RealInfrastructureFactory _factory;

    public EndToEndTests(RealInfrastructureFactory factory) => _factory = factory;

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@memberpulse.local", password = "Admin_Dev123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(Json))!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<T> EventuallyAsync<T>(string what, Func<Task<T?>> probe) where T : class
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            var value = await probe();
            if (value is not null) return value;
            await Task.Delay(500);
        }

        throw new TimeoutException($"Timed out after {Patience.TotalSeconds:0}s waiting for: {what}");
    }

    private static async Task<T> OkAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [IntegrationFact]
    public async Task A_purchase_flows_through_kafka_to_a_campaign_message_and_a_conversion()
    {
        var client = await SignedInClientAsync();
        var run = Guid.NewGuid().ToString("N")[..8];

        var member = await OkAsync<MemberDetailDto>(await client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Real", lastName = "Stack", email = $"it-{run}@example.com", dateOfBirth = "1990-01-01",
        }), HttpStatusCode.Created);
        var account = await OkAsync<AccountDto>(await client.PostAsJsonAsync($"/api/members/{member.Id}/accounts",
            new { type = "Checking", initialDeposit = 900_000 }), HttpStatusCode.Created);

        var segment = await OkAsync<SegmentDto>(await client.PostAsJsonAsync("/api/segments", new
        {
            name = $"IT whales {run}",
            criteria = new[] { new { metric = "TotalBalance", @operator = "GreaterThanOrEqual", value = 500_000 } },
        }), HttpStatusCode.Created);
        var inSegments = await client.GetFromJsonAsync<List<SegmentDto>>($"/api/members/{member.Id}/segments", Json);
        Assert.Contains(inSegments!, s => s.Id == segment.Id);

        var campaign = await OkAsync<CampaignDto>(await client.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"IT campaign {run}",
            segmentId = segment.Id,
            channel = "Email",
            messageTemplate = "Hi {FirstName}, ${Amount} on {MerchantCategory}?",
            direction = "Debit",
            merchantCategory = "Dining",
            cooldownDays = 30,
            conversionWindowDays = 7,
            conversionDirection = "Debit",
            conversionMinAmount = 100,
            conversionMerchantCategory = "Travel",
        }), HttpStatusCode.Created);

        // An overdraft is refused and must not produce an event.
        var overdraft = await client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions", new { amount = -9_999_999, description = "Too much" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overdraft.StatusCode);

        // 1. A dining purchase should trigger the campaign.
        await OkAsync<TransactionDto>(await client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions",
            new { amount = -45.5, description = "Dinner", merchantCategory = "Dining" }), HttpStatusCode.Created);

        var delivery = await EventuallyAsync("the campaign message to be sent", async () =>
            (await client.GetFromJsonAsync<PagedResult<DeliveryDto>>($"/api/campaigns/{campaign.Id}/deliveries", Json))!.Items
                .FirstOrDefault());
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.Equal("Hi Real, $45.50 on Dining?", delivery.Message);
        Assert.Null(delivery.ConvertedAt);

        // 2. A qualifying travel purchase afterwards should convert it.
        await OkAsync<TransactionDto>(await client.PostAsJsonAsync($"/api/accounts/{account.Id}/transactions",
            new { amount = -640.25, description = "Flights", merchantCategory = "Travel" }), HttpStatusCode.Created);

        var converted = await EventuallyAsync("the message to convert", async () =>
            (await client.GetFromJsonAsync<PagedResult<DeliveryDto>>($"/api/campaigns/{campaign.Id}/deliveries", Json))!.Items
                .FirstOrDefault(d => d.ConvertedAt is not null));
        Assert.Equal(640.25m, converted.ConversionAmount);

        // 3. The KPI report reflects it, and nothing was sent twice (cooldown + idempotency).
        var report = await client.GetFromJsonAsync<CampaignDetailReportDto>($"/api/reports/campaigns/{campaign.Id}", Json);
        Assert.Equal(new KpiDto(Sent: 1, Failed: 0, Converted: 1, ConversionRate: 1m, MembersReached: 1, ConvertedAmount: 640.25m),
            report!.Campaign.Kpis);
    }

    [IntegrationFact]
    public async Task Health_check_reports_the_real_database_as_healthy_and_auth_is_enforced()
    {
        var anonymous = _factory.CreateClient();

        var health = await anonymous.GetAsync("/health");
        var members = await anonymous.GetAsync("/api/members");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, members.StatusCode);
    }
}

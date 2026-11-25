using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Members;
using MemberPulse.Infrastructure.Campaigns;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MemberPulse.Tests.Integration;

/// <summary>
/// A real SMTP server (Mailpit, from docker-compose or the CI service container) and its HTTP API to read what arrived.
/// Overridable: MEMBERPULSE_IT_SMTP_HOST / _SMTP_PORT / _MAILPIT_API.
/// </summary>
public sealed class MailpitEnvironment
{
    public string SmtpHost { get; } = Environment.GetEnvironmentVariable("MEMBERPULSE_IT_SMTP_HOST") ?? "localhost";
    public int SmtpPort { get; } = int.Parse(Environment.GetEnvironmentVariable("MEMBERPULSE_IT_SMTP_PORT") ?? "1025");
    public string Api { get; } = Environment.GetEnvironmentVariable("MEMBERPULSE_IT_MAILPIT_API") ?? "http://localhost:8025";

    private readonly HttpClient _http = new();

    /// <summary>The plain-text body of the first message sent to <paramref name="address"/>, waiting for it to arrive.</summary>
    public async Task<(string Subject, string Text)> WaitForMailAsync(string address, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;
        while (DateTime.UtcNow < deadline)
        {
            using var search = JsonDocument.Parse(await _http.GetStringAsync($"{Api}/api/v1/search?query={Uri.EscapeDataString($"to:{address}")}"));
            if (search.RootElement.GetProperty("messages").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } first)
            {
                var id = first.GetProperty("ID").GetString();
                using var message = JsonDocument.Parse(await _http.GetStringAsync($"{Api}/api/v1/message/{id}"));
                return (message.RootElement.GetProperty("Subject").GetString()!, message.RootElement.GetProperty("Text").GetString()!);
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"No email to {address} reached Mailpit within {patience.TotalSeconds:0}s.");
    }
}

/// <summary>The whole app on real SQL Server and Kafka, with campaign email pointed at Mailpit.</summary>
public sealed class EmailEndToEndFactory : RealInfrastructureFactory
{
    public MailpitEnvironment Mailpit { get; } = new();

    protected override void ConfigureOverrides(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Host"] = Mailpit.SmtpHost,
            ["Email:Port"] = Mailpit.SmtpPort.ToString(),
            ["Email:Security"] = "None",
            ["Email:FromAddress"] = "noreply@memberpulse.test",
            ["Email:FromName"] = "MemberPulse Test",
            ["Email:Subject"] = "Rewards for you",
        }));
    }
}

public class MailpitTests(EmailEndToEndFactory factory) : IClassFixture<EmailEndToEndFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [IntegrationFact]
    public async Task The_sender_delivers_a_plain_text_email_through_a_real_smtp_server()
    {
        var mailpit = factory.Mailpit;
        var options = Options.Create(new EmailOptions
        {
            Host = mailpit.SmtpHost, Port = mailpit.SmtpPort, Security = EmailSecurity.None,
            FromAddress = "noreply@memberpulse.test", FromName = "MemberPulse Test", Subject = "Direct send",
        });
        var address = $"direct-{Guid.NewGuid():N}@example.com";
        var member = Member.Create("Noor", "Direct", address, new DateOnly(1990, 1, 1), DateTimeOffset.UtcNow);

        await new EmailMessageSender(options).SendAsync(CampaignChannel.Email, member, "Hello Noor, a real email.", CancellationToken.None);

        var (subject, text) = await mailpit.WaitForMailAsync(address, TimeSpan.FromSeconds(20));
        Assert.Equal("Direct send", subject);
        Assert.Equal("Hello Noor, a real email.", text.Trim());
    }

    [IntegrationFact]
    public async Task A_purchase_triggers_a_campaign_and_the_member_receives_the_email()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@memberpulse.local", password = "Admin_Dev123!" });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>(Json))!.AccessToken);

        var run = Guid.NewGuid().ToString("N")[..8];
        var address = $"mail-{run}@example.com";
        var member = await (await client.PostAsJsonAsync("/api/members",
            new { firstName = "Layla", lastName = "Inbox", email = address, dateOfBirth = "1990-01-01" })).Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        var account = await (await client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts",
            new { type = "Checking", initialDeposit = 750_000 })).Content.ReadFromJsonAsync<AccountDto>(Json);
        var segment = await (await client.PostAsJsonAsync("/api/segments", new
        {
            name = $"Inbox segment {run}",
            criteria = new[] { new { metric = "Age", @operator = "GreaterThanOrEqual", value = 18 }, new { metric = "TotalBalance", @operator = "GreaterThanOrEqual", value = 700_000 } },
        })).Content.ReadFromJsonAsync<SegmentDto>(Json);
        var campaign = await (await client.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"Inbox campaign {run}", segmentId = segment!.Id, channel = "Email",
            messageTemplate = "Hi {FirstName}, your {MerchantCategory} purchase of ${Amount} earns bonus points.",
            direction = "Debit", merchantCategory = "Travel", cooldownDays = 0,
        })).Content.ReadFromJsonAsync<CampaignDto>(Json);

        await client.PostAsJsonAsync($"/api/accounts/{account!.Id}/transactions",
            new { amount = -480.25, description = "Flights", merchantCategory = "Travel" });

        var (subject, text) = await factory.Mailpit.WaitForMailAsync(address, TimeSpan.FromSeconds(90));
        Assert.Equal("Rewards for you", subject);
        Assert.Equal("Hi Layla, your Travel purchase of $480.25 earns bonus points.", text.Trim());

        var deliveries = await client.GetFromJsonAsync<PagedResult<DeliveryDto>>($"/api/campaigns/{campaign!.Id}/deliveries", Json);
        var delivery = Assert.Single(deliveries!.Items);
        Assert.Equal(DeliveryStatus.Sent, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
    }
}

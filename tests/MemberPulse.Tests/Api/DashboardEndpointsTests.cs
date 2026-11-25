using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Events;
using MemberPulse.Domain.Staff;
using MemberPulse.Infrastructure.Campaigns;
using MemberPulse.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MemberPulse.Tests.Api;

public class DashboardEndpointsTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static int _bands;

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    /// <summary>
    /// Each call gets its own balance band (10,000 apart), so only this test's campaigns match its member and
    /// campaigns left behind by other tests in the shared database stay out of the counts.
    /// </summary>
    private async Task<(Guid AccountId, Guid CampaignA, Guid CampaignB)> SetUpTwoCampaignsAsync()
    {
        var balance = 1_000_000 + Interlocked.Increment(ref _bands) * 10_000;
        var member = await (await _client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Daily", lastName = "Test", email = $"d-{Guid.NewGuid():N}@example.com", dateOfBirth = "1990-01-01",
        })).Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        var account = await (await _client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts",
            new { type = "Checking", initialDeposit = balance })).Content.ReadFromJsonAsync<AccountDto>(Json);
        var segment = await (await _client.PostAsJsonAsync("/api/segments", new
        {
            name = $"Daily whales {Guid.NewGuid():N}",
            criteria = new[]
            {
                new { metric = "TotalBalance", @operator = "GreaterThanOrEqual", value = balance - 5_000 },
                new { metric = "TotalBalance", @operator = "LessThanOrEqual", value = balance + 5_000 },
            },
        })).Content.ReadFromJsonAsync<SegmentDto>(Json);

        async Task<Guid> Campaign(string name) => (await (await _client.PostAsJsonAsync("/api/campaigns", new
        {
            name = $"{name} {Guid.NewGuid():N}", segmentId = segment!.Id, channel = "Email",
            messageTemplate = "Hi {FirstName}", direction = "Debit",
        })).Content.ReadFromJsonAsync<CampaignDto>(Json))!.Id;

        return (account!.Id, await Campaign("A"), await Campaign("B"));
    }

    private async Task SpendAndProcessAsync(Guid accountId)
    {
        await _client.PostAsJsonAsync($"/api/accounts/{accountId}/transactions", new { amount = -20, description = "Purchase" });

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(100, 20, CancellationToken.None);
        var handler = scope.ServiceProvider.GetRequiredService<TransactionEventHandler>();
        foreach (var p in factory.Services.GetRequiredService<RecordingEventPublisher>().Published.Where(p => p.Key == accountId.ToString()))
        {
            await handler.HandleAsync(JsonSerializer.Deserialize<TransactionPosted>(p.Payload, Json)!, CancellationToken.None);
        }
    }

    private static (int Sent, int Converted) Today(DailyOverviewDto report)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var day = report.Daily.FirstOrDefault(d => d.Date == today);
        return day is null ? (0, 0) : (day.Sent, day.Converted);
    }

    [Fact]
    public async Task Daily_overview_sums_sent_and_converted_per_utc_day_across_campaigns()
    {
        var before = Today((await _client.GetFromJsonAsync<DailyOverviewDto>("/api/reports/campaigns/daily", Json))!);
        var (accountId, _, _) = await SetUpTwoCampaignsAsync();

        await SpendAndProcessAsync(accountId); // one purchase -> one message from each of the two campaigns

        var after = (await _client.GetFromJsonAsync<DailyOverviewDto>("/api/reports/campaigns/daily", Json))!;
        Assert.Equal(before.Sent + 2, Today(after).Sent);
        Assert.Equal(before.Converted, Today(after).Converted);
        Assert.Equal(after.Daily.Select(d => d.Date).Order(), after.Daily.Select(d => d.Date)); // ascending
    }

    [Fact]
    public async Task Daily_overview_counts_conversions_on_the_day_the_message_was_sent()
    {
        var (accountId, _, _) = await SetUpTwoCampaignsAsync();
        await SpendAndProcessAsync(accountId);
        var before = Today((await _client.GetFromJsonAsync<DailyOverviewDto>("/api/reports/campaigns/daily", Json))!);

        await SpendAndProcessAsync(accountId); // a later debit converts the earlier messages (default goal: any debit)

        var after = Today((await _client.GetFromJsonAsync<DailyOverviewDto>("/api/reports/campaigns/daily", Json))!);
        Assert.True(after.Converted >= before.Converted + 2);
    }

    [Fact]
    public async Task Daily_overview_rejects_reversed_and_oversized_ranges_with_400()
    {
        var now = DateTimeOffset.UtcNow;
        string Q(DateTimeOffset d) => Uri.EscapeDataString(d.ToString("O"));

        var reversed = await _client.GetAsync($"/api/reports/campaigns/daily?from={Q(now)}&to={Q(now.AddDays(-1))}");
        var tooLong = await _client.GetAsync($"/api/reports/campaigns/daily?from={Q(now.AddDays(-400))}&to={Q(now)}");

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Daily_overview_is_empty_when_nothing_was_sent_in_the_range()
    {
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-60).ToString("O"));
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-90).ToString("O"));

        var response = await _client.GetAsync($"/api/reports/campaigns/daily?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<DailyOverviewDto>(Json))!.Daily);
    }

    [Fact]
    public async Task Users_list_is_admin_only_and_shows_lock_state()
    {
        var lockedEmail = $"locked-{Guid.NewGuid():N}@example.com";
        var freeEmail = $"free-{Guid.NewGuid():N}@example.com";
        await factory.AddStaffUserAsync(lockedEmail, "Correct-Horse-9!", StaffRole.Staff);
        await factory.AddStaffUserAsync(freeEmail, "Correct-Horse-9!", StaffRole.Staff);
        var anonymous = factory.CreateClient();
        for (var i = 0; i < StaffUser.MaxFailedLogins; i++)
        {
            await anonymous.PostAsJsonAsync("/api/auth/login", new { email = lockedEmail, password = "wrong-wrong-wrong" });
        }

        var asStaff = await factory.CreateAuthenticatedClient(StaffRole.Staff).GetAsync("/api/users");
        var asAdmin = await factory.CreateAuthenticatedClient(StaffRole.Admin).GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        var users = (await asAdmin.Content.ReadFromJsonAsync<List<StaffUserSummaryDto>>(Json))!;
        var locked = Assert.Single(users, u => u.Email == lockedEmail);
        var free = Assert.Single(users, u => u.Email == freeEmail);
        Assert.True(locked.IsLockedOut);
        Assert.NotNull(locked.LockedUntil);
        Assert.False(free.IsLockedOut);
        Assert.Null(free.LockedUntil);
        Assert.Equal(users.Select(u => u.Email).Order(), users.Select(u => u.Email)); // ordered by email
    }
}

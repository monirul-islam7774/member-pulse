using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;

namespace MemberPulse.Tests.Api;

public class SegmentsApiTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private async Task<Guid> NewMemberWithSavingsAsync(decimal deposit)
    {
        var create = await _client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Seg", lastName = "Test", email = $"s-{Guid.NewGuid():N}@example.com", dateOfBirth = "1990-01-01",
        });
        var member = await create.Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        await _client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts", new { type = "Savings", initialDeposit = deposit });
        return member.Id;
    }

    private async Task<SegmentDto> CreateSegmentAsync(string name, decimal minBalance)
    {
        var response = await _client.PostAsJsonAsync("/api/segments", new
        {
            name,
            criteria = new[] { new { metric = "TotalBalance", @operator = "GreaterThanOrEqual", value = minBalance } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SegmentDto>(Json))!;
    }

    [Fact]
    public async Task Members_endpoint_returns_only_matching_members()
    {
        var rich = await NewMemberWithSavingsAsync(900_000);
        var poor = await NewMemberWithSavingsAsync(10);
        var segment = await CreateSegmentAsync($"Whales {Guid.NewGuid():N}", 500_000);

        var result = await _client.GetFromJsonAsync<PagedResult<MemberSummaryDto>>($"/api/segments/{segment.Id}/members", Json);

        Assert.Contains(result!.Items, m => m.Id == rich);
        Assert.DoesNotContain(result.Items, m => m.Id == poor);
    }

    [Fact]
    public async Task Member_segments_lists_the_segments_they_belong_to()
    {
        var memberId = await NewMemberWithSavingsAsync(750_000);
        var segment = await CreateSegmentAsync($"High balance {Guid.NewGuid():N}", 700_000);

        var segments = await _client.GetFromJsonAsync<List<SegmentDto>>($"/api/members/{memberId}/segments", Json);

        Assert.Contains(segments!, s => s.Id == segment.Id);
    }

    [Fact]
    public async Task Duplicate_name_returns_409()
    {
        var name = $"Dup {Guid.NewGuid():N}";
        await CreateSegmentAsync(name, 1);

        var again = await _client.PostAsJsonAsync("/api/segments", new
        {
            name,
            criteria = new[] { new { metric = "Age", @operator = "GreaterThan", value = 18 } },
        });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Invalid_scope_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/segments", new
        {
            name = $"Bad {Guid.NewGuid():N}",
            criteria = new[] { new { metric = "Age", @operator = "GreaterThan", value = 18, accountType = "Savings" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Empty_criteria_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/segments", new { name = "None", criteria = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_segment_returns_404()
    {
        var response = await _client.GetAsync($"/api/segments/{Guid.NewGuid()}/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

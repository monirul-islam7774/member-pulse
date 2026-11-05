using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Accounts;

namespace MemberPulse.Tests.Api;

public class MembersApiTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail() => $"member-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Create_then_get_returns_the_member()
    {
        var email = UniqueEmail();

        var create = await _client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Aisha",
            lastName = "Rahman",
            email,
            dateOfBirth = "1997-06-21",
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        Assert.NotNull(created);

        var fetched = await _client.GetFromJsonAsync<MemberDetailDto>($"/api/members/{created.Id}", Json);
        Assert.NotNull(fetched);
        Assert.Equal(email, fetched.Email);
    }

    [Fact]
    public async Task Create_with_duplicate_email_returns_409()
    {
        var body = new { firstName = "Dup", lastName = "User", email = UniqueEmail(), dateOfBirth = "1990-01-01" };

        var first = await _client.PostAsJsonAsync("/api/members", body);
        var second = await _client.PostAsJsonAsync("/api/members", body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_with_invalid_payload_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/members", new { firstName = "", lastName = "X", email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_member_returns_404()
    {
        var response = await _client.GetAsync($"/api/members/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenAccount_adds_account_with_initial_balance()
    {
        var create = await _client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Omar", lastName = "Faruk", email = UniqueEmail(), dateOfBirth = "1985-03-15",
        });
        var member = await create.Content.ReadFromJsonAsync<MemberDetailDto>(Json);

        var open = await _client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts", new { type = "Savings", initialDeposit = 5000 });

        Assert.Equal(HttpStatusCode.Created, open.StatusCode);
        var account = await open.Content.ReadFromJsonAsync<AccountDto>(Json);
        Assert.Equal(AccountType.Savings, account!.Type);
        Assert.Equal(5000m, account.Balance);
    }
}
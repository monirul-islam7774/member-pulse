using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;

namespace MemberPulse.Tests.Api;

public class TransactionsApiTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(Guid MemberId, AccountDto Account)> NewAccountAsync(string type, decimal deposit)
    {
        var create = await _client.PostAsJsonAsync("/api/members", new
        {
            firstName = "Test", lastName = "User", email = $"t-{Guid.NewGuid():N}@example.com", dateOfBirth = "1990-01-01",
        });
        var member = await create.Content.ReadFromJsonAsync<MemberDetailDto>(Json);
        var open = await _client.PostAsJsonAsync($"/api/members/{member!.Id}/accounts", new { type, initialDeposit = deposit });
        return (member.Id, (await open.Content.ReadFromJsonAsync<AccountDto>(Json))!);
    }

    private Task<HttpResponseMessage> PostAsync(Guid accountId, object body) =>
        _client.PostAsJsonAsync($"/api/accounts/{accountId}/transactions", body);

    private async Task<decimal> BalanceAsync(Guid memberId, Guid accountId)
    {
        var member = await _client.GetFromJsonAsync<MemberDetailDto>($"/api/members/{memberId}", Json);
        return member!.Accounts.Single(a => a.Id == accountId).Balance;
    }

    [Fact]
    public async Task Post_debit_is_recorded_and_updates_balance()
    {
        var (memberId, account) = await NewAccountAsync("Checking", 1000);

        var response = await PostAsync(account.Id, new { amount = -42.5, description = "Groceries", merchantCategory = "Grocery" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(957.5m, await BalanceAsync(memberId, account.Id));
        var list = await _client.GetFromJsonAsync<PagedResult<TransactionDto>>($"/api/accounts/{account.Id}/transactions", Json);
        Assert.Equal(2, list!.TotalCount); // initial deposit + purchase
        Assert.Equal("Grocery", list.Items.First(t => t.Amount < 0).MerchantCategory);
    }

    [Fact]
    public async Task Overdraft_on_checking_returns_422()
    {
        var (_, account) = await NewAccountAsync("Checking", 100);

        var response = await PostAsync(account.Id, new { amount = -500, description = "Too much" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Credit_card_can_go_negative()
    {
        var (_, account) = await NewAccountAsync("CreditCard", 0);

        var response = await PostAsync(account.Id, new { amount = -250, description = "Electronics", merchantCategory = "Retail" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Zero_amount_returns_400()
    {
        var (_, account) = await NewAccountAsync("Savings", 10);

        var response = await PostAsync(account.Id, new { amount = 0, description = "Nothing" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Future_dated_transaction_returns_400()
    {
        var (_, account) = await NewAccountAsync("Savings", 10);

        var response = await PostAsync(account.Id, new { amount = 5, description = "Later", occurredAt = DateTimeOffset.UtcNow.AddDays(2) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_account_returns_404()
    {
        var response = await PostAsync(Guid.NewGuid(), new { amount = 5, description = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Members;

namespace MemberPulse.Tests.Domain;

public class MemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2026, 4, 11, 31)] // day before birthday
    [InlineData(2026, 4, 12, 32)] // on birthday
    public void AgeOn_accounts_for_whether_birthday_has_passed(int year, int month, int day, int expectedAge)
    {
        var member = Member.Create("Sarah", "Johnson", "sarah@example.com", new DateOnly(1994, 4, 12), Now);

        Assert.Equal(expectedAge, member.AgeOn(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Create_normalizes_email()
    {
        var member = Member.Create("Sarah", "Johnson", "  Sarah@Example.COM ", new DateOnly(1994, 4, 12), Now);

        Assert.Equal("sarah@example.com", member.Email);
    }

    [Fact]
    public void OpenAccount_with_initial_deposit_sets_balance_and_records_transaction()
    {
        var member = Member.Create("Daniel", "Kim", "daniel@example.com", new DateOnly(1988, 9, 30), Now);

        var account = member.OpenAccount(AccountType.Savings, 1_000m, Now);

        Assert.Equal(1_000m, account.Balance);
        Assert.Single(account.Transactions);
        Assert.Single(member.Accounts);
    }

    [Fact]
    public void Debit_beyond_balance_on_deposit_account_is_rejected()
    {
        var member = Member.Create("Maria", "Lopez", "maria@example.com", new DateOnly(2000, 1, 5), Now);
        var savings = member.OpenAccount(AccountType.Savings, 100m, Now);

        Assert.Throws<InvalidOperationException>(() => savings.Post(-150m, "ATM withdrawal", null, Now));
        Assert.Equal(100m, savings.Balance);
    }

    [Fact]
    public void Credit_card_can_go_negative()
    {
        var member = Member.Create("Maria", "Lopez", "maria@example.com", new DateOnly(2000, 1, 5), Now);
        var card = member.OpenAccount(AccountType.CreditCard, 0m, Now);

        card.Post(-250m, "Lowe's", "HomeImprovement", Now);

        Assert.Equal(-250m, card.Balance);
    }
}
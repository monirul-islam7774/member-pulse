using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Members;
using MemberPulse.Domain.Segmentation;

namespace MemberPulse.Tests.Domain;

public class SegmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static Member NewMember(DateOnly? dob = null, DateTimeOffset? joined = null) =>
        Member.Create("A", "B", "a@b.com", dob ?? new DateOnly(1990, 1, 1), joined ?? Now.AddYears(-2));

    [Fact]
    public void Segment_requires_at_least_one_criterion()
    {
        Assert.Throws<ArgumentException>(() => Segment.Create("Empty", null, [], Now));
    }

    [Fact]
    public void All_criteria_must_hold()
    {
        var member = NewMember(new DateOnly(2000, 1, 1)); // 26
        member.OpenAccount(AccountType.Savings, 20_000, Now.AddDays(-100));

        var both = Segment.Create("Young savers", null,
        [
            SegmentCriterion.Create(MemberMetric.Age, ComparisonOperator.LessThan, 30),
            SegmentCriterion.Create(MemberMetric.TotalBalance, ComparisonOperator.GreaterThanOrEqual, 10_000),
        ], Now);
        var impossible = Segment.Create("Young rich", null,
        [
            SegmentCriterion.Create(MemberMetric.Age, ComparisonOperator.LessThan, 30),
            SegmentCriterion.Create(MemberMetric.TotalBalance, ComparisonOperator.GreaterThan, 1_000_000),
        ], Now);

        Assert.True(both.Matches(member, Now));
        Assert.False(impossible.Matches(member, Now));
    }

    [Fact]
    public void Spend_counts_only_debits_in_the_last_30_days_for_the_category()
    {
        var member = NewMember();
        var account = member.OpenAccount(AccountType.Checking, 5_000, Now.AddDays(-90));
        account.Post(-300, "Flight", "Travel", Now.AddDays(-5));
        account.Post(-200, "Hotel", "Travel", Now.AddDays(-10));
        account.Post(-900, "Old trip", "Travel", Now.AddDays(-45)); // outside window
        account.Post(-50, "Lunch", "Dining", Now.AddDays(-2));       // other category
        account.Post(+100, "Refund", "Travel", Now.AddDays(-1));      // credit, ignored

        var travelSpend = SegmentCriterion.Create(MemberMetric.Spend30d, ComparisonOperator.Equal, 500, merchantCategory: "travel");

        Assert.Equal(500m, travelSpend.Measure(member, Now));
        Assert.True(travelSpend.IsSatisfiedBy(member, Now));
    }

    [Fact]
    public void Account_type_scope_filters_balance()
    {
        var member = NewMember();
        member.OpenAccount(AccountType.Savings, 1_000, Now.AddDays(-30));
        member.OpenAccount(AccountType.Checking, 250, Now.AddDays(-30));

        var savings = SegmentCriterion.Create(MemberMetric.TotalBalance, ComparisonOperator.Equal, 1_000, AccountType.Savings);

        Assert.True(savings.IsSatisfiedBy(member, Now));
    }

    [Fact]
    public void Scope_that_does_not_fit_the_metric_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            SegmentCriterion.Create(MemberMetric.Age, ComparisonOperator.GreaterThan, 18, AccountType.Savings));
        Assert.Throws<ArgumentException>(() =>
            SegmentCriterion.Create(MemberMetric.TotalBalance, ComparisonOperator.GreaterThan, 18, merchantCategory: "Travel"));
    }
}

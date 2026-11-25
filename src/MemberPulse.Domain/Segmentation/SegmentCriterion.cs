using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Members;

namespace MemberPulse.Domain.Segmentation;

public enum MemberMetric
{
    /// <summary>Age in whole years.</summary>
    Age,
    /// <summary>Sum of account balances. Optionally scoped to one account type.</summary>
    TotalBalance,
    /// <summary>Number of accounts. Optionally scoped to one account type.</summary>
    AccountCount,
    /// <summary>Number of transactions in the last 30 days. Optionally scoped to a merchant category.</summary>
    TransactionCount30d,
    /// <summary>Total amount spent (debits) in the last 30 days. Optionally scoped to a merchant category.</summary>
    Spend30d,
    /// <summary>Whole days since the member joined.</summary>
    DaysSinceJoined,
}

public enum ComparisonOperator
{
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equal,
}

/// <summary>One condition of a segment, e.g. "Spend30d in 'Travel' >= 500".</summary>
public sealed class SegmentCriterion
{
    public const int WindowDays = 30;

    private SegmentCriterion() { }

    public MemberMetric Metric { get; private set; }
    public ComparisonOperator Operator { get; private set; }
    public decimal Value { get; private set; }
    public AccountType? AccountType { get; private set; }
    public string? MerchantCategory { get; private set; }

    public static SegmentCriterion Create(
        MemberMetric metric, ComparisonOperator op, decimal value,
        AccountType? accountType = null, string? merchantCategory = null)
    {
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        if (!Enum.IsDefined(op)) throw new ArgumentOutOfRangeException(nameof(op));

        if (accountType is not null && metric is not (MemberMetric.TotalBalance or MemberMetric.AccountCount))
        {
            throw new ArgumentException($"AccountType can only scope TotalBalance or AccountCount, not {metric}.", nameof(accountType));
        }

        var category = string.IsNullOrWhiteSpace(merchantCategory) ? null : merchantCategory.Trim();
        if (category is not null && metric is not (MemberMetric.Spend30d or MemberMetric.TransactionCount30d))
        {
            throw new ArgumentException($"MerchantCategory can only scope Spend30d or TransactionCount30d, not {metric}.", nameof(merchantCategory));
        }

        return new SegmentCriterion
        {
            Metric = metric,
            Operator = op,
            Value = value,
            AccountType = accountType,
            MerchantCategory = category,
        };
    }

    public decimal Measure(Member member, DateTimeOffset now)
    {
        switch (Metric)
        {
            case MemberMetric.Age:
                return member.AgeOn(DateOnly.FromDateTime(now.UtcDateTime));

            case MemberMetric.TotalBalance:
                return Accounts(member).Sum(a => a.Balance);

            case MemberMetric.AccountCount:
                return Accounts(member).Count();

            case MemberMetric.DaysSinceJoined:
                return Math.Max(0, (int)(now - member.JoinedAt).TotalDays);

            case MemberMetric.TransactionCount30d:
                return RecentTransactions(member, now).Count();

            case MemberMetric.Spend30d:
                return RecentTransactions(member, now).Where(t => t.Amount < 0).Sum(t => -t.Amount);

            default:
                throw new InvalidOperationException($"Unknown metric {Metric}.");
        }
    }

    public bool IsSatisfiedBy(Member member, DateTimeOffset now)
    {
        var measured = Measure(member, now);
        return Operator switch
        {
            ComparisonOperator.GreaterThan => measured > Value,
            ComparisonOperator.GreaterThanOrEqual => measured >= Value,
            ComparisonOperator.LessThan => measured < Value,
            ComparisonOperator.LessThanOrEqual => measured <= Value,
            ComparisonOperator.Equal => measured == Value,
            _ => throw new InvalidOperationException($"Unknown operator {Operator}."),
        };
    }

    private IEnumerable<Account> Accounts(Member member) =>
        AccountType is null ? member.Accounts : member.Accounts.Where(a => a.Type == AccountType);

    private IEnumerable<Transaction> RecentTransactions(Member member, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-WindowDays);
        return member.Accounts
            .SelectMany(a => a.Transactions)
            .Where(t => t.OccurredAt > cutoff && t.OccurredAt <= now)
            .Where(t => MerchantCategory is null
                || string.Equals(t.MerchantCategory, MerchantCategory, StringComparison.OrdinalIgnoreCase));
    }
}

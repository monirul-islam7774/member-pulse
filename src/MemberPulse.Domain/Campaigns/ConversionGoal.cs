using MemberPulse.Domain.Events;

namespace MemberPulse.Domain.Campaigns;

/// <summary>What counts as a member "converting" after receiving a campaign message.</summary>
public sealed class ConversionGoal
{
    public const int MaxWindowDays = 90;

    private ConversionGoal() { }

    /// <summary>How long after the message a transaction can still be credited to the campaign.</summary>
    public int WindowDays { get; private set; }
    public TransactionDirection Direction { get; private set; }
    public decimal? MinAmount { get; private set; }
    public string? MerchantCategory { get; private set; }

    /// <summary>Any purchase (debit) within 7 days.</summary>
    public static ConversionGoal Default => Create(7, TransactionDirection.Debit, null, null);

    public static ConversionGoal Create(int windowDays, TransactionDirection direction, decimal? minAmount, string? merchantCategory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowDays, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(windowDays, MaxWindowDays);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (minAmount < 0) throw new ArgumentOutOfRangeException(nameof(minAmount), "Minimum amount cannot be negative.");

        return new ConversionGoal
        {
            WindowDays = windowDays,
            Direction = direction,
            MinAmount = minAmount,
            MerchantCategory = string.IsNullOrWhiteSpace(merchantCategory) ? null : merchantCategory.Trim(),
        };
    }

    public bool Matches(TransactionPosted evt) => TransactionFilter.Matches(Direction, MinAmount, MerchantCategory, evt);
}

/// <summary>The direction / amount / category test shared by campaign triggers and conversion goals.</summary>
internal static class TransactionFilter
{
    public static bool Matches(TransactionDirection direction, decimal? minAmount, string? merchantCategory, TransactionPosted evt)
    {
        var directionMatches = direction switch
        {
            TransactionDirection.Debit => evt.Amount < 0,
            TransactionDirection.Credit => evt.Amount > 0,
            _ => true,
        };
        if (!directionMatches) return false;

        if (minAmount is { } min && Math.Abs(evt.Amount) < min) return false;

        return merchantCategory is null
            || string.Equals(merchantCategory, evt.MerchantCategory, StringComparison.OrdinalIgnoreCase);
    }
}

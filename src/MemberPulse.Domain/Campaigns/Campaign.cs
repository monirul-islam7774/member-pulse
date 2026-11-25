using System.Globalization;
using MemberPulse.Domain.Events;

namespace MemberPulse.Domain.Campaigns;

public enum CampaignChannel { Email, Sms, Push }

public enum CampaignStatus { Active, Paused }

public enum TransactionDirection
{
    Any,
    /// <summary>Money leaving the account (negative amounts).</summary>
    Debit,
    /// <summary>Money arriving in the account (positive amounts).</summary>
    Credit,
}

/// <summary>
/// Sends a message to members of a segment when a matching transaction is posted.
/// Fires at most once per transaction, and at most once per member per cooldown period.
/// </summary>
public sealed class Campaign
{
    private Campaign() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid SegmentId { get; private set; }
    public CampaignChannel Channel { get; private set; }
    /// <summary>Supports {FirstName}, {Amount} (absolute value) and {MerchantCategory}.</summary>
    public string MessageTemplate { get; private set; } = string.Empty;
    public TransactionDirection Direction { get; private set; }
    /// <summary>Triggering transaction must be at least this large (absolute value). Null = any size.</summary>
    public decimal? MinAmount { get; private set; }
    /// <summary>Triggering transaction must be in this merchant category. Null = any.</summary>
    public string? MerchantCategory { get; private set; }
    public int CooldownDays { get; private set; }
    /// <summary>What counts as a member responding to this campaign's messages.</summary>
    public ConversionGoal Conversion { get; private set; } = ConversionGoal.Default;
    public CampaignStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Campaign Create(
        string name, Guid segmentId, CampaignChannel channel, string messageTemplate,
        TransactionDirection direction, decimal? minAmount, string? merchantCategory,
        int cooldownDays, DateTimeOffset createdAt, ConversionGoal? conversion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTemplate);
        if (segmentId == Guid.Empty) throw new ArgumentException("A campaign needs a segment.", nameof(segmentId));
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (minAmount < 0) throw new ArgumentOutOfRangeException(nameof(minAmount), "Minimum amount cannot be negative.");
        ArgumentOutOfRangeException.ThrowIfNegative(cooldownDays);

        return new Campaign
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            SegmentId = segmentId,
            Channel = channel,
            MessageTemplate = messageTemplate.Trim(),
            Direction = direction,
            MinAmount = minAmount,
            MerchantCategory = string.IsNullOrWhiteSpace(merchantCategory) ? null : merchantCategory.Trim(),
            CooldownDays = cooldownDays,
            Conversion = conversion ?? ConversionGoal.Default,
            Status = CampaignStatus.Active,
            CreatedAt = createdAt,
        };
    }

    public void Pause() => Status = CampaignStatus.Paused;

    public void Resume() => Status = CampaignStatus.Active;

    /// <summary>Whether this transaction is the kind of event the campaign reacts to. Segment membership is checked separately.</summary>
    public bool AppliesTo(TransactionPosted evt)
    {
        return Status == CampaignStatus.Active
            && TransactionFilter.Matches(Direction, MinAmount, MerchantCategory, evt);
    }

    public string Render(string firstName, TransactionPosted evt) => MessageTemplate
        .Replace("{FirstName}", firstName, StringComparison.Ordinal)
        .Replace("{Amount}", Math.Abs(evt.Amount).ToString("F2", CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{MerchantCategory}", evt.MerchantCategory ?? string.Empty, StringComparison.Ordinal);
}

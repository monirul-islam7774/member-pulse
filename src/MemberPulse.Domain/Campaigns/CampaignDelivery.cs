using MemberPulse.Domain.Events;

namespace MemberPulse.Domain.Campaigns;

public enum DeliveryStatus
{
    /// <summary>Recorded, not yet sent. Also the claim that stops the same transaction triggering it twice.</summary>
    Pending,
    Sent,
    Failed,
}

/// <summary>Record that a campaign fired for a member because of a transaction.</summary>
public sealed class CampaignDelivery
{
    private CampaignDelivery() { }

    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid MemberId { get; private set; }
    /// <summary>The transaction that triggered it.</summary>
    public Guid TransactionId { get; private set; }
    public CampaignChannel Channel { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DeliveryStatus Status { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the member converted, if they have. Only the first qualifying transaction counts.</summary>
    public DateTimeOffset? ConvertedAt { get; private set; }
    public Guid? ConversionTransactionId { get; private set; }
    /// <summary>Absolute value of the converting transaction.</summary>
    public decimal? ConversionAmount { get; private set; }

    public bool IsConverted => ConvertedAt is not null;

    public static CampaignDelivery Create(Campaign campaign, Guid memberId, Guid transactionId, string message, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CampaignId = campaign.Id,
        MemberId = memberId,
        TransactionId = transactionId,
        Channel = campaign.Channel,
        Message = message,
        Status = DeliveryStatus.Pending,
        CreatedAt = now,
    };

    public void MarkSent() => Status = DeliveryStatus.Sent;

    public void MarkFailed(string error)
    {
        Status = DeliveryStatus.Failed;
        Error = error.Length > 500 ? error[..500] : error;
    }

    public bool IsWithinConversionWindow(DateTimeOffset occurredAt, int windowDays) =>
        occurredAt > CreatedAt && occurredAt <= CreatedAt.AddDays(windowDays);

    /// <summary>
    /// Credits <paramref name="evt"/> as a conversion if it qualifies: the message was sent, the member hasn't
    /// converted yet, the transaction came after the message (and isn't the one that triggered it), falls within
    /// the campaign's window, and meets its conversion goal.
    /// </summary>
    public bool TryConvert(Campaign campaign, TransactionPosted evt)
    {
        if (Status != DeliveryStatus.Sent || IsConverted) return false;
        if (campaign.Id != CampaignId || evt.MemberId != MemberId) return false;
        if (evt.TransactionId == TransactionId) return false;
        if (!IsWithinConversionWindow(evt.OccurredAt, campaign.Conversion.WindowDays)) return false;
        if (!campaign.Conversion.Matches(evt)) return false;

        ConvertedAt = evt.OccurredAt;
        ConversionTransactionId = evt.TransactionId;
        ConversionAmount = Math.Abs(evt.Amount);
        return true;
    }
}

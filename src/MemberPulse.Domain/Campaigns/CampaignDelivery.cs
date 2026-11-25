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
}

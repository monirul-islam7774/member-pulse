using MemberPulse.Domain.Events;

namespace MemberPulse.Domain.Campaigns;

public enum DeliveryStatus
{
    /// <summary>
    /// Recorded but not (yet) sent: either a send is in flight, or the last attempt failed and a retry is scheduled.
    /// Also the claim that stops the same transaction triggering it twice.
    /// </summary>
    Pending,
    Sent,
    /// <summary>Gave up: every attempt failed (or the member no longer exists). Not retried.</summary>
    Failed,
}

/// <summary>Record that a campaign fired for a member because of a transaction.</summary>
public sealed class CampaignDelivery
{
    /// <summary>Total send attempts allowed before the delivery is marked Failed.</summary>
    public const int MaxAttempts = 5;

    /// <summary>How long a send may be in flight before it is assumed lost (e.g. the process crashed) and retried.</summary>
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

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

    /// <summary>Send attempts made so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>For a Pending delivery: when it becomes due for (re)sending.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

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
        NextAttemptAt = now + ClaimTimeout,
    };

    /// <summary>30s, 2m, 8m, 32m after attempts 1 to 4.</summary>
    public static TimeSpan BackoffFor(int attempts) => TimeSpan.FromSeconds(30 * Math.Pow(4, Math.Max(0, attempts - 1)));

    public void MarkSent()
    {
        Attempts++;
        Status = DeliveryStatus.Sent;
        NextAttemptAt = null;
        Error = null;
    }

    /// <summary>Records a failed send. Schedules a retry, or gives up after <see cref="MaxAttempts"/>.</summary>
    /// <returns>True if the delivery has now permanently failed.</returns>
    public bool RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        Error = error.Length > 500 ? error[..500] : error;

        if (Attempts >= MaxAttempts)
        {
            Status = DeliveryStatus.Failed;
            NextAttemptAt = null;
            return true;
        }

        Status = DeliveryStatus.Pending;
        NextAttemptAt = now + BackoffFor(Attempts);
        return false;
    }

    /// <summary>Takes the delivery for a send attempt: hides it from other workers until the claim times out.</summary>
    public void Reclaim(DateTimeOffset now) => NextAttemptAt = now + ClaimTimeout;

    /// <summary>Gives up immediately, without retrying.</summary>
    public void MarkFailed(string error)
    {
        Status = DeliveryStatus.Failed;
        NextAttemptAt = null;
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

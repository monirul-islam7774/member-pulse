using MemberPulse.Domain.Campaigns;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemberPulse.Infrastructure.Campaigns;

public sealed class CampaignOptions
{
    public const string SectionName = "Campaigns";

    /// <summary>Set false to stop this instance retrying failed sends (e.g. in tests).</summary>
    public bool RetryWorkerEnabled { get; set; } = true;

    public int RetryPollSeconds { get; set; } = 30;

    public int RetryBatchSize { get; set; } = 50;
}

/// <summary>
/// Re-sends campaign messages whose earlier attempt failed (or whose send was lost mid-flight) once they are due.
/// A message can occasionally be sent twice if the process dies after sending but before recording it.
/// </summary>
public sealed class DeliveryRetrier(
    MemberPulseDbContext db,
    IMessageSender sender,
    TimeProvider time,
    ILogger<DeliveryRetrier> logger)
{
    /// <returns>How many messages were sent successfully.</returns>
    public async Task<int> ProcessDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.CampaignDeliveries
            .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt != null && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var delivery in due)
        {
            var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Id == delivery.MemberId, cancellationToken);
            if (member is null)
            {
                delivery.MarkFailed("Member no longer exists.");
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            // Hide it from other workers while we send.
            delivery.Reclaim(now);
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                await sender.SendAsync(delivery.Channel, member, delivery.Message, cancellationToken);
                delivery.MarkSent();
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var gaveUp = delivery.RecordFailure(ex.Message, time.GetUtcNow());
                logger.LogWarning(ex, "Retry of delivery {DeliveryId} failed (attempt {Attempts}){Outcome}",
                    delivery.Id, delivery.Attempts, gaveUp ? "; giving up" : "; will retry");
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }

        return sent;
    }
}

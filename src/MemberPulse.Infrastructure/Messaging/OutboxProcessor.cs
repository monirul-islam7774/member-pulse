using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemberPulse.Infrastructure.Messaging;

/// <summary>
/// Publishes pending outbox messages oldest-first. Messages sharing a key (an account) are never reordered:
/// while one is failing or backing off, later ones for the same key wait. Other keys carry on.
/// </summary>
public sealed class OutboxProcessor(
    MemberPulseDbContext db,
    IEventPublisher publisher,
    TimeProvider time,
    ILogger<OutboxProcessor> logger)
{
    /// <param name="maxAttempts">After this many failures a message is dead-lettered so it stops blocking its key.</param>
    /// <returns>How many messages were published.</returns>
    public async Task<int> ProcessBatchAsync(int batchSize, int maxAttempts, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null
                        && (m.NextAttemptAt == null || m.NextAttemptAt <= now)
                        // an older message for the same key that is still backing off must go first
                        && !db.OutboxMessages.Any(o => o.Key == m.Key && o.Id != m.Id
                                                       && o.ProcessedAt == null && o.DeadLetteredAt == null
                                                       && o.NextAttemptAt > now && o.CreatedAt < m.CreatedAt))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var published = 0;
        var blockedKeys = new HashSet<string>();
        foreach (var message in pending)
        {
            if (blockedKeys.Contains(message.Key))
            {
                continue; // an earlier message for this account just failed; keep order
            }

            try
            {
                await publisher.PublishAsync(message.Topic, message.Key, message.Type, message.Payload, cancellationToken);
                message.MarkPublished(time.GetUtcNow());
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                blockedKeys.Add(message.Key);
                var deadLettered = message.MarkFailed(ex.Message, time.GetUtcNow(), maxAttempts);
                if (deadLettered)
                {
                    logger.LogError(ex, "Outbox message {MessageId} dead-lettered after {Attempts} attempts", message.Id, message.Attempts);
                }
                else
                {
                    logger.LogWarning(ex, "Publishing outbox message {MessageId} failed (attempt {Attempts}); retrying after {Backoff}",
                        message.Id, message.Attempts, OutboxMessage.BackoffFor(message.Attempts));
                }
            }
            finally
            {
                // Save after each message so a crash never re-publishes more than the one in flight.
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }

        return published;
    }

    /// <summary>Deletes published messages older than the retention period. Dead letters are kept.</summary>
    /// <returns>How many rows were deleted.</returns>
    public async Task<int> PurgeProcessedAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - retention;
        var deleted = 0;

        while (true)
        {
            var old = await db.OutboxMessages
                .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .OrderBy(m => m.ProcessedAt)
                .Take(500)
                .ToListAsync(cancellationToken);
            if (old.Count == 0)
            {
                return deleted;
            }

            db.OutboxMessages.RemoveRange(old);
            await db.SaveChangesAsync(cancellationToken);
            deleted += old.Count;
        }
    }
}

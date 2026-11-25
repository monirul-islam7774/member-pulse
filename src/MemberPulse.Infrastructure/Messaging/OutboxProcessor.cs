using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemberPulse.Infrastructure.Messaging;

/// <summary>Publishes pending outbox messages oldest-first. Stops at the first failure so order is preserved.</summary>
public sealed class OutboxProcessor(
    MemberPulseDbContext db,
    IEventPublisher publisher,
    TimeProvider time,
    ILogger<OutboxProcessor> logger)
{
    /// <returns>How many messages were published.</returns>
    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message.Topic, message.Key, message.Type, message.Payload, cancellationToken);
                message.MarkPublished(time.GetUtcNow());
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.Message);
                logger.LogWarning(ex, "Publishing outbox message {MessageId} failed (attempt {Attempts})", message.Id, message.Attempts);
                break;
            }
            finally
            {
                // Save after each message so a crash never re-publishes more than the one in flight.
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }

        return published;
    }
}

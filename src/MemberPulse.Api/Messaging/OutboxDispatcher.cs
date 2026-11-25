using MemberPulse.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace MemberPulse.Api.Messaging;

/// <summary>Background loop that publishes pending outbox messages to Kafka and tidies up old ones.</summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IOptions<KafkaOptions> options,
    TimeProvider time,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PurgeEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.DispatcherEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(o.BootstrapServers))
        {
            logger.LogWarning("Kafka:BootstrapServers is not set; events are handled in-process instead of published.");
        }

        var delay = TimeSpan.FromMilliseconds(Math.Max(50, o.PollIntervalMilliseconds));
        using var timer = new PeriodicTimer(delay);
        var lastPurge = DateTimeOffset.MinValue;

        do
        {
            try
            {
                int published;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                    published = await processor.ProcessBatchAsync(o.BatchSize, o.MaxPublishAttempts, stoppingToken);
                }
                while (published == o.BatchSize && !stoppingToken.IsCancellationRequested); // drain a backlog

                var now = time.GetUtcNow();
                if (now - lastPurge >= PurgeEvery)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var purged = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>()
                        .PurgeProcessedAsync(TimeSpan.FromDays(Math.Max(1, o.ProcessedRetentionDays)), stoppingToken);
                    lastPurge = now;
                    if (purged > 0)
                    {
                        logger.LogInformation("Purged {Count} published outbox messages older than {Days} days", purged, o.ProcessedRetentionDays);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed; will retry.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try { return await timer.WaitForNextTickAsync(cancellationToken); }
        catch (OperationCanceledException) { return false; }
    }
}

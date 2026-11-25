using MemberPulse.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace MemberPulse.Api.Messaging;

/// <summary>Background loop that publishes pending outbox messages to Kafka.</summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IOptions<KafkaOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.DispatcherEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(o.BootstrapServers))
        {
            logger.LogWarning("Kafka:BootstrapServers is not set; events are logged instead of published.");
        }

        var delay = TimeSpan.FromMilliseconds(Math.Max(50, o.PollIntervalMilliseconds));
        using var timer = new PeriodicTimer(delay);

        do
        {
            try
            {
                int published;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                    published = await processor.ProcessBatchAsync(o.BatchSize, stoppingToken);
                }
                while (published == o.BatchSize && !stoppingToken.IsCancellationRequested); // drain a backlog
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

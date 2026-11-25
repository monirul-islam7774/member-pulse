using MemberPulse.Infrastructure.Campaigns;
using Microsoft.Extensions.Options;

namespace MemberPulse.Api.Messaging;

/// <summary>Background loop that retries campaign messages whose send failed.</summary>
public sealed class DeliveryRetryWorker(
    IServiceScopeFactory scopes,
    IOptions<CampaignOptions> options,
    ILogger<DeliveryRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.RetryWorkerEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, o.RetryPollSeconds)));
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DeliveryRetrier>().ProcessDueAsync(o.RetryBatchSize, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Delivery retry cycle failed; will try again.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}

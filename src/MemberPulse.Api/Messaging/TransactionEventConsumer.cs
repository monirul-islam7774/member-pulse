using Confluent.Kafka;
using MemberPulse.Infrastructure.Campaigns;
using MemberPulse.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace MemberPulse.Api.Messaging;

/// <summary>Reads TransactionPosted events from Kafka and runs them through the event handler (conversions + trigger engine).</summary>
public sealed class TransactionEventConsumer(
    IServiceScopeFactory scopes,
    IOptions<KafkaOptions> options,
    ILogger<TransactionEventConsumer> logger) : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.ConsumerEnabled || string.IsNullOrWhiteSpace(o.BootstrapServers))
        {
            return Task.CompletedTask; // in-process publisher feeds the engine instead
        }

        // Consume() blocks, so keep it off the host's startup path.
        return Task.Run(() => RunAsync(o, stoppingToken), CancellationToken.None);
    }

    private async Task RunAsync(KafkaOptions o, CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            GroupId = o.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // commit only after the engine has handled the event
            AllowAutoCreateTopics = true, // the topic may not exist until the first event is published
        }).Build();

        consumer.Subscribe(o.TransactionsTopic);
        logger.LogInformation("Trigger engine consuming {Topic} as group {Group}", o.TransactionsTopic, o.ConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning("Kafka consume error: {Reason}", ex.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                await HandleAsync(result.Message.Value, stoppingToken);
                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleAsync(string payload, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var evt = OutboxMessage.ReadTransaction(payload)
                    ?? throw new InvalidOperationException("Payload was not a TransactionPosted event.");

                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TransactionEventHandler>().HandleAsync(evt, stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Trigger engine failed on attempt {Attempt}/{Max}", attempt, MaxAttempts);
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), stoppingToken);
                }
                else
                {
                    // Skip rather than block every later event behind one bad message.
                    logger.LogError(ex, "Giving up on event after {Max} attempts: {Payload}", MaxAttempts, payload);
                }
            }
        }
    }
}

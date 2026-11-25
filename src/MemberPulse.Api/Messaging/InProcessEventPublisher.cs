using MemberPulse.Domain.Events;
using MemberPulse.Infrastructure.Campaigns;
using MemberPulse.Infrastructure.Messaging;

namespace MemberPulse.Api.Messaging;

/// <summary>
/// Used when no Kafka brokers are configured: hands events straight to the event handler (conversions + trigger engine),
/// so campaigns still work in local development without a broker.
/// </summary>
public sealed class InProcessEventPublisher(IServiceScopeFactory scopes, ILogger<InProcessEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync(string topic, string key, string eventType, string payload, CancellationToken cancellationToken)
    {
        logger.LogInformation("Kafka not configured; handling {EventType} for {Key} in-process", eventType, key);

        if (eventType != TransactionPosted.EventType)
        {
            return;
        }

        var evt = OutboxMessage.ReadTransaction(payload)
            ?? throw new InvalidOperationException("Outbox payload was not a TransactionPosted event.");

        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TransactionEventHandler>().HandleAsync(evt, cancellationToken);
    }
}

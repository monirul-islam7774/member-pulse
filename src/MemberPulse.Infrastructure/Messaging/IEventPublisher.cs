using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemberPulse.Infrastructure.Messaging;

public interface IEventPublisher
{
    /// <summary>Publishes one message. Throws if it could not be delivered.</summary>
    Task PublishAsync(string topic, string key, string eventType, string payload, CancellationToken cancellationToken);
}

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;

    public KafkaEventPublisher(IOptions<KafkaOptions> options)
    {
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000,
        }).Build();
    }

    public async Task PublishAsync(string topic, string key, string eventType, string payload, CancellationToken cancellationToken)
    {
        var message = new Message<string, string>
        {
            Key = key,
            Value = payload,
            Headers = new Headers { { "event-type", System.Text.Encoding.UTF8.GetBytes(eventType) } },
        };

        try
        {
            await _producer.ProduceAsync(topic, message, cancellationToken);
        }
        catch (ProduceException<string, string> ex)
        {
            throw new InvalidOperationException($"Kafka publish failed: {ex.Error.Reason}", ex);
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

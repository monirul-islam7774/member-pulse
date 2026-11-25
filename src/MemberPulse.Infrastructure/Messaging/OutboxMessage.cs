using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Domain.Events;

namespace MemberPulse.Infrastructure.Messaging;

/// <summary>An event waiting to be published, saved in the same database transaction as the change that caused it.</summary>
public sealed class OutboxMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    /// <summary>Kafka message key. Messages sharing a key keep their order.</summary>
    public string Key { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage ForTransaction(string topic, TransactionPosted evt, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Topic = topic,
        Key = evt.AccountId.ToString(),
        Type = TransactionPosted.EventType,
        Payload = JsonSerializer.Serialize(evt, JsonOptions),
        CreatedAt = now,
    };

    public static TransactionPosted? ReadTransaction(string payload) =>
        JsonSerializer.Deserialize<TransactionPosted>(payload, JsonOptions);

    public void MarkPublished(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
    }
}

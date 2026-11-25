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
    /// <summary>After a failure, don't retry before this time (exponential backoff).</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }
    /// <summary>Gave up after too many failed attempts. Kept for inspection and manual retry; no longer blocks later messages.</summary>
    public DateTimeOffset? DeadLetteredAt { get; private set; }

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

    /// <summary>2s, 4s, 8s ... capped at 5 minutes.</summary>
    public static TimeSpan BackoffFor(int attempts) => TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempts, 20))));

    /// <returns>True if this was the last attempt and the message is now dead-lettered.</returns>
    public bool MarkFailed(string error, DateTimeOffset now, int maxAttempts)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;

        if (Attempts >= maxAttempts)
        {
            DeadLetteredAt = now;
            NextAttemptAt = null;
            return true;
        }

        NextAttemptAt = now + BackoffFor(Attempts);
        return false;
    }

    /// <summary>Puts a dead-lettered message back in the queue for a fresh run of attempts.</summary>
    public void Revive()
    {
        DeadLetteredAt = null;
        NextAttemptAt = null;
        Attempts = 0;
    }
}

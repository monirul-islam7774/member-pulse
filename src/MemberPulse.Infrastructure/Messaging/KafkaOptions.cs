namespace MemberPulse.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>Comma-separated broker list, e.g. "localhost:9092". Empty = events are handled in-process instead.</summary>
    public string? BootstrapServers { get; set; }

    public string TransactionsTopic { get; set; } = "memberpulse.transactions";

    /// <summary>Set false to stop this instance publishing the outbox (e.g. in tests).</summary>
    public bool DispatcherEnabled { get; set; } = true;

    /// <summary>Set false to stop this instance consuming transaction events for campaigns.</summary>
    public bool ConsumerEnabled { get; set; } = true;

    public string ConsumerGroup { get; set; } = "memberpulse-triggers";

    public int PollIntervalMilliseconds { get; set; } = 1000;

    public int BatchSize { get; set; } = 50;
}

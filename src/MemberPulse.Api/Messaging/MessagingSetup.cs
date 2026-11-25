using MemberPulse.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace MemberPulse.Api.Messaging;

public static class MessagingSetup
{
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddOptions<KafkaOptions>().BindConfiguration(KafkaOptions.SectionName);

        // Real Kafka when brokers are configured, otherwise log-only. Chosen on first use so
        // configuration from any source (user-secrets, env, tests) is honoured.
        services.AddSingleton<IEventPublisher>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<KafkaOptions>>().Value.BootstrapServers)
                ? ActivatorUtilities.CreateInstance<LoggingEventPublisher>(sp)
                : ActivatorUtilities.CreateInstance<KafkaEventPublisher>(sp));

        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }
}

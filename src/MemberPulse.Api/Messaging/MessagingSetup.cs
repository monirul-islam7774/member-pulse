using MemberPulse.Infrastructure.Campaigns;
using MemberPulse.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace MemberPulse.Api.Messaging;

public static class MessagingSetup
{
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddOptions<KafkaOptions>().BindConfiguration(KafkaOptions.SectionName);

        // Real Kafka when brokers are configured, otherwise in-process. Chosen on first use so
        // configuration from any source (user-secrets, env, tests) is honoured.
        services.AddSingleton<IEventPublisher>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<KafkaOptions>>().Value.BootstrapServers)
                ? ActivatorUtilities.CreateInstance<InProcessEventPublisher>(sp)
                : ActivatorUtilities.CreateInstance<KafkaEventPublisher>(sp));

        services.AddSingleton<IMessageSender, LoggingMessageSender>();
        services.AddOptions<CampaignOptions>().BindConfiguration(CampaignOptions.SectionName);
        services.AddScoped<TriggerEngine>();
        services.AddScoped<DeliveryRetrier>();
        services.AddHostedService<DeliveryRetryWorker>();
        services.AddScoped<ConversionTracker>();
        services.AddScoped<TransactionEventHandler>();
        services.AddHostedService<TransactionEventConsumer>();

        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }
}

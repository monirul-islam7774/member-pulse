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

        // Email goes out over SMTP once Email:Host is set; until then (and for SMS and push, which have no provider yet)
        // messages are only logged. A half-set-up Email section stops the app at startup.
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddSingleton<LoggingMessageSender>();
        services.AddSingleton<EmailMessageSender>();
        services.AddSingleton<IMessageSender>(sp => new ChannelRoutingMessageSender(
            sp.GetRequiredService<IOptions<EmailOptions>>(),
            sp.GetRequiredService<EmailMessageSender>(),
            sp.GetRequiredService<LoggingMessageSender>()));
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

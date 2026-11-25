using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Members;
using MemberPulse.Infrastructure.Campaigns;
using Microsoft.Extensions.Options;

namespace MemberPulse.Tests.Messaging;

public class ChannelRoutingTests
{
    private sealed class RecordingSender : IMessageSender
    {
        public List<(CampaignChannel Channel, string Message)> Sent { get; } = [];

        public Task SendAsync(CampaignChannel channel, Member member, string message, CancellationToken cancellationToken)
        {
            Sent.Add((channel, message));
            return Task.CompletedTask;
        }
    }

    private static readonly Member Someone = Member.Create("A", "B", "a@b.example", new DateOnly(1990, 1, 1), DateTimeOffset.UtcNow);

    private static (ChannelRoutingMessageSender Router, RecordingSender Email, RecordingSender Fallback) Build(string? host)
    {
        var email = new RecordingSender();
        var fallback = new RecordingSender();
        var router = new ChannelRoutingMessageSender(Options.Create(new EmailOptions { Host = host }), email, fallback);
        return (router, email, fallback);
    }

    [Fact]
    public async Task Email_goes_to_the_smtp_sender_when_a_host_is_configured()
    {
        var (router, email, fallback) = Build("smtp.example.com");

        await router.SendAsync(CampaignChannel.Email, Someone, "hello", CancellationToken.None);

        Assert.Single(email.Sent);
        Assert.Empty(fallback.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Email_falls_back_to_logging_when_no_host_is_configured(string? host)
    {
        var (router, email, fallback) = Build(host);

        await router.SendAsync(CampaignChannel.Email, Someone, "hello", CancellationToken.None);

        Assert.Empty(email.Sent);
        Assert.Single(fallback.Sent);
    }

    [Theory]
    [InlineData(CampaignChannel.Sms)]
    [InlineData(CampaignChannel.Push)]
    public async Task Sms_and_push_have_no_provider_yet_so_they_always_use_the_fallback(CampaignChannel channel)
    {
        var (router, email, fallback) = Build("smtp.example.com");

        await router.SendAsync(channel, Someone, "hello", CancellationToken.None);

        Assert.Empty(email.Sent);
        Assert.Single(fallback.Sent);
    }
}

public class EmailOptionsValidationTests
{
    private static readonly EmailOptionsValidator Validator = new();

    private static EmailOptions Valid() => new() { Host = "smtp.example.com", FromAddress = "noreply@creditunion.example" };

    [Fact]
    public void Nothing_is_required_when_email_is_not_configured()
    {
        Assert.True(Validator.Validate(null, new EmailOptions()).Succeeded);
        Assert.True(Validator.Validate(null, new EmailOptions { Host = "  " }).Succeeded);
    }

    [Fact]
    public void A_complete_configuration_is_valid()
    {
        Assert.True(Validator.Validate(null, Valid()).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    public void A_host_without_a_usable_from_address_is_refused_at_startup(string? from)
    {
        var result = Validator.Validate(null, new EmailOptions { Host = "smtp.example.com", FromAddress = from });

        Assert.True(result.Failed);
        Assert.Contains("Email:FromAddress", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void An_impossible_port_is_refused(int port)
    {
        var options = Valid();
        options.Port = port;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Email:Port", result.FailureMessage);
    }

    [Fact]
    public void A_username_needs_a_password()
    {
        var options = Valid();
        options.Username = "mailer";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Email:Password", result.FailureMessage);
    }
}

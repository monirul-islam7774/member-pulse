using System.Text;
using MailKit.Net.Smtp;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Members;
using MemberPulse.Infrastructure.Campaigns;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MemberPulse.Tests.Messaging;

public class EmailMessageSenderTests : IDisposable
{
    private readonly FakeSmtpServer _server = new();

    public void Dispose() => _server.Dispose();

    private EmailMessageSender Sender(Action<EmailOptions>? configure = null)
    {
        var options = new EmailOptions
        {
            Host = "127.0.0.1",
            Port = _server.Port,
            Security = EmailSecurity.None,
            FromAddress = "noreply@creditunion.example",
            FromName = "Riverside Credit Union",
            TimeoutSeconds = 5,
        };
        configure?.Invoke(options);
        return new EmailMessageSender(Options.Create(options));
    }

    private static Member Aisha() =>
        Member.Create("Aisha", "Rahman", "aisha@example.com", new DateOnly(1997, 6, 21), DateTimeOffset.UtcNow);

    private static MimeMessage Parse(ReceivedMail mail) => MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(mail.Data)));

    [Fact]
    public async Task Delivers_the_message_as_a_plain_text_email_to_the_members_address()
    {
        await Sender().SendAsync(CampaignChannel.Email, Aisha(), "Hi Aisha, $45.50 on Dining?", CancellationToken.None);

        var mail = Assert.Single(_server.Mails);
        Assert.Equal("noreply@creditunion.example", mail.From);
        Assert.Equal(["aisha@example.com"], mail.Recipients);
        var message = Parse(mail);
        Assert.Equal("Riverside Credit Union", message.From.Mailboxes.Single().Name);
        Assert.Equal("Aisha Rahman", message.To.Mailboxes.Single().Name);
        Assert.Equal("A message from your credit union", message.Subject);
        Assert.Equal("Hi Aisha, $45.50 on Dining?", message.TextBody.TrimEnd());
        Assert.Null(message.HtmlBody);
    }

    [Fact]
    public async Task Uses_the_configured_subject()
    {
        await Sender(o => o.Subject = "Your travel rewards").SendAsync(CampaignChannel.Email, Aisha(), "Hello", CancellationToken.None);

        Assert.Equal("Your travel rewards", Parse(Assert.Single(_server.Mails)).Subject);
    }

    [Fact]
    public async Task Message_text_is_sent_literally_never_as_html_and_survives_smtp_dot_stuffing()
    {
        const string text = "<b>bold?</b> <script>alert(1)</script>\r\n.starts with a dot\r\nlast line";

        await Sender().SendAsync(CampaignChannel.Email, Aisha(), text, CancellationToken.None);

        var message = Parse(Assert.Single(_server.Mails));
        Assert.Null(message.HtmlBody);
        Assert.Equal(text, message.TextBody.TrimEnd().ReplaceLineEndings("\r\n"));
    }

    [Fact]
    public async Task A_member_name_with_special_characters_cannot_inject_headers()
    {
        var sneaky = Member.Create("Aisha\r\nBcc: attacker@evil.example", "Rahman", "aisha@example.com", new DateOnly(1997, 6, 21), DateTimeOffset.UtcNow);

        await Sender().SendAsync(CampaignChannel.Email, sneaky, "Hello", CancellationToken.None);

        var mail = Assert.Single(_server.Mails);
        Assert.Equal(["aisha@example.com"], mail.Recipients);
        Assert.Empty(Parse(mail).Bcc);
    }

    [Fact]
    public async Task A_rejected_recipient_throws_so_the_delivery_is_retried_or_marked_failed()
    {
        _server.RecipientReply = "550 5.1.1 No such mailbox";

        await Assert.ThrowsAsync<SmtpCommandException>(() =>
            Sender().SendAsync(CampaignChannel.Email, Aisha(), "Hello", CancellationToken.None));

        Assert.Empty(_server.Mails);
    }

    [Fact]
    public async Task An_unreachable_server_throws()
    {
        var closedPort = _server.Port;
        _server.Dispose();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Sender(o => o.Port = closedPort).SendAsync(CampaignChannel.Email, Aisha(), "Hello", CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_send_throws_operation_cancelled_which_the_engine_treats_as_a_shutdown()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Sender().SendAsync(CampaignChannel.Email, Aisha(), "Hello", cancelled.Token));
    }

    [Theory]
    [InlineData(CampaignChannel.Sms)]
    [InlineData(CampaignChannel.Push)]
    public async Task Only_the_email_channel_is_handled(CampaignChannel channel)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Sender().SendAsync(channel, Aisha(), "Hello", CancellationToken.None));

        Assert.Empty(_server.Mails);
    }
}

using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Members;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MemberPulse.Infrastructure.Campaigns;

public enum EmailSecurity
{
    /// <summary>No encryption. Only for a local test server such as Mailpit.</summary>
    None,
    /// <summary>Connect in the clear, then upgrade with STARTTLS (usually port 587).</summary>
    StartTls,
    /// <summary>TLS from the first byte (usually port 465).</summary>
    Ssl,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The SMTP server. Leave empty to send nothing: email messages are then only written to the log.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public EmailSecurity Security { get; set; } = EmailSecurity.StartTls;

    public string? Username { get; set; }

    /// <summary>Supply via user-secrets or the Email__Password environment variable; never commit it.</summary>
    public string? Password { get; set; }

    /// <summary>The address messages come from. Required once Host is set.</summary>
    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "MemberPulse";

    /// <summary>Campaigns have no subject of their own yet, so every email uses this one.</summary>
    public string Subject { get; set; } = "A message from your credit union";

    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>Refuses a half-set-up email configuration at startup, instead of failing every message later.</summary>
public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Success;
        }

        var problems = new List<string>();
        if (!IsEmailAddress(options.FromAddress))
        {
            problems.Add("Email:FromAddress must be a valid email address when Email:Host is set.");
        }

        if (options.Port is < 1 or > 65535)
        {
            problems.Add("Email:Port must be between 1 and 65535.");
        }

        if (!string.IsNullOrWhiteSpace(options.Username) && string.IsNullOrEmpty(options.Password))
        {
            problems.Add("Email:Password is required when Email:Username is set.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    // MimeKit accepts a bare "name" as a mailbox; a sender address must have a domain.
    private static bool IsEmailAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && System.Net.Mail.MailAddress.TryCreate(value, out var parsed)
        && string.Equals(parsed.Address, value.Trim(), StringComparison.Ordinal);
}

/// <summary>
/// Sends a campaign message to a member's email address over SMTP, as plain text. Any failure (unreachable server,
/// rejected recipient, timeout) is thrown, so the delivery is retried and finally marked Failed by the existing logic.
/// </summary>
public sealed class EmailMessageSender(IOptions<EmailOptions> options) : IMessageSender
{
    public async Task SendAsync(CampaignChannel channel, Member member, string message, CancellationToken cancellationToken)
    {
        if (channel != CampaignChannel.Email)
        {
            throw new ArgumentException($"The email sender cannot deliver {channel} messages.", nameof(channel));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var o = options.Value;

        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        email.To.Add(new MailboxAddress($"{member.FirstName} {member.LastName}", member.Email));
        email.Subject = o.Subject;
        email.Body = new TextPart("plain") { Text = message }; // text, never HTML: the message may contain member-supplied words

        using var client = new SmtpClient { Timeout = o.TimeoutSeconds * 1000 };
        await client.ConnectAsync(o.Host, o.Port, o.Security switch
        {
            EmailSecurity.None => SecureSocketOptions.None,
            EmailSecurity.Ssl => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls,
        }, cancellationToken);

        if (!string.IsNullOrWhiteSpace(o.Username))
        {
            await client.AuthenticateAsync(o.Username, o.Password, cancellationToken);
        }

        await client.SendAsync(email, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

/// <summary>
/// Email goes out over SMTP once a host is configured. Everything else (email with no host yet, and SMS and push,
/// which have no provider) falls back to <paramref name="fallback"/>, which only logs.
/// </summary>
public sealed class ChannelRoutingMessageSender(IOptions<EmailOptions> options, IMessageSender email, IMessageSender fallback) : IMessageSender
{
    public Task SendAsync(CampaignChannel channel, Member member, string message, CancellationToken cancellationToken) =>
        (channel == CampaignChannel.Email && options.Value.IsConfigured ? email : fallback)
            .SendAsync(channel, member, message, cancellationToken);
}

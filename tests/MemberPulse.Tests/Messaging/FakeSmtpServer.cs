using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MemberPulse.Tests.Messaging;

public sealed record ReceivedMail(string From, IReadOnlyList<string> Recipients, string Data);

/// <summary>
/// Just enough SMTP to receive a message over a real socket: lets the sender be tested against an actual SMTP
/// conversation (greeting, EHLO, MAIL, RCPT, DATA, QUIT) without Docker or a mocked client.
/// </summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private bool _disposed;

    public FakeSmtpServer()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ConcurrentQueue<ReceivedMail> Mails { get; } = new();

    /// <summary>When set, every RCPT TO is answered with this reply line (e.g. "550 5.1.1 No such mailbox").</summary>
    public string? RecipientReply { get; set; }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = HandleAsync(client);
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested || true)
        {
            // listener closed
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

            await writer.WriteLineAsync("220 fake.local ESMTP ready");
            var from = "";
            var recipients = new List<string>();

            while (await reader.ReadLineAsync() is { } line)
            {
                var command = line.ToUpperInvariant();
                if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                {
                    await writer.WriteLineAsync("250-fake.local");
                    await writer.WriteLineAsync("250 8BITMIME");
                }
                else if (command.StartsWith("MAIL FROM:"))
                {
                    from = Address(line);
                    recipients = [];
                    await writer.WriteLineAsync("250 OK");
                }
                else if (command.StartsWith("RCPT TO:"))
                {
                    if (RecipientReply is { } reply)
                    {
                        await writer.WriteLineAsync(reply);
                    }
                    else
                    {
                        recipients.Add(Address(line));
                        await writer.WriteLineAsync("250 OK");
                    }
                }
                else if (command == "DATA")
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.Append(dataLine.StartsWith("..") ? dataLine[1..] : dataLine).Append("\r\n"); // undo dot-stuffing
                    }

                    Mails.Enqueue(new ReceivedMail(from, recipients, data.ToString()));
                    await writer.WriteLineAsync("250 OK queued");
                }
                else if (command == "QUIT")
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                else if (command == "RSET" || command == "NOOP")
                {
                    await writer.WriteLineAsync("250 OK");
                }
                else
                {
                    await writer.WriteLineAsync("502 command not implemented");
                }
            }
        }
        catch (IOException)
        {
            // client went away
        }
    }

    private static string Address(string commandLine)
    {
        var start = commandLine.IndexOf('<') + 1;
        var end = commandLine.IndexOf('>');
        return commandLine[start..end];
    }

    public void Dispose()
    {
        if (_disposed) return; // tests may stop the server early to simulate an outage
        _disposed = true;
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}

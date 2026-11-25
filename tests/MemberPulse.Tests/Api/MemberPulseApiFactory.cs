using System.Collections.Concurrent;
using System.Net.Http.Headers;
using MemberPulse.Api.Auth;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Members;
using MemberPulse.Domain.Staff;
using MemberPulse.Infrastructure.Campaigns;
using MemberPulse.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MemberPulse.Tests.Api;

public class MemberPulseApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The app's clock: real time plus an adjustable offset, so tests can "wait" through backoffs.</summary>
    public OffsetTimeProvider Clock { get; } = new();

    /// <summary>Stands in for the email/SMS/push provider; can be told to fail.</summary>
    public ControllableMessageSender Sender { get; } = new();

    /// <summary>Settings a test class can override (e.g. a tiny login rate limit).</summary>
    protected virtual IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-test-signing-key-0123456789",
                // Tests drive these loops themselves instead of racing a background timer.
                ["Kafka:DispatcherEnabled"] = "false",
                ["Campaigns:RetryWorkerEnabled"] = "false",
                // Plenty of logins per minute; the rate-limit tests lower it.
                ["Security:LoginPermitsPerMinute"] = "1000",
            });
            config.AddInMemoryCollection(ExtraSettings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<RecordingEventPublisher>();
            services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<RecordingEventPublisher>());

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IMessageSender>();
            services.AddSingleton<IMessageSender>(Sender);
        });
    }

    /// <summary>Client already signed in as a staff user with the given role.</summary>
    public HttpClient CreateAuthenticatedClient(StaffRole role = StaffRole.Staff)
    {
        var user = StaffUser.Create($"{role}-{Guid.NewGuid():N}@example.com", "unused", role, DateTimeOffset.UtcNow);
        var token = Services.GetRequiredService<TokenService>().Issue(user);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    /// <summary>Stores a real staff user (hashed password) so the login endpoint can be exercised.</summary>
    public async Task<StaffUser> AddStaffUserAsync(string email, string password, StaffRole role)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberPulse.Infrastructure.Persistence.MemberPulseDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<StaffUser>>();

        var placeholder = StaffUser.Create(email, "pending", role, DateTimeOffset.UtcNow);
        var user = StaffUser.Create(email, hasher.HashPassword(placeholder, password), role, DateTimeOffset.UtcNow);
        db.StaffUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}

public sealed class OffsetTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <summary>
    /// Moves the app's clock forward until the returned scope is disposed. Tokens issued while it is moved are
    /// "from the future" for the JWT validator, so create HTTP clients before advancing.
    /// </summary>
    public IDisposable Advance(TimeSpan by)
    {
        Interlocked.Add(ref _offsetTicks, by.Ticks);
        return new Revert(this, by);
    }

    private sealed class Revert(OffsetTimeProvider clock, TimeSpan by) : IDisposable
    {
        public void Dispose() => Interlocked.Add(ref clock._offsetTicks, -by.Ticks);
    }
}

public sealed class RecordingEventPublisher : IEventPublisher
{
    private readonly object _gate = new();
    private readonly List<(string Topic, string Key, string Type, string Payload)> _published = [];
    private readonly ConcurrentDictionary<string, bool> _failingKeys = new();

    /// <summary>When set, every publish throws, to simulate Kafka being down.</summary>
    public volatile bool Fail;

    /// <summary>Makes publishes for one key (account) fail while others succeed.</summary>
    public void FailKey(string key, bool fail = true)
    {
        if (fail) _failingKeys[key] = true;
        else _failingKeys.TryRemove(key, out _);
    }

    public IReadOnlyList<(string Topic, string Key, string Type, string Payload)> Published
    {
        get { lock (_gate) return _published.ToList(); }
    }

    public Task PublishAsync(string topic, string key, string eventType, string payload, CancellationToken cancellationToken)
    {
        if (Fail || _failingKeys.ContainsKey(key)) throw new InvalidOperationException("broker unavailable");
        lock (_gate) _published.Add((topic, key, eventType, payload));
        return Task.CompletedTask;
    }
}

public sealed class ControllableMessageSender : IMessageSender
{
    private readonly object _gate = new();
    private readonly List<(string Email, string Message)> _sent = [];

    /// <summary>When set, sending throws an ordinary error (a provider outage).</summary>
    public volatile bool Fail;

    /// <summary>When set, sending throws OperationCanceledException, as if the process was shut down mid-send.</summary>
    public volatile bool Crash;

    public IReadOnlyList<(string Email, string Message)> Sent
    {
        get { lock (_gate) return _sent.ToList(); }
    }

    public Task SendAsync(CampaignChannel channel, Member member, string message, CancellationToken cancellationToken)
    {
        if (Crash) throw new OperationCanceledException("simulated crash");
        if (Fail) throw new InvalidOperationException("provider unavailable");
        lock (_gate) _sent.Add((member.Email, message));
        return Task.CompletedTask;
    }
}

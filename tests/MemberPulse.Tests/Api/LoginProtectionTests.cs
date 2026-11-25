using System.Net;
using System.Net.Http.Json;
using MemberPulse.Domain.Staff;

namespace MemberPulse.Tests.Api;

public class LoginProtectionTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private const string Password = "Correct-Horse-9!";

    private static string UniqueEmail() => $"lock-{Guid.NewGuid():N}@example.com";

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task FailLoginsAsync(HttpClient client, string email, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, "wrong-wrong-wrong")).StatusCode);
        }
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var email = UniqueEmail();
        await factory.AddStaffUserAsync(email, Password, StaffRole.Staff);
        var client = factory.CreateClient();

        await FailLoginsAsync(client, email, StaffUser.MaxFailedLogins);

        // Locked: same 401 as any other failure, so an attacker learns nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, Password)).StatusCode);
    }

    [Fact]
    public async Task The_lockout_expires_after_fifteen_minutes()
    {
        var email = UniqueEmail();
        await factory.AddStaffUserAsync(email, Password, StaffRole.Staff);
        var client = factory.CreateClient();
        await FailLoginsAsync(client, email, StaffUser.MaxFailedLogins);

        using (factory.Clock.Advance(StaffUser.LockoutDuration - TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, Password)).StatusCode);
        }

        using (factory.Clock.Advance(StaffUser.LockoutDuration + TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email, Password)).StatusCode);
        }
    }

    [Fact]
    public async Task A_successful_login_resets_the_failure_count()
    {
        var email = UniqueEmail();
        await factory.AddStaffUserAsync(email, Password, StaffRole.Staff);
        var client = factory.CreateClient();

        await FailLoginsAsync(client, email, StaffUser.MaxFailedLogins - 1);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email, Password)).StatusCode);
        await FailLoginsAsync(client, email, StaffUser.MaxFailedLogins - 1);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email, Password)).StatusCode); // never reached 5 in a row
    }

    [Fact]
    public async Task Failed_attempts_while_locked_do_not_extend_the_lockout()
    {
        var email = UniqueEmail();
        await factory.AddStaffUserAsync(email, Password, StaffRole.Staff);
        var client = factory.CreateClient();
        await FailLoginsAsync(client, email, StaffUser.MaxFailedLogins);

        using (factory.Clock.Advance(TimeSpan.FromMinutes(10)))
        {
            await FailLoginsAsync(client, email, 3); // hammering a locked account
        }

        using (factory.Clock.Advance(StaffUser.LockoutDuration + TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email, Password)).StatusCode);
        }
    }

    [Fact]
    public async Task An_admin_can_unlock_an_account_early()
    {
        var email = UniqueEmail();
        await factory.AddStaffUserAsync(email, Password, StaffRole.Staff);
        var anonymous = factory.CreateClient();
        await FailLoginsAsync(anonymous, email, StaffUser.MaxFailedLogins);
        var admin = factory.CreateAuthenticatedClient(StaffRole.Admin);

        var unlock = await admin.PostAsJsonAsync("/api/users/unlock", new { email });

        Assert.Equal(HttpStatusCode.NoContent, unlock.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(anonymous, email, Password)).StatusCode);
    }

    [Fact]
    public async Task Unlock_is_admin_only_and_unknown_email_is_404()
    {
        var staff = factory.CreateAuthenticatedClient(StaffRole.Staff);
        var admin = factory.CreateAuthenticatedClient(StaffRole.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/users/unlock", new { email = UniqueEmail() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/users/unlock", new { email = UniqueEmail() })).StatusCode);
    }

    [Fact]
    public async Task Unknown_emails_cannot_be_locked_or_distinguished()
    {
        var client = factory.CreateClient();

        for (var i = 0; i < StaffUser.MaxFailedLogins + 2; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, UniqueEmail(), Password)).StatusCode);
        }
    }
}

/// <summary>The rate limit is per client address, so it gets its own app with a tiny allowance.</summary>
public sealed class RateLimitedApiFactory : MemberPulseApiFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraSettings { get; } =
        new Dictionary<string, string?> { ["Security:LoginPermitsPerMinute"] = "3" };
}

public class LoginRateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Login_attempts_beyond_the_limit_get_429_with_retry_after_but_other_endpoints_are_unaffected()
    {
        var client = factory.CreateClient();
        var attempt = () => client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "whatever-password" });

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await attempt()).StatusCode);
        }

        var limited = await attempt();
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
}

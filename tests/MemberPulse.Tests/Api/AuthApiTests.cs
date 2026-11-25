using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Staff;

namespace MemberPulse.Tests.Api;

public class AuthApiTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private const string Password = "Correct-Horse-9!";

    private static string UniqueEmail() => $"staff-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Requests_without_a_token_are_rejected()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/segments")).StatusCode);
    }

    [Fact]
    public async Task Health_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_returns_a_token_that_works()
    {
        var email = UniqueEmail();
        await factory.AddStaffUserAsync(email, Password, StaffRole.Staff);
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = email.ToUpperInvariant(), password = Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(Json);
        Assert.Equal("Bearer", body!.TokenType);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/members")).StatusCode);
        var me = await client.GetFromJsonAsync<StaffUserDto>("/api/auth/me", Json);
        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_both_return_401()
    {
        var email = UniqueEmail();
        await factory.AddStaffUserAsync(email, Password, StaffRole.Staff);
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "nope-nope-nope" });
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new { email = UniqueEmail(), password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var client = factory.CreateAuthenticatedClient();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^3] + "abc");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/members")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_create_users()
    {
        var body = new { email = UniqueEmail(), password = "A-Long-Password-1", role = "Staff" };

        var asStaff = await factory.CreateAuthenticatedClient(StaffRole.Staff).PostAsJsonAsync("/api/users", body);
        var asAdmin = await factory.CreateAuthenticatedClient(StaffRole.Admin).PostAsJsonAsync("/api/users", body);

        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        Assert.Equal(HttpStatusCode.Created, asAdmin.StatusCode);
    }

    [Fact]
    public async Task Created_user_can_log_in_and_short_passwords_are_refused()
    {
        var admin = factory.CreateAuthenticatedClient(StaffRole.Admin);
        var email = UniqueEmail();

        var weak = await admin.PostAsJsonAsync("/api/users", new { email = UniqueEmail(), password = "short", role = "Staff" });
        var create = await admin.PostAsJsonAsync("/api/users", new { email, password = "A-Long-Password-1", role = "Staff" });
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "A-Long-Password-1" });

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Duplicate_staff_email_returns_409()
    {
        var admin = factory.CreateAuthenticatedClient(StaffRole.Admin);
        var body = new { email = UniqueEmail(), password = "A-Long-Password-1", role = "Staff" };

        await admin.PostAsJsonAsync("/api/users", body);
        var again = await admin.PostAsJsonAsync("/api/users", body);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }
}

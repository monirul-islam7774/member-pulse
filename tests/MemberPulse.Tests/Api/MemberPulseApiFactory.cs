using System.Net.Http.Headers;
using MemberPulse.Api.Auth;
using MemberPulse.Domain.Staff;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MemberPulse.Tests.Api;

public sealed class MemberPulseApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-signing-key-test-signing-key-0123456789",
        }));
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

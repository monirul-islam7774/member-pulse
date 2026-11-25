using System.IdentityModel.Tokens.Jwt;
using MemberPulse.Domain.Staff;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MemberPulse.Api.Auth;

public static class AuthSetup
{
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddJwtAuth(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TokenService>();
        services.AddSingleton<IPasswordHasher<StaffUser>, PasswordHasher<StaffUser>>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured lazily so the key is read from final configuration (user-secrets, env, tests).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var o = jwt.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = o.Issuer,
                    ValidateAudience = true,
                    ValidAudience = o.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = TokenService.SigningKey(o),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = TokenService.RoleClaim,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                };
            });

        services.AddAuthorizationBuilder()
            // Everything requires a signed-in user unless it opts out with [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, p => p.RequireRole(nameof(StaffRole.Admin)));

        return services;
    }

    /// <summary>
    /// Creates the first admin from Seed:AdminEmail / Seed:AdminPassword when no staff users exist.
    /// Does nothing if those settings are absent.
    /// </summary>
    public static async Task BootstrapAdminAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<MemberPulseDbContext>();
        if (await db.StaffUsers.AnyAsync(cancellationToken))
        {
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<StaffUser>>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var admin = StaffUser.Create(email, "pending", StaffRole.Admin, time.GetUtcNow());
        admin = StaffUser.Create(email, hasher.HashPassword(admin, password), StaffRole.Admin, time.GetUtcNow());

        db.StaffUsers.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
    }
}

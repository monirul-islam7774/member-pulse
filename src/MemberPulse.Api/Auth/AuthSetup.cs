using System.IdentityModel.Tokens.Jwt;
using MemberPulse.Domain.Staff;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MemberPulse.Api.Auth;

public static class AuthSetup
{
    public const string AdminPolicy = "Admin";
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddJwtAuth(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>().BindConfiguration(SecurityOptions.SectionName);

        // Slows password guessing: login attempts are limited per client IP. (Behind a reverse proxy, make sure the
        // real client address reaches the app, e.g. with forwarded-headers middleware, or everyone shares one limit.)
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
            limiter.AddPolicy(LoginRateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = httpContext.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value.LoginPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

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

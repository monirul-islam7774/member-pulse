using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MemberPulse.Domain.Staff;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MemberPulse.Api.Auth;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    public const string RoleClaim = "role";

    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.Key));

    public IssuedToken Issue(StaffUser user)
    {
        var o = options.Value;
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(o.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: o.Issuer,
            audience: o.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(RoleClaim, user.Role.ToString()),
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256));

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

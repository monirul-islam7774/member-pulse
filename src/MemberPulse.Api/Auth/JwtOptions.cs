using System.ComponentModel.DataAnnotations;

namespace MemberPulse.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "MemberPulse";

    [Required]
    public string Audience { get; set; } = "MemberPulse.Api";

    /// <summary>HMAC signing key, at least 32 characters. Supply via user-secrets or environment (Jwt__Key); never commit a real one.</summary>
    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpiryMinutes { get; set; } = 60;
}

using System.ComponentModel.DataAnnotations;

namespace MemberPulse.Api.Auth;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Login requests allowed per client IP per minute; beyond that the API answers 429.</summary>
    [Range(1, 100_000)]
    public int LoginPermitsPerMinute { get; set; } = 10;
}

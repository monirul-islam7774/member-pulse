using System.ComponentModel.DataAnnotations;
using MemberPulse.Domain.Staff;

namespace MemberPulse.Api.Contracts;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string Password { get; init; } = string.Empty;
}

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed class CreateStaffUserRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(12), MaxLength(200)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public StaffRole? Role { get; init; }
}

public sealed class UnlockUserRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;
}

public sealed record StaffUserDto(Guid Id, string Email, StaffRole Role, DateTimeOffset CreatedAt);

public sealed record StaffUserSummaryDto(
    Guid Id, string Email, StaffRole Role, DateTimeOffset CreatedAt, bool IsLockedOut, DateTimeOffset? LockedUntil);

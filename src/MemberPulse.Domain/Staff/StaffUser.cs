namespace MemberPulse.Domain.Staff;

public enum StaffRole
{
    /// <summary>Can use the API.</summary>
    Staff,
    /// <summary>Can use the API and manage staff users.</summary>
    Admin,
}

/// <summary>A credit-union employee who signs in to the API. Not a member.</summary>
public sealed class StaffUser
{
    private StaffUser() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public StaffRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static StaffUser Create(string email, string passwordHash, StaffRole role, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));

        return new StaffUser
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = createdAt,
        };
    }
}

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
    /// <summary>Consecutive failed logins that lock the account.</summary>
    public const int MaxFailedLogins = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private StaffUser() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public StaffRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

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

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>Counts a failed login; the account locks after <see cref="MaxFailedLogins"/> in a row.</summary>
    public void RegisterFailedLogin(DateTimeOffset now)
    {
        if (LockedUntil is { } until && until <= now)
        {
            // The previous lockout has run its course: start counting afresh.
            LockedUntil = null;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockedUntil = now + LockoutDuration;
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    public void Unlock() => RegisterSuccessfulLogin();
}

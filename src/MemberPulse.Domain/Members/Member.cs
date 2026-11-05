using MemberPulse.Domain.Accounts;

namespace MemberPulse.Domain.Members;

public sealed class Member
{
    private readonly List<Account> _accounts = [];

    private Member() { }

    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public DateOnly DateOfBirth { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }

    public IReadOnlyCollection<Account> Accounts => _accounts.AsReadOnly();

    public static Member Create(string firstName, string lastName, string email, DateOnly dateOfBirth, DateTimeOffset joinedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new Member
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            DateOfBirth = dateOfBirth,
            JoinedAt = joinedAt,
        };
    }

    public int AgeOn(DateOnly date)
    {
        var age = date.Year - DateOfBirth.Year;
        if (date < DateOfBirth.AddYears(age))
        {
            age--;
        }
        return age;
    }

    public Account OpenAccount(AccountType type, decimal initialDeposit, DateTimeOffset openedAt)
    {
        var account = Account.Open(Id, type, initialDeposit, openedAt);
        _accounts.Add(account);
        return account;
    }
}
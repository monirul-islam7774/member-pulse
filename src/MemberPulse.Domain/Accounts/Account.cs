namespace MemberPulse.Domain.Accounts;

public sealed class Account
{
    private readonly List<Transaction> _transactions = [];

    private Account() { }

    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public AccountType Type { get; private set; }
    public decimal Balance { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }

    public IReadOnlyCollection<Transaction> Transactions => _transactions.AsReadOnly();

    public bool IsDepositAccount => Type is AccountType.Savings or AccountType.Checking;

    internal static Account Open(Guid memberId, AccountType type, decimal initialDeposit, DateTimeOffset openedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialDeposit);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            Type = type,
            OpenedAt = openedAt,
        };

        if (initialDeposit > 0)
        {
            account.Post(initialDeposit, "Initial deposit", merchantCategory: null, openedAt);
        }

        return account;
    }

    public Transaction Post(decimal amount, string description, string? merchantCategory, DateTimeOffset occurredAt)
    {
        if (amount == 0)
        {
            throw new ArgumentException("Amount cannot be zero.", nameof(amount));
        }

        if (IsDepositAccount && Balance + amount < 0)
        {
            throw new InvalidOperationException("Insufficient funds.");
        }

        var transaction = Transaction.Create(Id, amount, description, merchantCategory, occurredAt);
        _transactions.Add(transaction);
        Balance += amount;
        return transaction;
    }
}
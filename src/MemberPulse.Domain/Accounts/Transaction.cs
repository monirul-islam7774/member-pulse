namespace MemberPulse.Domain.Accounts;

public sealed class Transaction
{
    private Transaction() { } // EF Core uses this to load rows

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? MerchantCategory { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    internal static Transaction Create(
        Guid accountId, decimal amount, string description,
        string? merchantCategory, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Amount = amount,
            Description = description.Trim(),
            MerchantCategory = string.IsNullOrWhiteSpace(merchantCategory) ? null : merchantCategory.Trim(),
            OccurredAt = occurredAt,
        };
    }
}
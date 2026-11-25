using MemberPulse.Domain.Accounts;

namespace MemberPulse.Domain.Events;

/// <summary>
/// Published whenever a transaction is posted (including an account's initial deposit).
/// Delivery is at-least-once, so consumers should de-duplicate on <see cref="TransactionId"/>.
/// </summary>
public sealed record TransactionPosted(
    Guid TransactionId,
    Guid AccountId,
    Guid MemberId,
    AccountType AccountType,
    decimal Amount,
    decimal BalanceAfter,
    string Description,
    string? MerchantCategory,
    DateTimeOffset OccurredAt)
{
    public const string EventType = "TransactionPosted";

    /// <summary>Call straight after posting, so the account balance already includes the transaction.</summary>
    public static TransactionPosted From(Account account, Transaction transaction) => new(
        transaction.Id,
        account.Id,
        account.MemberId,
        account.Type,
        transaction.Amount,
        account.Balance,
        transaction.Description,
        transaction.MerchantCategory,
        transaction.OccurredAt);
}

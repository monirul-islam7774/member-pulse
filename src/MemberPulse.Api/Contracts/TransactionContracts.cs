using System.ComponentModel.DataAnnotations;

namespace MemberPulse.Api.Contracts;

public sealed class PostTransactionRequest
{
    /// <summary>Positive for credits/deposits, negative for debits/purchases. Cannot be zero.</summary>
    [Required, Range(-1_000_000_000d, 1_000_000_000d)]
    public decimal? Amount { get; init; }

    [Required, MaxLength(200)]
    public string Description { get; init; } = string.Empty;

    [MaxLength(50)]
    public string? MerchantCategory { get; init; }

    /// <summary>Defaults to the current time. Cannot be in the future.</summary>
    public DateTimeOffset? OccurredAt { get; init; }
}

public sealed record TransactionDto(
    Guid Id,
    Guid AccountId,
    decimal Amount,
    string Description,
    string? MerchantCategory,
    DateTimeOffset OccurredAt);

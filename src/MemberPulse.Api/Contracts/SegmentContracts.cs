using System.ComponentModel.DataAnnotations;
using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Segmentation;

namespace MemberPulse.Api.Contracts;

public sealed class CriterionRequest
{
    [Required]
    public MemberMetric? Metric { get; init; }

    [Required]
    public ComparisonOperator? Operator { get; init; }

    [Required, Range(-1_000_000_000d, 1_000_000_000d)]
    public decimal? Value { get; init; }

    /// <summary>Only for TotalBalance / AccountCount.</summary>
    public AccountType? AccountType { get; init; }

    /// <summary>Only for Spend30d / TransactionCount30d.</summary>
    [MaxLength(50)]
    public string? MerchantCategory { get; init; }
}

public sealed class CreateSegmentRequest
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; init; }

    [Required, MinLength(1), MaxLength(20)]
    public List<CriterionRequest> Criteria { get; init; } = [];
}

public sealed record CriterionDto(
    MemberMetric Metric,
    ComparisonOperator Operator,
    decimal Value,
    AccountType? AccountType,
    string? MerchantCategory);

public sealed record SegmentDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CriterionDto> Criteria);

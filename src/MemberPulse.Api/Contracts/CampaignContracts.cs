using System.ComponentModel.DataAnnotations;
using MemberPulse.Domain.Campaigns;

namespace MemberPulse.Api.Contracts;

public sealed class CreateCampaignRequest
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public Guid? SegmentId { get; init; }

    [Required]
    public CampaignChannel? Channel { get; init; }

    /// <summary>May use {FirstName}, {Amount} and {MerchantCategory}.</summary>
    [Required, MaxLength(1000)]
    public string MessageTemplate { get; init; } = string.Empty;

    public TransactionDirection Direction { get; init; } = TransactionDirection.Any;

    [Range(0d, 1_000_000_000d)]
    public decimal? MinAmount { get; init; }

    [MaxLength(50)]
    public string? MerchantCategory { get; init; }

    /// <summary>Minimum days between messages from this campaign to the same member. 0 = no limit.</summary>
    [Range(0, 3650)]
    public int CooldownDays { get; init; }

    /// <summary>Days after a message in which a qualifying transaction counts as a conversion.</summary>
    [Range(1, 90)]
    public int ConversionWindowDays { get; init; } = 7;

    /// <summary>Defaults to Debit: the member went on to spend.</summary>
    public TransactionDirection ConversionDirection { get; init; } = TransactionDirection.Debit;

    [Range(0d, 1_000_000_000d)]
    public decimal? ConversionMinAmount { get; init; }

    [MaxLength(50)]
    public string? ConversionMerchantCategory { get; init; }
}

public sealed record ConversionGoalDto(
    int WindowDays,
    TransactionDirection Direction,
    decimal? MinAmount,
    string? MerchantCategory);

public sealed record CampaignDto(
    Guid Id,
    string Name,
    Guid SegmentId,
    CampaignChannel Channel,
    string MessageTemplate,
    TransactionDirection Direction,
    decimal? MinAmount,
    string? MerchantCategory,
    int CooldownDays,
    ConversionGoalDto Conversion,
    CampaignStatus Status,
    DateTimeOffset CreatedAt);

public sealed record DeliveryDto(
    Guid Id,
    Guid CampaignId,
    Guid MemberId,
    Guid TransactionId,
    CampaignChannel Channel,
    string Message,
    DeliveryStatus Status,
    int Attempts,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConvertedAt,
    Guid? ConversionTransactionId,
    decimal? ConversionAmount);

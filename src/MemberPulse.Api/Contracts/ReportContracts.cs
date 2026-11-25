namespace MemberPulse.Api.Contracts;

/// <summary>
/// Sent/Failed/Pending count messages by when they were created. Pending are still being retried after a failed
/// send. Failed gave up. Converted counts those messages that led to a conversion (so it can only grow as members
/// respond). ConversionRate = Converted / Sent.
/// </summary>
public sealed record KpiDto(
    int Sent,
    int Failed,
    int Pending,
    int Converted,
    decimal ConversionRate,
    int MembersReached,
    decimal ConvertedAmount);

public sealed record CampaignKpiDto(Guid CampaignId, string Name, KpiDto Kpis);

public sealed record CampaignReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    KpiDto Totals,
    IReadOnlyList<CampaignKpiDto> Campaigns);

public sealed record DailyKpiDto(DateOnly Date, int Sent, int Converted);

public sealed record CampaignDetailReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    CampaignKpiDto Campaign,
    IReadOnlyList<DailyKpiDto> Daily);

using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Api.Controllers;

[ApiController]
[Route("api/reports/campaigns")]
public sealed class ReportsController(MemberPulseDbContext db, TimeProvider time) : ControllerBase
{
    private const int MaxRangeDays = 366;

    /// <summary>KPIs for every campaign that sent messages in the range (default: last 30 days).</summary>
    [HttpGet]
    public async Task<ActionResult<CampaignReportDto>> Overview(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (!TryResolveRange(from, to, out var start, out var end, out var problem))
        {
            return problem!;
        }

        var deliveries = InRange(start, end);

        var counts = await deliveries
            .GroupBy(d => d.CampaignId)
            .Select(g => new
            {
                CampaignId = g.Key,
                Sent = g.Count(d => d.Status == DeliveryStatus.Sent),
                Failed = g.Count(d => d.Status == DeliveryStatus.Failed),
                Converted = g.Count(d => d.ConvertedAt != null),
                Amount = g.Sum(d => d.ConversionAmount) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        // Distinct (campaign, member) pairs among sent messages; grouped in memory to keep the SQL simple.
        var reached = await deliveries
            .Where(d => d.Status == DeliveryStatus.Sent)
            .Select(d => new { d.CampaignId, d.MemberId })
            .Distinct()
            .ToListAsync(cancellationToken);
        var reachedByCampaign = reached.GroupBy(r => r.CampaignId).ToDictionary(g => g.Key, g => g.Count());

        var ids = counts.Select(c => c.CampaignId).ToList();
        var names = await db.Campaigns.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var campaigns = counts
            .Select(c => new CampaignKpiDto(
                c.CampaignId,
                names.GetValueOrDefault(c.CampaignId, "(deleted)"),
                Kpi(c.Sent, c.Failed, c.Converted, reachedByCampaign.GetValueOrDefault(c.CampaignId), c.Amount)))
            .OrderByDescending(c => c.Kpis.Sent).ThenBy(c => c.Name)
            .ToList();

        var totals = Kpi(
            counts.Sum(c => c.Sent), counts.Sum(c => c.Failed), counts.Sum(c => c.Converted),
            reached.Select(r => r.MemberId).Distinct().Count(), counts.Sum(c => c.Amount));

        return new CampaignReportDto(start, end, totals, campaigns);
    }

    /// <summary>One campaign's KPIs plus a per-day series (days in UTC, by when the message was sent).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CampaignDetailReportDto>> Detail(
        Guid id, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (!TryResolveRange(from, to, out var start, out var end, out var problem))
        {
            return problem!;
        }

        var campaign = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            return NotFound();
        }

        var rows = await InRange(start, end)
            .Where(d => d.CampaignId == id)
            .Select(d => new { d.MemberId, d.Status, d.CreatedAt, d.ConvertedAt, d.ConversionAmount })
            .ToListAsync(cancellationToken);

        var sent = rows.Where(r => r.Status == DeliveryStatus.Sent).ToList();
        var kpis = Kpi(
            sent.Count,
            rows.Count(r => r.Status == DeliveryStatus.Failed),
            rows.Count(r => r.ConvertedAt != null),
            sent.Select(r => r.MemberId).Distinct().Count(),
            rows.Sum(r => r.ConversionAmount ?? 0m));

        var daily = rows
            .GroupBy(r => DateOnly.FromDateTime(r.CreatedAt.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new DailyKpiDto(g.Key, g.Count(r => r.Status == DeliveryStatus.Sent), g.Count(r => r.ConvertedAt != null)))
            .ToList();

        return new CampaignDetailReportDto(start, end, new CampaignKpiDto(campaign.Id, campaign.Name, kpis), daily);
    }

    private IQueryable<CampaignDelivery> InRange(DateTimeOffset start, DateTimeOffset end) =>
        db.CampaignDeliveries.AsNoTracking().Where(d => d.CreatedAt >= start && d.CreatedAt <= end);

    private static KpiDto Kpi(int sent, int failed, int converted, int reached, decimal amount) =>
        new(sent, failed, converted, sent == 0 ? 0m : Math.Round((decimal)converted / sent, 4), reached, amount);

    private bool TryResolveRange(
        DateTimeOffset? from, DateTimeOffset? to,
        out DateTimeOffset start, out DateTimeOffset end, out ActionResult? problem)
    {
        end = to ?? time.GetUtcNow();
        start = from ?? end.AddDays(-30);
        problem = null;

        if (start > end)
        {
            ModelState.AddModelError(nameof(from), "'from' must not be after 'to'.");
        }
        else if ((end - start).TotalDays > MaxRangeDays)
        {
            ModelState.AddModelError(nameof(from), $"The range cannot exceed {MaxRangeDays} days.");
        }

        if (ModelState.IsValid)
        {
            return true;
        }

        problem = ValidationProblem(ModelState);
        return false;
    }
}

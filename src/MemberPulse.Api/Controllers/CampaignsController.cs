using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Api.Controllers;

[ApiController]
[Route("api/campaigns")]
public sealed class CampaignsController(MemberPulseDbContext db, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CampaignDto>>> List(CancellationToken cancellationToken)
    {
        var campaigns = await db.Campaigns.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return campaigns.Select(ToDto).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CampaignDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return campaign is null ? NotFound() : ToDto(campaign);
    }

    [HttpPost]
    public async Task<ActionResult<CampaignDto>> Create(CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await db.Campaigns.AnyAsync(c => c.Name == name, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Campaign name already used",
                detail: $"A campaign named '{name}' already exists.");
        }

        if (!await db.Segments.AnyAsync(s => s.Id == request.SegmentId, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.SegmentId), "Segment does not exist.");
            return ValidationProblem(ModelState);
        }

        Campaign campaign;
        try
        {
            campaign = Campaign.Create(
                name, request.SegmentId!.Value, request.Channel!.Value, request.MessageTemplate,
                request.Direction, request.MinAmount, request.MerchantCategory, request.CooldownDays, time.GetUtcNow(),
                ConversionGoal.Create(request.ConversionWindowDays, request.ConversionDirection,
                    request.ConversionMinAmount, request.ConversionMerchantCategory));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? nameof(request), ex.Message);
            return ValidationProblem(ModelState);
        }

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = campaign.Id }, ToDto(campaign));
    }

    [HttpPost("{id:guid}/pause")]
    public Task<ActionResult<CampaignDto>> Pause(Guid id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, c => c.Pause(), cancellationToken);

    [HttpPost("{id:guid}/resume")]
    public Task<ActionResult<CampaignDto>> Resume(Guid id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, c => c.Resume(), cancellationToken);

    [HttpGet("{id:guid}/deliveries")]
    public async Task<ActionResult<PagedResult<DeliveryDto>>> Deliveries(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Campaigns.AnyAsync(c => c.Id == id, cancellationToken))
        {
            return NotFound();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.CampaignDeliveries.AsNoTracking()
            .Where(d => d.CampaignId == id)
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DeliveryDto(d.Id, d.CampaignId, d.MemberId, d.TransactionId, d.Channel, d.Message, d.Status, d.Attempts, d.Error, d.CreatedAt,
                d.ConvertedAt, d.ConversionTransactionId, d.ConversionAmount))
            .ToListAsync(cancellationToken);

        return new PagedResult<DeliveryDto>(items, page, pageSize, total);
    }

    private async Task<ActionResult<CampaignDto>> SetStatusAsync(Guid id, Action<Campaign> change, CancellationToken cancellationToken)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            return NotFound();
        }

        change(campaign);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(campaign);
    }

    private static CampaignDto ToDto(Campaign c) => new(
        c.Id, c.Name, c.SegmentId, c.Channel, c.MessageTemplate, c.Direction,
        c.MinAmount, c.MerchantCategory, c.CooldownDays,
        new ConversionGoalDto(c.Conversion.WindowDays, c.Conversion.Direction, c.Conversion.MinAmount, c.Conversion.MerchantCategory),
        c.Status, c.CreatedAt);
}

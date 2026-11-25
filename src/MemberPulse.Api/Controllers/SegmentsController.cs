using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Members;
using MemberPulse.Domain.Segmentation;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Api.Controllers;

[ApiController]
public sealed class SegmentsController(MemberPulseDbContext db, TimeProvider time) : ControllerBase
{
    [HttpGet("api/segments")]
    public async Task<ActionResult<IReadOnlyList<SegmentDto>>> List(CancellationToken cancellationToken)
    {
        var segments = await db.Segments.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return segments.Select(ToDto).ToList();
    }

    [HttpGet("api/segments/{id:guid}")]
    public async Task<ActionResult<SegmentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var segment = await db.Segments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return segment is null ? NotFound() : ToDto(segment);
    }

    [HttpPost("api/segments")]
    public async Task<ActionResult<SegmentDto>> Create(CreateSegmentRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await db.Segments.AnyAsync(s => s.Name == name, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Segment name already used",
                detail: $"A segment named '{name}' already exists.");
        }

        Segment segment;
        try
        {
            var criteria = request.Criteria.Select(c => SegmentCriterion.Create(
                c.Metric!.Value, c.Operator!.Value, c.Value!.Value, c.AccountType, c.MerchantCategory));
            segment = Segment.Create(name, request.Description, criteria, time.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(request.Criteria), ex.Message);
            return ValidationProblem(ModelState);
        }

        db.Segments.Add(segment);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = segment.Id }, ToDto(segment));
    }

    [HttpGet("api/segments/{id:guid}/members")]
    public async Task<ActionResult<PagedResult<MemberSummaryDto>>> Members(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var segment = await db.Segments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (segment is null)
        {
            return NotFound();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var now = time.GetUtcNow();

        // Evaluated in memory against each member's recent activity. Fine for a modest member base;
        // revisit with precomputed metrics if membership grows large.
        var matching = (await MembersWithRecentActivity(now).ToListAsync(cancellationToken))
            .Where(m => segment.Matches(m, now))
            .OrderBy(m => m.LastName).ThenBy(m => m.FirstName)
            .ToList();

        var items = matching
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MemberSummaryDto(m.Id, m.FirstName + " " + m.LastName, m.Email, m.Accounts.Count))
            .ToList();

        return new PagedResult<MemberSummaryDto>(items, page, pageSize, matching.Count);
    }

    [HttpGet("api/members/{memberId:guid}/segments")]
    public async Task<ActionResult<IReadOnlyList<SegmentDto>>> ForMember(Guid memberId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var member = await MembersWithRecentActivity(now).FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        var segments = await db.Segments.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return segments.Where(s => s.Matches(member, now)).Select(ToDto).ToList();
    }

    private IQueryable<Member> MembersWithRecentActivity(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-SegmentCriterion.WindowDays);
        return db.Members
            .AsNoTracking()
            .Include(m => m.Accounts)
            .ThenInclude(a => a.Transactions.Where(t => t.OccurredAt > cutoff));
    }

    private static SegmentDto ToDto(Segment s) => new(
        s.Id, s.Name, s.Description, s.CreatedAt,
        s.Criteria.Select(c => new CriterionDto(c.Metric, c.Operator, c.Value, c.AccountType, c.MerchantCategory)).ToList());
}

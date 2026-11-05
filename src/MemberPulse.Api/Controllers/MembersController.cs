using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Members;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Api.Controllers;

[ApiController]
[Route("api/members")]
public sealed class MembersController(MemberPulseDbContext db, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<MemberSummaryDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Members
            .AsNoTracking()
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MemberSummaryDto(m.Id, m.FirstName + " " + m.LastName, m.Email, m.Accounts.Count))
            .ToListAsync(cancellationToken);

        return new PagedResult<MemberSummaryDto>(items, page, pageSize, total);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MemberDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var member = await db.Members
            .AsNoTracking()
            .Include(m => m.Accounts)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        return member is null ? NotFound() : ToDetailDto(member);
    }

    [HttpPost]
    public async Task<ActionResult<MemberDetailDto>> Create(CreateMemberRequest request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (request.DateOfBirth > today)
        {
            ModelState.AddModelError(nameof(request.DateOfBirth), "Date of birth cannot be in the future.");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Members.AnyAsync(m => m.Email == email, cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email already registered",
                detail: $"A member with email '{email}' already exists.");
        }

        var member = Member.Create(request.FirstName, request.LastName, email, request.DateOfBirth!.Value, time.GetUtcNow());
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = member.Id }, ToDetailDto(member));
    }

    [HttpPost("{id:guid}/accounts")]
    public async Task<ActionResult<AccountDto>> OpenAccount(Guid id, OpenAccountRequest request, CancellationToken cancellationToken)
    {
        var member = await db.Members
            .Include(m => m.Accounts)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (member is null)
        {
            return NotFound();
        }

        var account = member.OpenAccount(request.Type!.Value, request.InitialDeposit, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, ToAccountDto(account));
    }

    private MemberDetailDto ToDetailDto(Member m)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return new MemberDetailDto(
            m.Id, m.FirstName, m.LastName, m.Email, m.DateOfBirth,
            m.AgeOn(today), m.JoinedAt,
            m.Accounts.Select(ToAccountDto).ToList());
    }

    private static AccountDto ToAccountDto(Account a) => new(a.Id, a.Type, a.Balance, a.OpenedAt);
}
using MemberPulse.Api.Auth;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Api.Controllers;

public sealed record DeadLetterDto(
    Guid Id, string Topic, string Key, string Type, int Attempts, string? LastError,
    DateTimeOffset CreatedAt, DateTimeOffset DeadLetteredAt);

/// <summary>Events that could not be published after every retry. Admin only.</summary>
[ApiController]
[Authorize(Policy = AuthSetup.AdminPolicy)]
[Route("api/admin/outbox")]
public sealed class OutboxAdminController(MemberPulseDbContext db) : ControllerBase
{
    [HttpGet("dead")]
    public async Task<ActionResult<IReadOnlyList<DeadLetterDto>>> Dead(CancellationToken cancellationToken)
    {
        return await db.OutboxMessages.AsNoTracking()
            .Where(m => m.DeadLetteredAt != null)
            .OrderBy(m => m.CreatedAt)
            .Take(200)
            .Select(m => new DeadLetterDto(m.Id, m.Topic, m.Key, m.Type, m.Attempts, m.LastError, m.CreatedAt, m.DeadLetteredAt!.Value))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Gives a dead-lettered message a fresh run of attempts (e.g. after fixing the broker).</summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var message = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id && m.DeadLetteredAt != null, cancellationToken);
        if (message is null)
        {
            return NotFound();
        }

        message.Revive();
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

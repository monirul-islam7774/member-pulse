using MemberPulse.Api.Contracts;
using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Events;
using MemberPulse.Infrastructure.Messaging;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MemberPulse.Api.Controllers;

[ApiController]
[Route("api/accounts/{accountId:guid}/transactions")]
public sealed class TransactionsController(MemberPulseDbContext db, TimeProvider time, IOptions<KafkaOptions> kafka) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TransactionDto>>> List(
        Guid accountId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId, cancellationToken))
        {
            return NotFound();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.OccurredAt)
            .ThenBy(t => t.Id);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto(t.Id, t.AccountId, t.Amount, t.Description, t.MerchantCategory, t.OccurredAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<TransactionDto>(items, page, pageSize, total);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransactionDto>> GetById(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        var t = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.AccountId == accountId, cancellationToken);

        return t is null ? NotFound() : ToDto(t);
    }

    [HttpPost]
    public async Task<ActionResult<TransactionDto>> Post(Guid accountId, PostTransactionRequest request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var amount = request.Amount!.Value;

        if (amount == 0)
        {
            ModelState.AddModelError(nameof(request.Amount), "Amount cannot be zero.");
        }
        if (request.OccurredAt > now)
        {
            ModelState.AddModelError(nameof(request.OccurredAt), "OccurredAt cannot be in the future.");
        }
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            ModelState.AddModelError(nameof(request.Description), "Description is required.");
        }
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        Transaction transaction;
        try
        {
            transaction = account.Post(amount, request.Description, request.MerchantCategory, request.OccurredAt ?? now);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Transaction rejected", detail: ex.Message);
        }

        // Saved in the same database transaction as the posting, so the event can't be lost or sent for a rolled-back change.
        db.OutboxMessages.Add(OutboxMessage.ForTransaction(kafka.Value.TransactionsTopic, TransactionPosted.From(account, transaction), now));
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { accountId, id = transaction.Id }, ToDto(transaction));
    }

    private static TransactionDto ToDto(Transaction t) =>
        new(t.Id, t.AccountId, t.Amount, t.Description, t.MerchantCategory, t.OccurredAt);
}

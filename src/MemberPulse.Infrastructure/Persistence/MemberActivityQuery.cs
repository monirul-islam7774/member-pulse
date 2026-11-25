using MemberPulse.Domain.Members;
using MemberPulse.Domain.Segmentation;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Infrastructure.Persistence;

public static class MemberActivityQuery
{
    /// <summary>Members with their accounts and only the transactions segment rules look at (the last 30 days).</summary>
    public static IQueryable<Member> WithRecentActivity(this MemberPulseDbContext db, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-SegmentCriterion.WindowDays);
        return db.Members
            .AsNoTracking()
            .Include(m => m.Accounts)
            .ThenInclude(a => a.Transactions.Where(t => t.OccurredAt > cutoff))
            // Members x accounts x transactions in one joined query repeats rows multiplicatively; load each level separately.
            .AsSplitQuery();
    }
}

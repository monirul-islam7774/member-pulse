using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Events;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Infrastructure.Campaigns;

/// <summary>
/// Credits a posted transaction as a conversion for any campaign message the member received recently and
/// hasn't yet responded to. Safe to call twice with the same event.
/// </summary>
public sealed class ConversionTracker(MemberPulseDbContext db)
{
    /// <returns>How many deliveries this transaction converted.</returns>
    public async Task<int> HandleAsync(TransactionPosted evt, CancellationToken cancellationToken)
    {
        // Open (unconverted, sent) messages from before this transaction and inside the longest possible window.
        var earliest = evt.OccurredAt.AddDays(-ConversionGoal.MaxWindowDays);
        var open = await db.CampaignDeliveries
            .Where(d => d.MemberId == evt.MemberId
                        && d.Status == DeliveryStatus.Sent
                        && d.ConvertedAt == null
                        && d.CreatedAt < evt.OccurredAt
                        && d.CreatedAt >= earliest)
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return 0;
        }

        var campaignIds = open.Select(d => d.CampaignId).Distinct().ToList();
        var campaigns = await db.Campaigns.AsNoTracking().Where(c => campaignIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);

        var converted = open.Count(d => campaigns.TryGetValue(d.CampaignId, out var campaign) && d.TryConvert(campaign, evt));
        if (converted > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return converted;
    }
}

/// <summary>Everything that reacts to a posted transaction. Both the Kafka consumer and the in-process publisher call this.</summary>
public sealed class TransactionEventHandler(ConversionTracker conversions, TriggerEngine triggers)
{
    public async Task HandleAsync(TransactionPosted evt, CancellationToken cancellationToken)
    {
        // Conversions first: this transaction should be judged against messages sent before it, not one it triggers.
        await conversions.HandleAsync(evt, cancellationToken);
        await triggers.HandleAsync(evt, cancellationToken);
    }
}

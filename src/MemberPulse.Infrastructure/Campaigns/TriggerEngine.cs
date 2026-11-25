using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Events;
using MemberPulse.Domain.Members;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemberPulse.Infrastructure.Campaigns;

/// <summary>Sends a campaign message to a member. Swap the implementation to talk to a real email/SMS/push provider.</summary>
public interface IMessageSender
{
    Task SendAsync(CampaignChannel channel, Member member, string message, CancellationToken cancellationToken);
}

/// <summary>Default sender: writes the message to the log instead of contacting anyone.</summary>
public sealed class LoggingMessageSender(ILogger<LoggingMessageSender> logger) : IMessageSender
{
    public Task SendAsync(CampaignChannel channel, Member member, string message, CancellationToken cancellationToken)
    {
        logger.LogInformation("[{Channel}] to {Email}: {Message}", channel, member.Email, message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reacts to a posted transaction: for each active campaign that applies to it and whose segment the member
/// belongs to, records a delivery and sends the message. Safe to call twice with the same event.
/// </summary>
public sealed class TriggerEngine(
    MemberPulseDbContext db,
    IMessageSender sender,
    TimeProvider time,
    ILogger<TriggerEngine> logger)
{
    /// <returns>Number of campaign messages triggered.</returns>
    public async Task<int> HandleAsync(TransactionPosted evt, CancellationToken cancellationToken)
    {
        var campaigns = (await db.Campaigns.AsNoTracking()
                .Where(c => c.Status == CampaignStatus.Active)
                .ToListAsync(cancellationToken))
            .Where(c => c.AppliesTo(evt))
            .ToList();
        if (campaigns.Count == 0)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        var member = await db.WithRecentActivity(now).FirstOrDefaultAsync(m => m.Id == evt.MemberId, cancellationToken);
        if (member is null)
        {
            logger.LogWarning("Transaction {TransactionId} refers to unknown member {MemberId}", evt.TransactionId, evt.MemberId);
            return 0;
        }

        var segmentIds = campaigns.Select(c => c.SegmentId).Distinct().ToList();
        var segments = await db.Segments.AsNoTracking().Where(s => segmentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);

        var triggered = 0;
        foreach (var campaign in campaigns)
        {
            if (!segments.TryGetValue(campaign.SegmentId, out var segment) || !segment.Matches(member, now))
            {
                continue;
            }

            if (await db.CampaignDeliveries.AnyAsync(d => d.CampaignId == campaign.Id && d.TransactionId == evt.TransactionId, cancellationToken))
            {
                continue; // event delivered twice
            }

            if (campaign.CooldownDays > 0)
            {
                var since = now.AddDays(-campaign.CooldownDays);
                var recentlySent = await db.CampaignDeliveries.AnyAsync(
                    d => d.CampaignId == campaign.Id && d.MemberId == member.Id
                         && d.Status != DeliveryStatus.Failed && d.CreatedAt > since,
                    cancellationToken);
                if (recentlySent)
                {
                    continue;
                }
            }

            var delivery = CampaignDelivery.Create(campaign, member.Id, evt.TransactionId, campaign.Render(member.FirstName, evt), now);
            db.CampaignDeliveries.Add(delivery);
            try
            {
                await db.SaveChangesAsync(cancellationToken); // claims the (campaign, transaction) pair
            }
            catch (DbUpdateException)
            {
                db.Entry(delivery).State = EntityState.Detached;
                continue; // another instance claimed it first
            }

            try
            {
                await sender.SendAsync(campaign.Channel, member, delivery.Message, cancellationToken);
                delivery.MarkSent();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var gaveUp = delivery.RecordFailure(ex.Message, time.GetUtcNow());
                logger.LogWarning(ex, "Sending campaign {CampaignId} to member {MemberId} failed (attempt {Attempts}){Outcome}",
                    campaign.Id, member.Id, delivery.Attempts, gaveUp ? "; giving up" : "; will retry");
            }

            await db.SaveChangesAsync(CancellationToken.None);
            triggered++;
        }

        return triggered;
    }
}

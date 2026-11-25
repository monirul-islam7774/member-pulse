using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Staff;
using MemberPulse.Infrastructure.Messaging;

namespace MemberPulse.Tests.Domain;

public class StaffLockoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static StaffUser NewUser() => StaffUser.Create("a@b.com", "hash", StaffRole.Staff, Now);

    [Fact]
    public void Locks_on_the_fifth_consecutive_failure_for_fifteen_minutes()
    {
        var user = NewUser();

        for (var i = 0; i < StaffUser.MaxFailedLogins - 1; i++) user.RegisterFailedLogin(Now);
        Assert.False(user.IsLockedOut(Now));

        user.RegisterFailedLogin(Now);
        Assert.True(user.IsLockedOut(Now));
        Assert.True(user.IsLockedOut(Now.AddMinutes(14)));
        Assert.False(user.IsLockedOut(Now.AddMinutes(15)));
    }

    [Fact]
    public void Success_and_unlock_clear_the_count_and_the_lock()
    {
        var user = NewUser();
        for (var i = 0; i < StaffUser.MaxFailedLogins; i++) user.RegisterFailedLogin(Now);

        user.Unlock();

        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(0, user.FailedLoginCount);
        for (var i = 0; i < StaffUser.MaxFailedLogins - 1; i++) user.RegisterFailedLogin(Now);
        Assert.False(user.IsLockedOut(Now)); // a fresh count of 4
    }

    [Fact]
    public void After_a_lockout_expires_the_count_starts_again()
    {
        var user = NewUser();
        for (var i = 0; i < StaffUser.MaxFailedLogins; i++) user.RegisterFailedLogin(Now);
        var later = Now.AddMinutes(16);

        for (var i = 0; i < StaffUser.MaxFailedLogins - 1; i++) user.RegisterFailedLogin(later);

        Assert.False(user.IsLockedOut(later)); // 4 new failures aren't enough to lock again
        user.RegisterFailedLogin(later);
        Assert.True(user.IsLockedOut(later));
    }
}

public class RetryBackoffTests
{
    [Fact]
    public void Outbox_backoff_doubles_and_is_capped_at_five_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), OutboxMessage.BackoffFor(1));
        Assert.Equal(TimeSpan.FromSeconds(4), OutboxMessage.BackoffFor(2));
        Assert.Equal(TimeSpan.FromSeconds(128), OutboxMessage.BackoffFor(7));
        Assert.Equal(TimeSpan.FromMinutes(5), OutboxMessage.BackoffFor(9));
        Assert.Equal(TimeSpan.FromMinutes(5), OutboxMessage.BackoffFor(500)); // no overflow
    }

    [Fact]
    public void Delivery_backoff_is_30s_2m_8m_32m()
    {
        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(32)],
            Enumerable.Range(1, 4).Select(CampaignDelivery.BackoffFor));
    }

    [Fact]
    public void Delivery_gives_up_exactly_at_the_maximum_attempts()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var campaign = Campaign.Create("C", Guid.NewGuid(), CampaignChannel.Sms, "x", TransactionDirection.Any, null, null, 0, now);
        var delivery = CampaignDelivery.Create(campaign, Guid.NewGuid(), Guid.NewGuid(), "msg", now);

        for (var attempt = 1; attempt < CampaignDelivery.MaxAttempts; attempt++)
        {
            Assert.False(delivery.RecordFailure("boom", now));
            Assert.Equal(DeliveryStatus.Pending, delivery.Status);
            Assert.Equal(now + CampaignDelivery.BackoffFor(attempt), delivery.NextAttemptAt);
        }

        Assert.True(delivery.RecordFailure("boom", now));
        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        Assert.Null(delivery.NextAttemptAt);
    }

    [Fact]
    public void Outbox_message_is_dead_lettered_on_the_last_attempt_and_can_be_revived()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var evt = new MemberPulse.Domain.Events.TransactionPosted(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MemberPulse.Domain.Accounts.AccountType.Savings, 5, 5, "x", null, now);
        var message = OutboxMessage.ForTransaction("t", evt, now);

        Assert.False(message.MarkFailed("e", now, maxAttempts: 2));
        Assert.Equal(now + OutboxMessage.BackoffFor(1), message.NextAttemptAt);
        Assert.True(message.MarkFailed("e", now, maxAttempts: 2));
        Assert.Equal(now, message.DeadLetteredAt);

        message.Revive();

        Assert.Null(message.DeadLetteredAt);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.NextAttemptAt);
    }
}

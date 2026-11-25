using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Events;

namespace MemberPulse.Tests.Domain;

public class ConversionTests
{
    private static readonly DateTimeOffset Sent = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid MemberId = Guid.NewGuid();

    private static Campaign NewCampaign(ConversionGoal? goal = null) =>
        Campaign.Create("C", Guid.NewGuid(), CampaignChannel.Email, "x", TransactionDirection.Any, null, null, 0, Sent, goal);

    private static CampaignDelivery SentDelivery(Campaign campaign, Guid? triggerTx = null)
    {
        var d = CampaignDelivery.Create(campaign, MemberId, triggerTx ?? Guid.NewGuid(), "msg", Sent);
        d.MarkSent();
        return d;
    }

    private static TransactionPosted Tx(DateTimeOffset at, decimal amount = -50, string? category = null, Guid? member = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), member ?? MemberId, AccountType.Checking, amount, 1000, "x", category, at);

    [Fact]
    public void Qualifying_transaction_inside_the_window_converts_once_with_its_amount()
    {
        var campaign = NewCampaign();
        var delivery = SentDelivery(campaign);
        var tx = Tx(Sent.AddDays(2), -75.5m);

        Assert.True(delivery.TryConvert(campaign, tx));
        Assert.Equal(tx.OccurredAt, delivery.ConvertedAt);
        Assert.Equal(tx.TransactionId, delivery.ConversionTransactionId);
        Assert.Equal(75.5m, delivery.ConversionAmount);

        Assert.False(delivery.TryConvert(campaign, Tx(Sent.AddDays(3), -999))); // only the first counts
        Assert.Equal(75.5m, delivery.ConversionAmount);
    }

    [Theory]
    [InlineData(0, true)]    // exactly at the end of the window
    [InlineData(1, false)]   // one tick after it
    public void Window_end_is_inclusive(long extraTicks, bool expected)
    {
        var campaign = NewCampaign(ConversionGoal.Create(7, TransactionDirection.Debit, null, null));
        var delivery = SentDelivery(campaign);

        var converted = delivery.TryConvert(campaign, Tx(Sent.AddDays(7).AddTicks(extraTicks)));

        Assert.Equal(expected, converted);
    }

    [Fact]
    public void Transaction_before_or_at_the_message_does_not_convert()
    {
        var campaign = NewCampaign();
        var delivery = SentDelivery(campaign);

        Assert.False(delivery.TryConvert(campaign, Tx(Sent.AddMinutes(-1))));
        Assert.False(delivery.TryConvert(campaign, Tx(Sent)));
    }

    [Fact]
    public void The_triggering_transaction_cannot_convert_its_own_message()
    {
        var campaign = NewCampaign();
        var trigger = Guid.NewGuid();
        var delivery = SentDelivery(campaign, trigger);

        Assert.False(delivery.TryConvert(campaign, Tx(Sent.AddHours(1), id: trigger)));
    }

    [Fact]
    public void Only_sent_messages_can_convert()
    {
        var campaign = NewCampaign();
        var pending = CampaignDelivery.Create(campaign, MemberId, Guid.NewGuid(), "msg", Sent);
        var failed = CampaignDelivery.Create(campaign, MemberId, Guid.NewGuid(), "msg", Sent);
        failed.MarkFailed("boom");
        var tx = Tx(Sent.AddHours(1));

        Assert.False(pending.TryConvert(campaign, tx));
        Assert.False(failed.TryConvert(campaign, tx));
    }

    [Fact]
    public void Another_members_transaction_does_not_convert()
    {
        var campaign = NewCampaign();
        var delivery = SentDelivery(campaign);

        Assert.False(delivery.TryConvert(campaign, Tx(Sent.AddHours(1), member: Guid.NewGuid())));
    }

    [Fact]
    public void Goal_filters_apply_to_the_converting_transaction()
    {
        var campaign = NewCampaign(ConversionGoal.Create(7, TransactionDirection.Debit, 100, "Travel"));
        var delivery = SentDelivery(campaign);

        Assert.False(delivery.TryConvert(campaign, Tx(Sent.AddHours(1), -500, "Dining")));  // wrong category
        Assert.False(delivery.TryConvert(campaign, Tx(Sent.AddHours(2), -50, "Travel")));   // too small
        Assert.False(delivery.TryConvert(campaign, Tx(Sent.AddHours(3), +500, "Travel")));  // wrong direction
        Assert.True(delivery.TryConvert(campaign, Tx(Sent.AddHours(4), -500, "travel")));
    }

    [Fact]
    public void Default_goal_is_any_debit_within_seven_days()
    {
        var goal = NewCampaign().Conversion;

        Assert.Equal(7, goal.WindowDays);
        Assert.Equal(TransactionDirection.Debit, goal.Direction);
        Assert.Null(goal.MinAmount);
        Assert.Null(goal.MerchantCategory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void Window_must_be_between_1_and_90_days(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConversionGoal.Create(days, TransactionDirection.Debit, null, null));
    }
}

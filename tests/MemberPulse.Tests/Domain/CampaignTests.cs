using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Events;

namespace MemberPulse.Tests.Domain;

public class CampaignTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static Campaign NewCampaign(
        TransactionDirection direction = TransactionDirection.Any,
        decimal? minAmount = null, string? category = null, string template = "Hi {FirstName}") =>
        Campaign.Create("C", Guid.NewGuid(), CampaignChannel.Email, template, direction, minAmount, category, 0, Now);

    private static TransactionPosted Tx(decimal amount, string? category = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AccountType.Checking, amount, 1000, "x", category, Now);

    [Fact]
    public void Direction_filter_separates_debits_and_credits()
    {
        Assert.True(NewCampaign(TransactionDirection.Debit).AppliesTo(Tx(-5)));
        Assert.False(NewCampaign(TransactionDirection.Debit).AppliesTo(Tx(5)));
        Assert.True(NewCampaign(TransactionDirection.Credit).AppliesTo(Tx(5)));
        Assert.False(NewCampaign(TransactionDirection.Credit).AppliesTo(Tx(-5)));
        Assert.True(NewCampaign().AppliesTo(Tx(-5)) && NewCampaign().AppliesTo(Tx(5)));
    }

    [Fact]
    public void Min_amount_uses_the_absolute_value()
    {
        var campaign = NewCampaign(minAmount: 100);

        Assert.True(campaign.AppliesTo(Tx(-100)));
        Assert.True(campaign.AppliesTo(Tx(250)));
        Assert.False(campaign.AppliesTo(Tx(-99.99m)));
    }

    [Fact]
    public void Merchant_category_matches_ignoring_case_and_rejects_others()
    {
        var campaign = NewCampaign(category: "Travel");

        Assert.True(campaign.AppliesTo(Tx(-5, "travel")));
        Assert.False(campaign.AppliesTo(Tx(-5, "Dining")));
        Assert.False(campaign.AppliesTo(Tx(-5, null)));
    }

    [Fact]
    public void Paused_campaign_applies_to_nothing_until_resumed()
    {
        var campaign = NewCampaign();
        campaign.Pause();
        Assert.False(campaign.AppliesTo(Tx(-5)));

        campaign.Resume();
        Assert.True(campaign.AppliesTo(Tx(-5)));
    }

    [Fact]
    public void Render_fills_placeholders()
    {
        var campaign = NewCampaign(template: "{FirstName}, your {MerchantCategory} purchase of ${Amount} caught our eye");

        var text = campaign.Render("Aisha", Tx(-1234.5m, "Travel"));

        Assert.Equal("Aisha, your Travel purchase of $1234.50 caught our eye", text);
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => NewCampaign(template: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewCampaign(minAmount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Campaign.Create("C", Guid.NewGuid(), CampaignChannel.Sms, "x", TransactionDirection.Any, null, null, -1, Now));
    }
}

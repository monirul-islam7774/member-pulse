using MemberPulse.Domain.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemberPulse.Infrastructure.Persistence.Configurations;

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.MessageTemplate).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.MerchantCategory).HasMaxLength(50);
        builder.Property(c => c.MinAmount).HasPrecision(18, 2);
        builder.Property(c => c.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        builder.OwnsOne(c => c.Conversion, o =>
        {
            o.Property(x => x.WindowDays).HasColumnName("ConversionWindowDays");
            o.Property(x => x.Direction).HasColumnName("ConversionDirection").HasConversion<string>().HasMaxLength(20);
            o.Property(x => x.MinAmount).HasColumnName("ConversionMinAmount").HasPrecision(18, 2);
            o.Property(x => x.MerchantCategory).HasColumnName("ConversionMerchantCategory").HasMaxLength(50);
        });
        builder.Navigation(c => c.Conversion).IsRequired();

        builder.HasOne<MemberPulse.Domain.Segmentation.Segment>()
            .WithMany()
            .HasForeignKey(c => c.SegmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CampaignDeliveryConfiguration : IEntityTypeConfiguration<CampaignDelivery>
{
    public void Configure(EntityTypeBuilder<CampaignDelivery> builder)
    {
        builder.ToTable("CampaignDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Message).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.Error).HasMaxLength(500);
        builder.Property(d => d.ConversionAmount).HasPrecision(18, 2);
        builder.Ignore(d => d.IsConverted);
        builder.Property(d => d.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);

        // A transaction can trigger a given campaign only once, even if its event is delivered twice.
        builder.HasIndex(d => new { d.CampaignId, d.TransactionId }).IsUnique();
        builder.HasIndex(d => new { d.CampaignId, d.MemberId, d.CreatedAt });
        builder.HasIndex(d => new { d.MemberId, d.CreatedAt }); // conversion lookups

        builder.HasOne<Campaign>().WithMany().HasForeignKey(d => d.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MemberPulse.Domain.Members.Member>().WithMany().HasForeignKey(d => d.MemberId).OnDelete(DeleteBehavior.Restrict);
    }
}

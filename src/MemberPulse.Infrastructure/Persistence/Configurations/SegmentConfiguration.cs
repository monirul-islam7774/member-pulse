using MemberPulse.Domain.Segmentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemberPulse.Infrastructure.Persistence.Configurations;

internal sealed class SegmentConfiguration : IEntityTypeConfiguration<Segment>
{
    public void Configure(EntityTypeBuilder<Segment> builder)
    {
        builder.ToTable("Segments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.HasIndex(s => s.Name).IsUnique();

        builder.OwnsMany(s => s.Criteria, c =>
        {
            c.ToTable("SegmentCriteria");
            c.WithOwner().HasForeignKey("SegmentId");
            c.Property<int>("Id").ValueGeneratedOnAdd();
            c.HasKey("Id");
            c.Property(x => x.Metric).HasConversion<string>().HasMaxLength(30);
            c.Property(x => x.Operator).HasConversion<string>().HasMaxLength(30);
            c.Property(x => x.Value).HasPrecision(18, 2);
            c.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(30);
            c.Property(x => x.MerchantCategory).HasMaxLength(50);
        });

        builder.Navigation(s => s.Criteria).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

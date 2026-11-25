using MemberPulse.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemberPulse.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Topic).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Key).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Type).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(500);

        // The dispatcher only ever looks at unprocessed rows.
        builder.HasIndex(m => m.CreatedAt).HasFilter("[ProcessedAt] IS NULL");
    }
}

using FusionOps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Outbox;

public class OutboxConfig : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("Outbox");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
               .ValueGeneratedNever();

        builder.Property(o => o.Type)
               .HasMaxLength(200)
               .IsRequired();

        builder.HasIndex(o => o.ProcessedAt)
               .HasDatabaseName("IX_Outbox_ProcessedAt");
    }
}
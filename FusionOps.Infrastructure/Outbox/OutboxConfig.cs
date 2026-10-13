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

        builder.Property(o => o.Payload).IsRequired();
        builder.Property(o => o.Actor).HasMaxLength(200).IsRequired();
        builder.Property(o => o.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(o => o.LastError).HasMaxLength(2000);

        builder.HasIndex(o => new { o.ProcessedAt, o.DeadLetteredAt, o.NextAttemptAt, o.OccurredOn })
               .HasDatabaseName("IX_Outbox_Dispatch");
    }
}

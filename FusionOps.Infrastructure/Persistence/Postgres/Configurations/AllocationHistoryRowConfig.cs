using FusionOps.Infrastructure.Persistence.Postgres.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Persistence.Postgres.Configurations;

public class AllocationHistoryRowConfig : IEntityTypeConfiguration<AllocationHistoryRow>
{
    public void Configure(EntityTypeBuilder<AllocationHistoryRow> builder)
    {
        builder.ToTable("allocation_history");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.AllocationId).HasColumnName("allocation_id");
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.Property(x => x.ResourceId).HasColumnName("resource_id");
        builder.Property(x => x.FromTs).HasColumnName("from_ts");
        builder.Property(x => x.ToTs).HasColumnName("to_ts");
        builder.Property(x => x.Recorded).HasColumnName("recorded_at");
        builder.Property(x => x.Sku).HasColumnName("sku");
        builder.Property(x => x.Qty).HasColumnName("qty");

        builder.HasIndex(x => new { x.ProjectId, x.Recorded }).HasDatabaseName("ix_history_project_rec");
    }
}

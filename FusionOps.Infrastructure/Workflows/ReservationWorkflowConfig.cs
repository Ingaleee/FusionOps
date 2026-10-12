using FusionOps.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Workflows;

public sealed class ReservationWorkflowConfig : IEntityTypeConfiguration<ReservationWorkflow>
{
    public void Configure(EntityTypeBuilder<ReservationWorkflow> b)
    {
        b.ToTable("reservation_workflows");
        b.HasKey(w => w.Id);
        b.Property(w => w.Id).ValueGeneratedNever();
        b.Property(w => w.Version).IsConcurrencyToken();
        b.Property(w => w.State).HasConversion<string>().HasMaxLength(32);
        b.Property(w => w.PausedStep).HasConversion<string>().HasMaxLength(32);
        b.Property(w => w.Outcome).HasConversion<string>().HasMaxLength(32);
        b.Property(w => w.SubmittedBy).HasMaxLength(200);
        b.Property(w => w.ApprovedBy).HasMaxLength(200);
        b.Property(w => w.SpecificationHash).HasMaxLength(64);
        b.Property(w => w.FailureCode).HasMaxLength(100);
        b.Property(w => w.LastErrorCode).HasMaxLength(100);
        b.Ignore(w => w.DomainEvents);
        b.Ignore(w => w.ResourceIds);
        b.Ignore(w => w.AllocationIds);
        b.Property<Guid[]>("_resourceIds").HasField("_resourceIds").HasColumnName("ResourceIds").IsRequired();
        b.Property<Guid[]>("_allocationIds").HasField("_allocationIds").HasColumnName("AllocationIds").IsRequired();
        b.ComplexProperty(w => w.Period, p =>
        {
            p.IsRequired();
            p.Property(v => v.Start).HasColumnName("PeriodStart");
            p.Property(v => v.End).HasColumnName("PeriodEnd");
        });
        b.Property<Guid?>("LeaseToken");
        b.Property<DateTimeOffset?>("LeaseExpiresAt");
        b.Property<long>("LeaseEpoch");
        b.HasIndex(w => w.CreatedAt);
        b.HasIndex(nameof(ReservationWorkflow.State), nameof(ReservationWorkflow.NextAttemptAt), "LeaseExpiresAt");
        b.OwnsMany(w => w.Licenses, l =>
        {
            l.ToTable("workflow_license_requirements");
            l.WithOwner().HasForeignKey("WorkflowId");
            l.Property(r => r.Product).HasMaxLength(128);
            l.HasKey("WorkflowId", nameof(LicenseRequirement.Product));
        });
        b.OwnsMany(w => w.Transitions, t =>
        {
            t.ToTable("workflow_transitions");
            t.WithOwner().HasForeignKey("WorkflowId");
            t.HasKey("WorkflowId", nameof(WorkflowTransition.Version));
            t.Property(r => r.Version).ValueGeneratedNever();
            t.Property(r => r.From).HasConversion<string>().HasMaxLength(32);
            t.Property(r => r.To).HasConversion<string>().HasMaxLength(32);
            t.Property(r => r.Actor).HasMaxLength(200);
            t.Property(r => r.Reason).HasMaxLength(100);
        });
    }
}

public sealed class WorkflowEffectReceiptConfig : IEntityTypeConfiguration<WorkflowEffectReceipt>
{
    public void Configure(EntityTypeBuilder<WorkflowEffectReceipt> b)
    {
        b.ToTable("workflow_effect_inbox");
        b.HasKey(r => new { r.WorkflowId, r.Step });
        b.Property(r => r.Step).HasMaxLength(32);
        b.Property(r => r.SpecificationHash).HasMaxLength(64);
        b.Property(r => r.RejectionCode).HasMaxLength(100);
    }
}

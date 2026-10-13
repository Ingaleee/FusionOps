using FusionOps.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Projects;

public sealed class ProjectConfig : IEntityTypeConfiguration<OperationalProject>
{
    public void Configure(EntityTypeBuilder<OperationalProject> b)
    {
        b.ToTable("ops_projects"); b.HasKey(p => p.Id); b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Version).IsConcurrencyToken();
        b.Property(p => p.Name).HasMaxLength(200); b.Property(p => p.CreatedBy).HasMaxLength(200);
        b.Property(p => p.DefinitionDigest).HasMaxLength(64); b.Property(p => p.InitialDefinitionDigest).HasMaxLength(64);
        b.Ignore(p => p.Definition); b.Ignore(p => p.DomainEvents);
        b.HasIndex(p => new { p.TenantId, p.Id });
        b.OwnsMany(p => p.Members, m =>
        {
            m.ToTable("ops_project_members");
            m.WithOwner().HasForeignKey("TenantId", "ProjectId").HasPrincipalKey(nameof(OperationalProject.TenantId), nameof(OperationalProject.Id));
            m.Property(x => x.Subject).HasMaxLength(200); m.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            m.HasKey("ProjectId", nameof(ProjectMember.Subject));
            m.HasIndex("TenantId", nameof(ProjectMember.Subject), "ProjectId");
        });
    }
}
public sealed class ProjectRevisionConfig : IEntityTypeConfiguration<ProjectDefinitionRevision>
{
    public void Configure(EntityTypeBuilder<ProjectDefinitionRevision> b)
    {
        b.ToTable("ops_project_revisions"); b.HasKey(p => new { p.ProjectId, p.DefinitionVersion });
        b.Property(p => p.Actor).HasMaxLength(200); b.Property(p => p.Digest).HasMaxLength(64); b.Ignore(p => p.Definition);
        b.HasOne<OperationalProject>().WithMany().HasForeignKey(p => new { p.TenantId, p.ProjectId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

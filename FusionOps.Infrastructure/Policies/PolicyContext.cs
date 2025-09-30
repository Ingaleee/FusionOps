using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Policies;

public sealed class PolicyContext : DbContext
{
    public PolicyContext(DbContextOptions<PolicyContext> options) : base(options) { }

    public DbSet<PolicyDocument> PolicyDocuments => Set<PolicyDocument>();
    public DbSet<PolicyDecisionRecord> PolicyDecisions => Set<PolicyDecisionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PolicyDocument>(b =>
        {
            b.ToTable("policy_documents");
            b.HasKey(x => x.Id);
            b.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.Engine).HasMaxLength(16).IsRequired();
            b.Property(x => x.Status).HasMaxLength(16).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Name, x.Status });
        });

        modelBuilder.Entity<PolicyDecisionRecord>(b =>
        {
            b.ToTable("policy_decisions");
            b.HasKey(x => x.Id);
            b.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            b.Property(x => x.PolicyName).HasMaxLength(128).IsRequired();
            b.Property(x => x.Engine).HasMaxLength(16).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.PolicyName, x.OccurredAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}

public sealed class PolicyDocument
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = null!;
    public string Name { get; set; } = null!;          // policy set name
    public string Engine { get; set; } = null!;        // nrules | rego
    public int Version { get; set; }
    public string Status { get; set; } = "draft";      // draft | active | retired
    public string Source { get; set; } = string.Empty; // C# for nrules or rego text
    public byte[]? Wasm { get; set; }                  // compiled rego
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PolicyDecisionRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = null!;
    public string PolicyName { get; set; } = null!;
    public int Version { get; set; }
    public string Engine { get; set; } = null!;
    public string DecisionJson { get; set; } = string.Empty;
    public string InputFingerprint { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? RuleMatchesJson { get; set; }
}




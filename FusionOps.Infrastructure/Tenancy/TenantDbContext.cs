using System.Linq.Expressions;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Tenancy;

public abstract class TenantDbContext(DbContextOptions options, TenantScope? scope) : DbContext(options)
{
    private bool _scopeSealed;
    public TenantScope? Scope { get; private set; } = scope ?? TenantExecution.Current;
    public Guid TenantId => Scope?.Id ?? Guid.Empty;
    public bool SystemAccess => Scope?.IsSystem == true;
    public void BindScope(TenantScope scope)
    {
        if (_scopeSealed || Database.CurrentTransaction is not null || Database.GetDbConnection().State != System.Data.ConnectionState.Closed || ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A context scope can be selected only before its first database operation");
        Scope = scope;
    }
    internal void SealScope()
    {
        if (Scope is null || !SystemAccess && TenantId == Guid.Empty) throw new TenantRequiredException();
        _scopeSealed = true;
    }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(new TenantConnectionInterceptor(this));

    protected void ConfigureTenancy(ModelBuilder model)
    {
        var tenantTypes = model.Model.GetEntityTypes().Where(t => typeof(TenantOwnedEntity).IsAssignableFrom(t.ClrType) && !t.IsOwned()).Select(t => t.ClrType).ToArray();
        foreach (var type in tenantTypes)
        {
            var b = model.Entity(type);
            b.HasBaseType((Type?)null);
            b.Property<Guid>(nameof(TenantOwnedEntity.TenantId)).HasDefaultValue(TenantIdentity.Legacy).ValueGeneratedNever().IsConcurrencyToken();
            var e = Expression.Parameter(type, "entity");
            var allowed = Expression.OrElse(Expression.Property(Expression.Constant(this), nameof(SystemAccess)),
                Expression.Equal(Expression.Property(e, nameof(TenantOwnedEntity.TenantId)), Expression.Property(Expression.Constant(this), nameof(TenantId))));
            b.HasQueryFilter(Expression.Lambda(allowed, e));
        }
        foreach (var owned in model.Model.GetEntityTypes().Where(t => t.IsOwned() && t.FindProperty("TenantId") is not null).ToArray())
        {
            var property = owned.FindProperty("TenantId")!;
            property.SetDefaultValue(TenantIdentity.Legacy);
            property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }
    private void ValidateWrites()
    {
        if (Scope is null || !SystemAccess && TenantId == Guid.Empty) throw new TenantRequiredException();
        if (ChangeTracker.Entries<FusionOps.Domain.Projects.ProjectDefinitionRevision>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new FusionOps.Domain.Projects.ProjectRuleException("project_revision_immutable");
        if (ChangeTracker.Entries<FusionOps.Domain.Scheduling.TemporalScenario>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new FusionOps.Domain.Scheduling.TemporalModelException("scenario_immutable");
        if (ChangeTracker.Entries<FusionOps.Domain.Execution.ExecutionBaseline>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<FusionOps.Domain.Execution.ExecutionPlanDecision>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new FusionOps.Domain.Execution.ExecutionPlanRuleException("execution_history_immutable");
        foreach (var entry in ChangeTracker.Entries<TenantOwnedEntity>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                if (SystemAccess) throw new TenantRequiredException();
                entry.Entity.BindTenant(TenantId);
            }
            if (entry.Entity.TenantId == Guid.Empty || !SystemAccess && entry.Entity.TenantId != TenantId
                || entry.State != EntityState.Added && entry.OriginalValues.GetValue<Guid>(nameof(TenantOwnedEntity.TenantId)) != entry.Entity.TenantId)
                throw new TenantBoundaryException();
        }
        ChangeTracker.DetectChanges();
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ValidateWrites(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

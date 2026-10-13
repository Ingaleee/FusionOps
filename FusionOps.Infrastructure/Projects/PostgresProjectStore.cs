using System.Data;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Projects;
using FusionOps.Domain.Projects;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FusionOps.Infrastructure.Projects;

public sealed class PostgresProjectStore(IDbContextFactory<FulfillmentContext> factory, IExecutionContext execution,
    TimeProvider clock, ProjectAccessPolicy policy) : IProjectStore
{
    public async Task<OperationalProject> CreateAsync(Guid id, ProjectDefinition definition, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var project = OperationalProject.Create(id, db.TenantId, definition, execution.Actor, clock.GetUtcNow());
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await WorkflowPersistence.LockAsync(db, id, "project-create", ct);
        await LockAsync(db, id, ct);
        var existing = await Query(db).SingleOrDefaultAsync(p => p.Id == id, ct);
        if (existing is not null)
        {
            existing.Require(execution.Actor, ProjectPermission.Read); existing.VerifyEvidence();
            if (existing.CreatedBy != execution.Actor || existing.InitialDefinitionDigest != project.InitialDefinitionDigest)
                throw new ProjectSubmissionConflictException();
            return existing;
        }
        db.Add(project); db.Add(ProjectDefinitionRevision.Capture(project, execution.Actor));
        try { await CommitAsync(db, project, tx, ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" }) { throw new ProjectSubmissionConflictException(); }
        return project;
    }
    public async Task<OperationalProject?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var project = await Query(db).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Members.Any(m => m.Subject == execution.Actor), ct);
        project?.VerifyEvidence(); return project;
    }
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(Guid? after, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new ProjectRuleException("project_page_invalid");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        return await db.Set<OperationalProject>().AsNoTracking().Where(p => p.Members.Any(m => m.Subject == execution.Actor)
            && (after == null || p.Id.CompareTo(after.Value) > 0)).OrderBy(p => p.Id).Take(limit)
            .Select(p => new ProjectSummary(p.Id, p.Name, p.Version, p.DefinitionVersion, p.DefinitionDigest,
                p.CreatedBy, p.CreatedAt, p.UpdatedAt, p.Members.Where(m => m.Subject == execution.Actor).Select(m => m.Role).First()))
            .ToListAsync(ct);
    }
    public async Task RequireAsync(Guid projectId, ProjectPermission permission, CancellationToken ct)
    {
        if (!await CanAsync(projectId, permission, ct)) throw new ProjectAccessException();
    }
    public async Task<bool> CanAsync(Guid projectId, ProjectPermission permission, CancellationToken ct)
    {
        if (projectId == Guid.Empty) throw new ProjectRuleException("project_id_required");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var project = await Query(db).AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return policy.AllowUnregisteredDevelopmentProjects;
        project.VerifyEvidence(); return project.Can(execution.Actor, permission);
    }
    public Task<OperationalProject?> ReplaceAsync(Guid id, long version, ProjectDefinition definition, CancellationToken ct) =>
        MutateAsync(id, version, ProjectPermission.EditDefinition, p => p.ReplaceDefinition(definition, execution.Actor, clock.GetUtcNow()), ct);
    public Task<OperationalProject?> SetMemberAsync(Guid id, long version, string subject, ProjectRole role, CancellationToken ct) =>
        MutateAsync(id, version, ProjectPermission.ManageMembers, p => p.SetMember(subject, role, execution.Actor, clock.GetUtcNow()), ct);
    public Task<OperationalProject?> RemoveMemberAsync(Guid id, long version, string subject, CancellationToken ct) =>
        MutateAsync(id, version, ProjectPermission.ManageMembers, p => p.RemoveMember(subject, execution.Actor, clock.GetUtcNow()), ct);
    public Task<OperationalProject?> TransferOwnerAsync(Guid id, long version, string subject, CancellationToken ct) =>
        MutateAsync(id, version, ProjectPermission.ManageMembers, p => p.TransferOwnership(subject, execution.Actor, clock.GetUtcNow()), ct);
    private async Task<OperationalProject?> MutateAsync(Guid id, long version, ProjectPermission permission, Action<OperationalProject> change, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if ((await LockAsync(db, id, ct)).Count == 0) return null;
        var project = await Query(db).SingleAsync(p => p.Id == id, ct);
        project.Require(execution.Actor, permission); project.VerifyEvidence();
        if (project.Version != version) throw new ProjectVersionException();
        var definitionVersion = project.DefinitionVersion;
        change(project);
        if (definitionVersion != project.DefinitionVersion) db.Add(ProjectDefinitionRevision.Capture(project, execution.Actor));
        await CommitAsync(db, project, tx, ct); return project;
    }
    public async Task<IReadOnlyList<ProjectDefinitionRevision>?> RevisionsAsync(Guid id, long? before, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 50 || before is < 1) throw new ProjectRuleException("project_history_page_invalid");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var project = await Query(db).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (project is null || !project.Can(execution.Actor, ProjectPermission.Read)) return null;
        var revisions = await db.Set<ProjectDefinitionRevision>().AsNoTracking().Where(r => r.ProjectId == id && (before == null || r.DefinitionVersion < before))
            .OrderByDescending(r => r.DefinitionVersion).Take(limit).ToListAsync(ct);
        foreach (var revision in revisions) revision.VerifyEvidence(); return revisions;
    }
    private static IQueryable<OperationalProject> Query(FulfillmentContext db) => db.Set<OperationalProject>().Include(p => p.Members).AsSplitQuery();
    private static Task<List<Guid>> LockAsync(FulfillmentContext db, Guid id, CancellationToken ct) =>
        db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM ops_projects WHERE \"TenantId\" = {db.TenantId} AND \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
    private async Task CommitAsync(FulfillmentContext db, OperationalProject project, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, CancellationToken ct)
    {
        WorkflowPersistence.StageEvents(db, execution.Actor, project.Id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); project.ClearDomainEvents();
    }
}

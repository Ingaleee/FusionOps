using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Projects;
using FusionOps.Application.Scheduling;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FusionOps.Infrastructure.Scheduling;

public sealed class PostgresTemporalScenarioStore(IDbContextFactory<FulfillmentContext> factory, IProjectAccess access,
    IExecutionContext execution, TimeProvider clock, TemporalSnapshotCapture snapshots, TemporalPlanner planner) : ITemporalScenarioStore
{
    public async Task<TemporalScenario> CreateAsync(Guid projectId, TemporalScenarioRequest request, CancellationToken ct)
    {
        if (request.Id == Guid.Empty || request.ExpectedProjectVersion < 1 || request.CandidateIds is null || request.CandidateIds.Count > 200
            || request.CandidateIds.Any(id => id == Guid.Empty) || request.CandidateIds.Distinct().Count() != request.CandidateIds.Count
            || request.GridMinutes is < 1 or > 60 || request.MaxSearchNodes is < 1 or > 1_000_000 || request.MaxElapsedMilliseconds is < 1 or > 30000)
            throw new TemporalModelException("scenario_request_invalid");
        var assumptions = (request.Assumptions ?? ScenarioAssumptions.None).Normalize(request.CandidateIds);
        await access.RequireAsync(projectId, ProjectPermission.Plan, ct);
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { ProjectId = projectId, request.ExpectedProjectVersion, Candidates = request.CandidateIds.Order().ToArray(), request.GridMinutes,
            request.MaxSearchNodes, request.MaxElapsedMilliseconds, Assumptions = assumptions }, TemporalInputCodec.Json)));
        await using (var read = await factory.CreateDbContextAsync(ct))
        {
            var existing = await read.Set<TemporalScenario>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.Id, ct);
            if (existing is not null) return Resolve(existing, projectId, requestHash);
        }
        var capture = await snapshots.CaptureAsync(projectId, new(request.ExpectedProjectVersion, request.CandidateIds,
            request.GridMinutes, request.MaxSearchNodes, request.MaxElapsedMilliseconds), execution.Actor, ct);
        var input = TemporalInputCodec.Read(TemporalInputCodec.Write(assumptions.Apply(capture.Input)));
        var result = planner.Plan(input, ct);
        var validation = new TemporalPlanValidator().Validate(input, result.Tasks, requireComplete: false);
        await using var db = await factory.CreateDbContextAsync(ct);
        var scenario = TemporalScenario.Create(request.Id, db.TenantId, execution.Actor, requestHash,
            new(capture.Manifest, capture.Input, assumptions, input, result, validation), clock.GetUtcNow());
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await WorkflowPersistence.LockAsync(db, scenario.Id, "temporal-scenario", ct);
        var replay = await db.Set<TemporalScenario>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == scenario.Id, ct);
        if (replay is not null) return Resolve(replay, projectId, requestHash);
        await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM ops_projects WHERE \"TenantId\" = {db.TenantId} AND \"Id\" = {projectId} FOR UPDATE").ToListAsync(ct);
        var project = await db.Set<OperationalProject>().Include(p => p.Members).AsSplitQuery().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ProjectAccessException();
        project.Require(execution.Actor, ProjectPermission.Plan); project.VerifyEvidence();
        if (project.Version != request.ExpectedProjectVersion || project.DefinitionDigest != capture.Manifest.DefinitionDigest) throw new ProjectVersionException();
        db.Add(scenario); WorkflowPersistence.StageEvents(db, execution.Actor, scenario.Id);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" }) { throw new ScenarioSubmissionConflictException(); }
        scenario.ClearDomainEvents(); return scenario;
    }
    private TemporalScenario Resolve(TemporalScenario existing, Guid projectId, string requestHash)
    {
        if (existing.ProjectId != projectId || existing.Actor != execution.Actor || existing.RequestHash != requestHash) throw new ScenarioSubmissionConflictException();
        existing.VerifyEvidence(); return existing;
    }
    public async Task<TemporalScenario?> GetAsync(Guid projectId, Guid scenarioId, CancellationToken ct)
    {
        if (!await access.CanAsync(projectId, ProjectPermission.Read, ct)) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var scenario = await db.Set<TemporalScenario>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == scenarioId && s.ProjectId == projectId, ct);
        scenario?.VerifyEvidence(); return scenario;
    }
    public async Task<IReadOnlyList<TemporalScenarioSummary>> ListAsync(Guid projectId, Guid? after, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new TemporalModelException("scenario_page_invalid");
        await access.RequireAsync(projectId, ProjectPermission.Read, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Set<TemporalScenario>().AsNoTracking().Where(s => s.ProjectId == projectId && (after == null || s.Id.CompareTo(after.Value) > 0))
            .OrderBy(s => s.Id).Take(limit).Select(s => new TemporalScenarioSummary(s.Id, s.ProjectVersion, s.DefinitionVersion, s.Digest, s.Actor,
                s.CreatedAt, s.IsComplete, s.SearchComplete, s.IsHypothetical, s.TotalCost, s.FinishAt)).ToListAsync(ct);
    }
    public async Task<ScenarioReplay?> ReplayAsync(Guid projectId, Guid scenarioId, CancellationToken ct)
    {
        await access.RequireAsync(projectId, ProjectPermission.Plan, ct);
        var scenario = await GetAsync(projectId, scenarioId, ct); if (scenario is null) return null;
        var evidence = scenario.Evidence;
        var result = planner.Plan(evidence.Input, ct);
        var validation = new TemporalPlanValidator().Validate(evidence.Input, result.Tasks, requireComplete: false);
        var matches = JsonSerializer.Serialize(result with { ElapsedMilliseconds = 0 }, TemporalInputCodec.Json)
            == JsonSerializer.Serialize(evidence.Result with { ElapsedMilliseconds = 0 }, TemporalInputCodec.Json);
        return new(result, validation, matches);
    }
    public async Task<ScenarioComparison?> CompareAsync(Guid projectId, Guid left, Guid right, CancellationToken ct)
    {
        var a = await GetAsync(projectId, left, ct); var b = await GetAsync(projectId, right, ct); if (a is null || b is null) return null;
        var tasksA = a.Evidence.Result.Tasks.ToDictionary(t => t.TaskId); var tasksB = b.Evidence.Result.Tasks.ToDictionary(t => t.TaskId);
        var changed = tasksA.Keys.Union(tasksB.Keys).Where(id => !tasksA.TryGetValue(id, out var old) || !tasksB.TryGetValue(id, out var current)
            || old.Period != current.Period || old.Cost != current.Cost || !old.Resources.SequenceEqual(current.Resources)).Order().ToArray();
        return new(left, right, a.DefinitionVersion, b.DefinitionVersion, a.IsHypothetical, b.IsHypothetical, b.TotalCost - a.TotalCost,
            a.FinishAt is { } start && b.FinishAt is { } finish ? (finish - start).TotalMinutes : null, changed);
    }
}

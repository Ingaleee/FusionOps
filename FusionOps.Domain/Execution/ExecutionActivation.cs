using System.Text.Json;
using FusionOps.Domain.Events;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Tenancy;

namespace FusionOps.Domain.Execution;

public enum ActivationState { ReservingResources, ReservingLicenses, Active, ReleasingLicenses, ReleasingResources, Rejected, Compensated, NeedsAttention, Completed }
public enum ActivationStepOutcome { Confirmed, Rejected, Unresolved }
public sealed record ExecutionResourceOwnership(Guid TaskId, Guid ResourceId, Guid AllocationId);
public sealed record ActivationStepResult(ActivationStepOutcome Outcome, string? Code = null, IReadOnlyList<ExecutionResourceOwnership>? Resources = null);
public enum ActivationMutation { Cancel, Resume }

/// <summary>Reservation activation, distinct from approval and the later actual task lifecycle.</summary>
public sealed class ExecutionActivation : TenantOwnedEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];
    private ExecutionActivation() { }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Digest { get; private set; } = string.Empty;
    public long ApprovedPlanVersion { get; private set; }
    public string RequestedBy { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public ActivationState State { get; private set; }
    public ActivationState? PausedStep { get; private set; }
    public bool CancellationRequested { get; private set; }
    public bool WorkCompletionRequested { get; private set; }
    public string? FailureCode { get; private set; }
    public string? LastErrorCode { get; private set; }
    public int StepAttempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ExecutionBaseline Baseline { get; private set; } = null!;
    public string ResourceOwnershipJson { get; private set; } = "[]";
    public IReadOnlyList<ExecutionResourceOwnership> ResourceOwnership => JsonSerializer.Deserialize<ExecutionResourceOwnership[]>(ResourceOwnershipJson)!;
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    public void AddDomainEvent(IDomainEvent e) => _events.Add(e);
    public void ClearDomainEvents() => _events.Clear();
    public static bool IsExecutable(ActivationState state) => state is ActivationState.ReservingResources or ActivationState.ReservingLicenses or ActivationState.ReleasingLicenses or ActivationState.ReleasingResources;
    public static bool HoldsProject(ActivationState state) => state is not (ActivationState.Rejected or ActivationState.Compensated or ActivationState.Completed);

    public static ExecutionActivation Request(ExecutionPlan plan, string digest, string actor, DateTimeOffset now)
    {
        plan.Baseline.VerifyEvidence(); plan.RequireValidApproval(now);
        if (!ProjectDefinitionCodec.Text(actor, 200) || digest != plan.Baseline.Digest) throw new ExecutionPlanRuleException("execution_reviewed_digest_mismatch");
        if (plan.Baseline.ScenarioEvidence.Input.Definition.Tasks.Any(t => t.Materials.Count > 0)) throw new ExecutionPlanRuleException("execution_material_participant_unsupported");
        if (now >= plan.Baseline.ScenarioEvidence.Result.Tasks.Min(t => t.Period.Start)) throw new ExecutionPlanRuleException("execution_activation_window_closed");
        var activation = new ExecutionActivation { Id = plan.Id, ProjectId = plan.ProjectId, Baseline = plan.Baseline, Digest = digest,
            ApprovedPlanVersion = plan.Version, RequestedBy = actor, CreatedAt = Normalize(now), State = ActivationState.ReservingResources };
        activation.BindTenant(plan.TenantId); activation.Record(actor, "activation_requested", now); return activation;
    }

    public void RequestCancellation(string actor, string reason, DateTimeOffset now)
    {
        RequireTime(now);
        if (!ProjectDefinitionCodec.Text(actor, 200) || !ProjectDefinitionCodec.Text(reason, 100)) throw new ExecutionPlanRuleException("execution_cancellation_reason_required");
        if (CancellationRequested || !HoldsProject(State)) return;
        CancellationRequested = true; FailureCode ??= reason;
        if (State == ActivationState.Active) Move(ActivationState.ReleasingLicenses, actor, reason, now);
        else Record(actor, reason, now); // Resolve the original pending effect before releasing it.
    }
    public void Resume(string actor, DateTimeOffset now)
    {
        RequireTime(now);
        if (!ProjectDefinitionCodec.Text(actor, 200) || State != ActivationState.NeedsAttention || PausedStep is null) throw new ExecutionPlanRuleException("execution_resume_state");
        var step = PausedStep.Value; PausedStep = null; Move(step, actor, "operator_resumed", now);
    }
    public void FinishReportedWork(string actor, IReadOnlyList<ExecutionTask> tasks, DateTimeOffset now)
    {
        RequireTime(now);
        var expected = Baseline.ScenarioEvidence.Result.Tasks.Select(t => t.TaskId).ToHashSet();
        if (State != ActivationState.Active || !ProjectDefinitionCodec.Text(actor, 200) || tasks.Count != expected.Count
            || tasks.Any(t => t.PlanId != Id || t.State != ExecutionTaskState.Completed) || !expected.SetEquals(tasks.Select(t => t.TaskId)))
            throw new ExecutionPlanRuleException("execution_work_incomplete");
        WorkCompletionRequested = true; Move(ActivationState.ReleasingLicenses, actor, "reported_work_completed", now);
    }
    public void CompleteStep(ActivationState expected, ActivationStepResult result, DateTimeOffset now, int maxAttempts, string? admissionFailure = null)
    {
        RequireTime(now);
        if (State != expected || !IsExecutable(expected) || !Enum.IsDefined(result.Outcome) || maxAttempts is < 1 or > 100)
            throw new ExecutionPlanRuleException("execution_step_invalid");
        if (result.Outcome != ActivationStepOutcome.Confirmed && !ProjectDefinitionCodec.Text(result.Code, 100)) throw new ExecutionPlanRuleException("execution_step_code_required");
        if (admissionFailure is not null)
        {
            if (result.Outcome != ActivationStepOutcome.Confirmed || expected is not (ActivationState.ReservingResources or ActivationState.ReservingLicenses)
                || !ProjectDefinitionCodec.Text(admissionFailure, 100)) throw new ExecutionPlanRuleException("execution_step_invalid");
            CancellationRequested = true; FailureCode ??= admissionFailure;
        }
        if (result.Outcome == ActivationStepOutcome.Unresolved || (result.Outcome == ActivationStepOutcome.Rejected && expected is ActivationState.ReleasingLicenses or ActivationState.ReleasingResources))
        {
            LastErrorCode = result.Code; StepAttempts++;
            if (StepAttempts >= maxAttempts) { PausedStep = State; State = ActivationState.NeedsAttention; NextAttemptAt = null; Record("execution-worker", "outcome_unresolved", now); }
            else { NextAttemptAt = Normalize(now).AddSeconds(Math.Min(60, 1 << Math.Min(StepAttempts, 6))); Record("execution-worker", "retry_scheduled", now); }
            return;
        }
        if (result.Outcome == ActivationStepOutcome.Rejected)
        {
            FailureCode ??= result.Code;
            Move(expected == ActivationState.ReservingResources ? ActivationState.Rejected : ActivationState.ReleasingResources, "execution-worker", result.Code!, now); return;
        }
        if (expected == ActivationState.ReservingResources)
        {
            var wanted = Baseline.ScenarioEvidence.Result.Tasks.SelectMany(t => t.Resources.Select(r => (t.TaskId, r.ResourceId))).ToHashSet();
            var actual = result.Resources;
            if (actual is null || actual.Count != wanted.Count || actual.Any(r => r.AllocationId == Guid.Empty)
                || actual.Select(r => r.AllocationId).Distinct().Count() != actual.Count
                || !wanted.SetEquals(actual.Select(r => (r.TaskId, r.ResourceId)))) throw new ExecutionPlanRuleException("execution_resource_result_invalid");
            ResourceOwnershipJson = JsonSerializer.Serialize(actual.OrderBy(r => r.TaskId).ThenBy(r => r.ResourceId));
            Move(CancellationRequested ? ActivationState.ReleasingResources : ActivationState.ReservingLicenses, "execution-worker", admissionFailure ?? "resources_confirmed", now);
        }
        else if (expected == ActivationState.ReservingLicenses) Move(CancellationRequested ? ActivationState.ReleasingLicenses : ActivationState.Active, "execution-worker", admissionFailure ?? "licenses_confirmed", now);
        else Move(expected == ActivationState.ReleasingLicenses ? ActivationState.ReleasingResources : WorkCompletionRequested ? ActivationState.Completed : ActivationState.Compensated, "execution-worker", "ownership_released", now);
    }
    private void Move(ActivationState state, string actor, string reason, DateTimeOffset now)
    { State = state; StepAttempts = 0; LastErrorCode = null; NextAttemptAt = IsExecutable(state) ? Normalize(now) : null; Record(actor, reason, now); }
    private void Record(string actor, string reason, DateTimeOffset now)
    {
        var normalized = Normalize(now); if (normalized < UpdatedAt) throw new ExecutionPlanRuleException("execution_decision_time_invalid");
        Version = checked(Version + 1); UpdatedAt = normalized;
        if (Version == 1) NextAttemptAt = normalized;
        _events.Add(new ExecutionActivationChanged(Id, ProjectId, Version, State, CancellationRequested, actor, reason) { OccurredOn = normalized });
    }
    private static DateTimeOffset Normalize(DateTimeOffset now) => new(now.UtcTicks / 10 * 10, TimeSpan.Zero);
    private void RequireTime(DateTimeOffset now) { if (Normalize(now) < UpdatedAt) throw new ExecutionPlanRuleException("execution_decision_time_invalid"); }
}

public sealed class ExecutionActivationDecision : TenantOwnedEntity
{
    private ExecutionActivationDecision() { }
    public Guid ActivationId { get; private set; }
    public long Version { get; private set; }
    public ActivationState State { get; private set; }
    public ActivationState? PausedStep { get; private set; }
    public bool CancellationRequested { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string? ErrorCode { get; private set; }
    public long LeaseEpoch { get; private set; }
    public DateTimeOffset At { get; private set; }
    public static ExecutionActivationDecision Capture(ExecutionActivation a, long epoch = 0)
    {
        var change = a.DomainEvents.OfType<ExecutionActivationChanged>().Single(e => e.Version == a.Version);
        var decision = new ExecutionActivationDecision { ActivationId = a.Id, Version = a.Version, State = a.State, PausedStep = a.PausedStep,
            CancellationRequested = a.CancellationRequested, Actor = change.Actor, Reason = change.Reason, ErrorCode = a.LastErrorCode ?? a.FailureCode,
            LeaseEpoch = epoch, At = a.UpdatedAt }; decision.BindTenant(a.TenantId); return decision;
    }
}
public sealed class ExecutionActivationConflictException() : Exception("Another activation holds this project or the original request identity differs");

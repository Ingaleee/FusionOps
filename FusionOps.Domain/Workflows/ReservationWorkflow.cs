using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Domain.Events;
using FusionOps.Domain.Shared;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Workflows;

public sealed class ReservationWorkflow : IHasDomainEvents
{
    private Guid[] _resourceIds = [];
    private Guid[] _allocationIds = [];
    private readonly List<LicenseRequirement> _licenses = [];
    private readonly List<WorkflowTransition> _transitions = [];
    private readonly List<IDomainEvent> _domainEvents = [];
    private ReservationWorkflow() { }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public TimeRange Period { get; private set; } = null!;
    public IReadOnlyList<Guid> ResourceIds => Array.AsReadOnly(_resourceIds);
    public IReadOnlyList<Guid> AllocationIds => Array.AsReadOnly(_allocationIds);
    public IReadOnlyCollection<LicenseRequirement> Licenses => _licenses.AsReadOnly();
    public IReadOnlyCollection<WorkflowTransition> Transitions => _transitions.AsReadOnly();
    public string SpecificationHash { get; private set; } = string.Empty;
    public string SubmittedBy { get; private set; } = string.Empty;
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeadlineAt { get; private set; }
    public int CompletionTimeoutSeconds { get; private set; }
    public long Version { get; private set; }
    public WorkflowState State { get; private set; }
    public WorkflowState? PausedStep { get; private set; }
    public WorkflowOutcome Outcome { get; private set; }
    public bool CancellationRequested { get; private set; }
    public string? FailureCode { get; private set; }
    public string? LastErrorCode { get; private set; }
    public int StepAttempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public static bool IsExecutable(WorkflowState state) => state is WorkflowState.ReservingResources or WorkflowState.ReservingLicenses
        or WorkflowState.ReleasingLicenses or WorkflowState.ReleasingResources;

    public static ReservationWorkflow Propose(Guid id, ReservationSpecification specification, string actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(specification);
        CheckActor(actor);
        if (id == Guid.Empty || specification.ProjectId == Guid.Empty) throw new DomainException("Workflow and project IDs are required");
        if (specification.ResourceIds is not { Count: > 0 and <= 100 }
            || specification.ResourceIds.Any(r => r == Guid.Empty)
            || specification.ResourceIds.Distinct().Count() != specification.ResourceIds.Count)
            throw new DomainException("A proposal requires 1–100 unique nonempty resource IDs");
        if (specification.Licenses is null || specification.Licenses.Count > 20
            || specification.Licenses.Any(l => l is null || string.IsNullOrWhiteSpace(l.Product) || l.Product.Length > 128
                || l.Product != l.Product.Trim() || l.Seats is < 1 or > 1_000_000)
            || specification.Licenses.Select(l => l.Product).Distinct(StringComparer.Ordinal).Count() != specification.Licenses.Count)
            throw new DomainException("License requirements must name up to 20 distinct products with positive bounded quantities");
        if (specification.CompletionTimeoutSeconds is < 30 or > 3600) throw new DomainException("Completion timeout must be 30–3600 seconds");
        ArgumentNullException.ThrowIfNull(specification.Period);
        var workflow = new ReservationWorkflow
        {
            Id = id, ProjectId = specification.ProjectId, Period = specification.Period,
            _resourceIds = specification.ResourceIds.Order().ToArray(), SubmittedBy = actor,
            CreatedAt = now.ToUniversalTime(), CompletionTimeoutSeconds = specification.CompletionTimeoutSeconds,
            State = WorkflowState.AwaitingApproval
        };
        workflow._licenses.AddRange(specification.Licenses.OrderBy(l => l.Product, StringComparer.Ordinal));
        workflow.SpecificationHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            workflow.ProjectId, ResourceIds = workflow._resourceIds, From = workflow.Period.Start.UtcTicks,
            To = workflow.Period.End.UtcTicks, Licenses = workflow._licenses, workflow.CompletionTimeoutSeconds
        })));
        workflow.Record(null, actor, "proposed", now);
        return workflow;
    }

    public void Approve(string actor, bool requireSeparateApprover, DateTimeOffset now)
    {
        CheckActor(actor);
        RequireState(WorkflowState.AwaitingApproval);
        if (requireSeparateApprover && actor == SubmittedBy) throw new WorkflowApprovalException();
        ApprovedBy = actor;
        DeadlineAt = now.ToUniversalTime().AddSeconds(CompletionTimeoutSeconds);
        Move(WorkflowState.ReservingResources, actor, "approved", now);
    }

    public void RequestCancellation(string actor, DateTimeOffset now)
    {
        CheckActor(actor);
        if (CancellationRequested || State is WorkflowState.Compensated or WorkflowState.Cancelled or WorkflowState.Failed) return;
        CancellationRequested = true;
        if (Outcome is WorkflowOutcome.None or WorkflowOutcome.Successful)
        {
            Outcome = WorkflowOutcome.UserCancelled;
            FailureCode = "user_cancelled";
        }
        if (State == WorkflowState.AwaitingApproval) Move(WorkflowState.Cancelled, actor, "withdrawn", now);
        else if (State == WorkflowState.Succeeded) Move(WorkflowState.ReleasingLicenses, actor, "cancellation_requested", now);
        else Record(State, actor, "cancellation_requested", now);
    }

    public void Resume(string actor, DateTimeOffset now)
    {
        CheckActor(actor);
        RequireState(WorkflowState.NeedsAttention);
        var step = PausedStep ?? throw new DomainException("The paused step is missing");
        PausedStep = null;
        Move(step, actor, "operator_resumed", now);
    }

    public void ExpireIfDue(DateTimeOffset now)
    {
        if (CancellationRequested || State is not (WorkflowState.ReservingResources or WorkflowState.ReservingLicenses)
            || DeadlineAt is null || now < DeadlineAt) return;
        CancellationRequested = true;
        Outcome = WorkflowOutcome.DeadlineExceeded;
        FailureCode = "deadline_exceeded";
        Record(State, "workflow-worker", "deadline_exceeded", now);
    }

    public void CompleteStep(WorkflowState expectedStep, WorkflowStepResult result, DateTimeOffset now, int maxAttempts)
    {
        RequireState(expectedStep);
        if (!IsExecutable(expectedStep)) throw new DomainException("The workflow has no executable step");
        if (!Enum.IsDefined(result.Kind)) throw new ArgumentOutOfRangeException(nameof(result));
        if (maxAttempts is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        ExpireIfDue(now);
        if (result.Kind == StepResultKind.Retry || result.Kind == StepResultKind.Rejected
            && expectedStep is WorkflowState.ReleasingLicenses or WorkflowState.ReleasingResources)
        {
            ScheduleRetry(result.Code ?? "participant_unavailable", now, maxAttempts);
            return;
        }
        if (result.Kind == StepResultKind.Rejected)
        {
            if (Outcome == WorkflowOutcome.None) { Outcome = WorkflowOutcome.Rejected; FailureCode = result.Code ?? "participant_rejected"; }
            Move(expectedStep == WorkflowState.ReservingResources
                ? (CancellationRequested ? WorkflowState.Cancelled : WorkflowState.Failed) : WorkflowState.ReleasingResources,
                "workflow-worker", result.Code ?? "participant_rejected", now);
            return;
        }
        if (expectedStep == WorkflowState.ReservingResources)
        {
            if (result.AllocationIds is null || result.AllocationIds.Count != _resourceIds.Length
                || result.AllocationIds.Any(id => id == Guid.Empty) || result.AllocationIds.Distinct().Count() != result.AllocationIds.Count)
                throw new DomainException("The participant returned an invalid allocation receipt");
            _allocationIds = result.AllocationIds.ToArray();
            Move(CancellationRequested ? WorkflowState.ReleasingResources : WorkflowState.ReservingLicenses,
                "workflow-worker", "resources_reserved", now);
        }
        else if (expectedStep == WorkflowState.ReservingLicenses)
        {
            if (!CancellationRequested) Outcome = WorkflowOutcome.Successful;
            Move(CancellationRequested ? WorkflowState.ReleasingLicenses : WorkflowState.Succeeded,
                "workflow-worker", "licenses_reserved", now);
        }
        else Move(expectedStep == WorkflowState.ReleasingLicenses ? WorkflowState.ReleasingResources : WorkflowState.Compensated,
            "workflow-worker", expectedStep == WorkflowState.ReleasingLicenses ? "licenses_released" : "resources_released", now);
    }

    private void ScheduleRetry(string code, DateTimeOffset now, int maxAttempts)
    {
        LastErrorCode = code.Length <= 100 ? code : code[..100];
        StepAttempts++;
        if (StepAttempts >= maxAttempts)
        {
            PausedStep = State;
            var previous = State;
            State = WorkflowState.NeedsAttention;
            NextAttemptAt = null;
            Record(previous, "workflow-worker", "retry_budget_exhausted", now);
        }
        else
        {
            NextAttemptAt = now.ToUniversalTime().AddSeconds(Math.Min(60, 1 << Math.Min(StepAttempts, 6)));
            Record(State, "workflow-worker", "retry_scheduled", now);
        }
    }

    private void Move(WorkflowState next, string actor, string reason, DateTimeOffset now)
    {
        var previous = State;
        State = next;
        StepAttempts = 0;
        NextAttemptAt = IsExecutable(next) ? now.ToUniversalTime() : null;
        LastErrorCode = null;
        Record(previous, actor, reason, now);
    }

    private void Record(WorkflowState? previous, string actor, string reason, DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now.ToUniversalTime();
        _transitions.Add(new(Version, previous, State, actor, reason, UpdatedAt));
        AddDomainEvent(new ReservationWorkflowChanged(Id, ProjectId, Version, State, Outcome, reason) { OccurredOn = UpdatedAt });
    }

    private void RequireState(WorkflowState expected)
    {
        if (State != expected) throw new DomainException($"Expected workflow state {expected}, found {State}");
    }
    private static void CheckActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200) throw new DomainException("A bounded actor identity is required");
    }
}

public sealed class WorkflowApprovalException() : DomainException("A different actor must approve this proposal");
public sealed class WorkflowVersionException() : DomainException("The workflow version changed; reload its ETag");
public sealed class WorkflowSubmissionConflictException() : DomainException("This workflow ID already belongs to another proposal");

using FusionOps.Domain.Events;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Tenancy;

namespace FusionOps.Domain.Execution;

public enum ExecutionTaskState { Planned, Running, Completed }
public enum ExecutionTaskMutation { Start, Complete }
public sealed class ExecutionTask : TenantOwnedEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];
    private ExecutionTask() { }
    public Guid PlanId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid TaskId { get; private set; }
    public long Version { get; private set; }
    public ExecutionTaskState State { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool CompletedOutsideReservation { get; private set; }
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    public void AddDomainEvent(IDomainEvent e) => _events.Add(e);
    public void ClearDomainEvents() => _events.Clear();
    public static ExecutionTask Planned(ExecutionBaseline baseline, Guid taskId)
    {
        if (!baseline.ScenarioEvidence.Result.Tasks.Any(t => t.TaskId == taskId)) throw new ExecutionPlanRuleException("execution_task_missing");
        var task = new ExecutionTask { PlanId = baseline.Id, ProjectId = baseline.ProjectId, TaskId = taskId, Version = 1 };
        task.BindTenant(baseline.TenantId); return task;
    }
    public static IReadOnlyList<ExecutionTask> PlanAll(ExecutionBaseline baseline)
    {
        baseline.VerifyEvidence();
        return baseline.ScenarioEvidence.Result.Tasks.Select(t =>
        { var task = new ExecutionTask { PlanId = baseline.Id, ProjectId = baseline.ProjectId, TaskId = t.TaskId, Version = 1 }; task.BindTenant(baseline.TenantId); return task; }).ToArray();
    }
    public string? StartRejection(ExecutionActivation activation, IReadOnlyList<ExecutionTask> tasks, DateTimeOffset now)
    {
        if (State != ExecutionTaskState.Planned || activation.State != ActivationState.Active || activation.CancellationRequested) return "execution_task_start_state";
        var scheduled = activation.Baseline.ScenarioEvidence.Result.Tasks.Single(t => t.TaskId == TaskId);
        if (now < scheduled.Period.Start || now >= scheduled.Period.End) return "execution_task_start_window";
        var definition = activation.Baseline.ScenarioEvidence.Input.Definition.Tasks.Single(t => t.Id == TaskId);
        if (definition.Dependencies.Any(id => !tasks.Any(t => t.PlanId == PlanId && t.TaskId == id && t.State == ExecutionTaskState.Completed && t.CompletedAt <= now))) return "execution_task_dependencies_incomplete";
        return null;
    }
    public void Start(DateTimeOffset now)
    {
        if (State != ExecutionTaskState.Planned) throw new ExecutionPlanRuleException("execution_task_start_state");
        StartedAt = Normalize(now); State = ExecutionTaskState.Running; Version++;
    }
    public string? CompletionRejection(DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
    {
        if (State != ExecutionTaskState.Running || StartedAt is null) return "execution_task_completion_state";
        if (effectiveAt < StartedAt || effectiveAt > recordedAt || effectiveAt.UtcTicks % 10 != 0) return "execution_task_completion_time";
        return null;
    }
    public void Complete(DateTimeOffset effectiveAt, DateTimeOffset recordedAt, DateTimeOffset reservedEnd)
    {
        if (CompletionRejection(effectiveAt, recordedAt) is { } error) throw new ExecutionPlanRuleException(error);
        CompletedAt = effectiveAt.ToUniversalTime(); CompletedOutsideReservation = effectiveAt > reservedEnd;
        State = ExecutionTaskState.Completed; Version++;
    }
    private static DateTimeOffset Normalize(DateTimeOffset now) => new(now.UtcTicks / 10 * 10, TimeSpan.Zero);
}

/// <summary>An original task command decision, including a rejected attempt and its observed evidence.</summary>
public sealed class ExecutionTaskFact : TenantOwnedEntity
{
    private ExecutionTaskFact() { }
    public Guid PlanId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid CommandId { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public ExecutionTaskMutation Mutation { get; private set; }
    public bool Accepted { get; private set; }
    public string? RejectionCode { get; private set; }
    public long TaskVersion { get; private set; }
    public ExecutionTaskState State { get; private set; }
    public DateTimeOffset? EffectiveAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public bool OutsideReservation { get; private set; }
    public static ExecutionTaskFact Capture(ExecutionTask task, Guid command, string hash, string actor, ExecutionTaskMutation mutation, string? rejection, DateTimeOffset now)
    {
        if (command == Guid.Empty || hash.Length != 64 || !ProjectDefinitionCodec.Text(actor, 200) || !Enum.IsDefined(mutation)) throw new ExecutionPlanRuleException("execution_task_command_invalid");
        var fact = new ExecutionTaskFact { PlanId = task.PlanId, TaskId = task.TaskId, CommandId = command, RequestHash = hash, Actor = actor,
            Mutation = mutation, Accepted = rejection is null, RejectionCode = rejection, TaskVersion = task.Version, State = task.State,
            EffectiveAt = rejection is null ? mutation == ExecutionTaskMutation.Start ? task.StartedAt : task.CompletedAt : null,
            RecordedAt = new(now.UtcTicks / 10 * 10, TimeSpan.Zero), OutsideReservation = task.CompletedOutsideReservation };
        fact.BindTenant(task.TenantId); return fact;
    }
}

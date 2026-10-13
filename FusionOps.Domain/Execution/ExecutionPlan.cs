using FusionOps.Domain.Events;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Tenancy;

namespace FusionOps.Domain.Execution;

public enum ExecutionPlanState { AwaitingApproval, Approved, Rejected, Withdrawn }
public enum ExecutionPlanMutation { Approve, Reject, Withdraw }

public sealed class ExecutionPlan : TenantOwnedEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];
    private ExecutionPlan() { }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public long Version { get; private set; }
    public ExecutionPlanState State { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? ApprovalExpiresAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ExecutionBaseline Baseline { get; private set; } = null!;
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    public void AddDomainEvent(IDomainEvent e) => _events.Add(e);
    public void ClearDomainEvents() => _events.Clear();

    public static (ExecutionPlan Plan, ExecutionPlanDecision Decision) Propose(ExecutionBaseline baseline)
    {
        baseline.VerifyEvidence();
        var plan = new ExecutionPlan { Id = baseline.Id, ProjectId = baseline.ProjectId, Baseline = baseline };
        plan.BindTenant(baseline.TenantId);
        var decision = plan.Record(baseline.CreatedBy, "proposed", baseline.CreatedAt);
        return (plan, decision);
    }
    public ExecutionPlanDecision Decide(ExecutionPlanMutation mutation, string reviewedDigest, string actor, string? reason, DateTimeOffset now)
    {
        Baseline.VerifyEvidence();
        if (!ProjectDefinitionCodec.Text(actor, 200) || reviewedDigest != Baseline.Digest)
            throw new ExecutionPlanRuleException("execution_reviewed_digest_mismatch");
        if (!Enum.IsDefined(mutation)) throw new ExecutionPlanRuleException("execution_decision_invalid");
        if (mutation != ExecutionPlanMutation.Approve && !ProjectDefinitionCodec.Text(reason ?? string.Empty, 500))
            throw new ExecutionPlanRuleException("execution_decision_reason_required");
        if (mutation == ExecutionPlanMutation.Approve && reason is not null)
            throw new ExecutionPlanRuleException("execution_approval_reason_not_supported");
        if (Normalize(now) < UpdatedAt) throw new ExecutionPlanRuleException("execution_decision_time_invalid");
        if (mutation == ExecutionPlanMutation.Withdraw)
        {
            if (State is not (ExecutionPlanState.AwaitingApproval or ExecutionPlanState.Approved)) throw new ExecutionPlanRuleException("execution_decision_state");
            State = ExecutionPlanState.Withdrawn;
        }
        else
        {
            if (State != ExecutionPlanState.AwaitingApproval) throw new ExecutionPlanRuleException("execution_decision_state");
            if (now >= Baseline.ReviewExpiresAt) throw new ExecutionPlanRuleException("execution_review_expired");
            if (mutation == ExecutionPlanMutation.Approve)
            {
                if (Baseline.Evidence.Policy.RequireSeparateApprover && actor == Baseline.CreatedBy)
                    throw new ExecutionPlanRuleException("execution_separate_approver_required");
                State = ExecutionPlanState.Approved; ApprovedBy = actor; ApprovedAt = Normalize(now);
                ApprovalExpiresAt = ApprovedAt.Value.AddSeconds(Baseline.Evidence.Policy.ApprovalLifetimeSeconds);
            }
            else State = ExecutionPlanState.Rejected;
        }
        return Record(actor, mutation.ToString().ToLowerInvariant(), now, reason);
    }
    public void RequireValidApproval(DateTimeOffset now)
    {
        if (State != ExecutionPlanState.Approved || ApprovalExpiresAt is null || now >= ApprovalExpiresAt)
            throw new ExecutionPlanRuleException("execution_valid_approval_required");
    }
    private ExecutionPlanDecision Record(string actor, string action, DateTimeOffset now, string? reason = null)
    {
        Version = checked(Version + 1); UpdatedAt = Normalize(now);
        _events.Add(new ExecutionPlanChanged(Id, ProjectId, Version, Baseline.Digest, State, actor, action) { OccurredOn = UpdatedAt });
        return ExecutionPlanDecision.Capture(this, actor, action, reason);
    }
    private static DateTimeOffset Normalize(DateTimeOffset value) => new(value.UtcTicks / 10 * 10, TimeSpan.Zero);
}

public sealed class ExecutionPlanDecision : TenantOwnedEntity
{
    private ExecutionPlanDecision() { }
    public Guid PlanId { get; private set; }
    public long Version { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public ExecutionPlanState State { get; private set; }
    public string ReviewedDigest { get; private set; } = string.Empty;
    public string PolicyVersion { get; private set; } = string.Empty;
    public DateTimeOffset At { get; private set; }
    public DateTimeOffset? ApprovalExpiresAt { get; private set; }
    internal static ExecutionPlanDecision Capture(ExecutionPlan plan, string actor, string action, string? reason)
    {
        var decision = new ExecutionPlanDecision { PlanId = plan.Id, Version = plan.Version, Actor = actor, Action = action,
            Reason = reason, State = plan.State, ReviewedDigest = plan.Baseline.Digest, PolicyVersion = plan.Baseline.Evidence.Policy.Version,
            At = plan.UpdatedAt, ApprovalExpiresAt = plan.ApprovalExpiresAt };
        decision.BindTenant(plan.TenantId); return decision;
    }
}

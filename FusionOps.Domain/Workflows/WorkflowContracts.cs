using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Workflows;

public enum WorkflowState
{
    AwaitingApproval, ReservingResources, ReservingLicenses, Succeeded,
    ReleasingLicenses, ReleasingResources, Compensated, Cancelled, Failed, NeedsAttention
}

public enum WorkflowOutcome { None, Successful, Rejected, UserCancelled, DeadlineExceeded }
public enum StepResultKind { Succeeded, Rejected, Retry }

public sealed record LicenseRequirement(string Product, int Seats);
public sealed record ReservationSpecification(Guid ProjectId, IReadOnlyList<Guid> ResourceIds, TimeRange Period,
    IReadOnlyList<LicenseRequirement> Licenses, int CompletionTimeoutSeconds = 300);
public sealed record WorkflowTransition(long Version, WorkflowState? From, WorkflowState To,
    string Actor, string Reason, DateTimeOffset At);
public sealed record WorkflowStepResult(StepResultKind Kind, IReadOnlyList<Guid>? AllocationIds = null, string? Code = null)
{
    public static WorkflowStepResult Success(IReadOnlyList<Guid>? allocations = null) => new(StepResultKind.Succeeded, allocations);
    public static WorkflowStepResult Rejected(string code) => new(StepResultKind.Rejected, Code: code);
    public static WorkflowStepResult Retry(string code) => new(StepResultKind.Retry, Code: code);
}

using FusionOps.Domain.Execution;

namespace FusionOps.Application.Execution;

public sealed record ExecutionAllocation(Guid TaskId, Guid ResourceId, Guid AllocationId);
public sealed record ExecutionParticipantResult(bool Succeeded, string? RejectionCode, IReadOnlyList<ExecutionAllocation> Allocations)
{
    public static ExecutionParticipantResult Success(IReadOnlyList<ExecutionAllocation>? allocations = null) => new(true, null, allocations ?? []);
    public static ExecutionParticipantResult Rejected(string code) => new(false, code, []);
}
public interface ITemporalWorkforceParticipant
{
    Task<ExecutionParticipantResult> ReserveAsync(ExecutionBaseline baseline, CancellationToken ct);
    Task ReleaseAsync(ExecutionBaseline baseline, CancellationToken ct);
}
public interface ITemporalLicenseParticipant
{
    Task<ExecutionParticipantResult> ReserveAsync(ExecutionBaseline baseline, CancellationToken ct);
    Task ReleaseAsync(ExecutionBaseline baseline, CancellationToken ct);
}

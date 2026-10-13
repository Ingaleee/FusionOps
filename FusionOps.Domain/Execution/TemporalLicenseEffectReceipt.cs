using FusionOps.Domain.Tenancy;

namespace FusionOps.Domain.Execution;

public sealed class TemporalLicenseEffectReceipt : TenantOwnedEntity
{
    private TemporalLicenseEffectReceipt() { }
    public Guid PlanId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Step { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public bool Succeeded { get; private set; }
    public string? RejectionCode { get; private set; }
    public DateTimeOffset At { get; private set; }
    public static TemporalLicenseEffectReceipt Create(ExecutionBaseline b, string step, bool succeeded, string? code, DateTimeOffset now)
    {
        var receipt = new TemporalLicenseEffectReceipt { PlanId = b.Id, ProjectId = b.ProjectId, Step = step, Digest = b.Digest,
            Succeeded = succeeded, RejectionCode = code, At = new(now.UtcTicks / 10 * 10, TimeSpan.Zero) };
        receipt.BindTenant(b.TenantId); return receipt;
    }
}

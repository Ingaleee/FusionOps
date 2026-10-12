namespace FusionOps.Infrastructure.Idempotency;

public sealed class CommandReceipt
{
    public string ScopeHash { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
}

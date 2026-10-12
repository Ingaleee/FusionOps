namespace FusionOps.Infrastructure.Persistence.Postgres.Models;

public sealed class ProjectionCheckpoint
{
    public string Name { get; set; } = string.Empty;
    public long CommitPosition { get; set; }
    public long PreparePosition { get; set; }
}

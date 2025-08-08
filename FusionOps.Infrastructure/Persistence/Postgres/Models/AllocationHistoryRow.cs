using System;

namespace FusionOps.Infrastructure.Persistence.Postgres.Models;

public class AllocationHistoryRow
{
    public Guid Id { get; set; }
    public Guid AllocationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ResourceId { get; set; }
    public DateTime FromTs { get; set; }
    public DateTime ToTs { get; set; }
    public DateTime Recorded { get; set; }
    public string? Sku { get; set; }
    public int Qty { get; set; }
}

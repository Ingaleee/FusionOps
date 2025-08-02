using System;

namespace FusionOps.Gateway.Models;

public class Allocation
{
    public Guid AllocationId { get; set; }
    public Guid ResourceId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

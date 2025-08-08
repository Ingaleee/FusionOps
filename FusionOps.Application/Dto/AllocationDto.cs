namespace FusionOps.Application.Dto;

public sealed class AllocationDto
{
    public Guid AllocationId { get; init; }
    public Guid ResourceId { get; init; }
    public DateTime From { get; init; }
    public DateTime To { get; init; }
}

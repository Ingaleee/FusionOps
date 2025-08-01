namespace FusionOps.Application.Dto;

public sealed class AllocationHistoryDto
{
    public Guid AllocationId { get; init; }
    public Guid ResourceId { get; init; }
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public DateTime Recorded { get; init; }
}

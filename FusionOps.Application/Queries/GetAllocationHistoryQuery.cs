using MediatR;
using FusionOps.Application.Dto;

namespace FusionOps.Application.Queries;

public sealed record GetAllocationHistoryQuery(
    Guid ProjectId,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 100) : IRequest<PagedResult<AllocationHistoryDto>>;

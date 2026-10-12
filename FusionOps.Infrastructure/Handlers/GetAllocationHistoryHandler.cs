using FusionOps.Application.Dto;
using FusionOps.Application.Queries;
using FusionOps.Infrastructure.Persistence.Postgres;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FusionOps.Infrastructure.Handlers;

public sealed class GetAllocationHistoryHandler(
    IDbContextFactory<FulfillmentContext> factory, ILogger<GetAllocationHistoryHandler> logger)
    : IRequestHandler<GetAllocationHistoryQuery, PagedResult<AllocationHistoryDto>>
{
    public async Task<PagedResult<AllocationHistoryDto>> Handle(GetAllocationHistoryQuery query, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var filtered = context.AllocationHistory.AsNoTracking().Where(r => r.ProjectId == query.ProjectId
            && (query.From == null || r.Recorded >= query.From) && (query.To == null || r.Recorded <= query.To));
        var total = await filtered.CountAsync(cancellationToken);
        var items = await filtered.OrderByDescending(r => r.Recorded).ThenByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(r => new AllocationHistoryDto
            {
                AllocationId = r.AllocationId, ResourceId = r.ResourceId,
                From = r.FromTs, To = r.ToTs, Recorded = r.Recorded
            }).ToListAsync(cancellationToken);
        logger.LogInformation("Read {Count} of {Total} history records for {ProjectId}", items.Count, total, query.ProjectId);
        return new PagedResult<AllocationHistoryDto>(total, items);
    }
}

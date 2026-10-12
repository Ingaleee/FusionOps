using FusionOps.Application.Dto;
using FusionOps.Application.Queries;
using FusionOps.Infrastructure.Persistence.SqlServer;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FusionOps.Infrastructure.Handlers;

public sealed class GetAllocationsBatchedHandler(
    IDbContextFactory<WorkforceContext> factory, ILogger<GetAllocationsBatchedHandler> logger)
    : IRequestHandler<GetAllocationsBatchedQuery, IDictionary<Guid, IEnumerable<AllocationDto>>>
{
    public async Task<IDictionary<Guid, IEnumerable<AllocationDto>>> Handle(GetAllocationsBatchedQuery request, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Allocations.AsNoTracking()
            .Where(a => request.ProjectIds.Contains(a.ProjectId) && !a.IsCancelled).ToListAsync(cancellationToken);
        logger.LogInformation("Read allocations for {Count} projects", request.ProjectIds.Count);
        return request.ProjectIds.Distinct().ToDictionary(id => id, id => rows.Where(a => a.ProjectId == id)
            .Select(a => new AllocationDto
            {
                AllocationId = a.Id.Value, ResourceId = a.ResourceId,
                From = a.Period.Start.UtcDateTime, To = a.Period.End.UtcDateTime
            }));
    }
}

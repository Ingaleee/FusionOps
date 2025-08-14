using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FusionOps.Application.Dto;
using FusionOps.Application.Queries;
using FusionOps.Infrastructure.Persistence.Postgres;

namespace FusionOps.Infrastructure.Handlers;

public sealed class GetAllocationHistoryHandler
    : IRequestHandler<GetAllocationHistoryQuery, PagedResult<AllocationHistoryDto>>
{
    private readonly IDbContextFactory<FulfillmentContext> _factory;
    private readonly ILogger<GetAllocationHistoryHandler> _logger;

    private static readonly Func<FulfillmentContext, Guid, DateTime?, DateTime?, int, int,
                                 Task<(List<AllocationHistoryDto>, int)>> _compiled =
        EF.CompileAsyncQuery((FulfillmentContext ctx, Guid projectId, DateTime? from, DateTime? to, int skip, int take) =>
            new ValueTuple<List<AllocationHistoryDto>, int>(
                ctx.AllocationHistory
                   .Where(r => r.ProjectId == projectId
                             && (from == null || r.Recorded >= from)
                             && (to == null || r.Recorded <= to))
                   .OrderByDescending(r => r.Recorded)
                   .Skip(skip).Take(take)
                   .Select(r => new AllocationHistoryDto
                   {
                       AllocationId = r.AllocationId,
                       ResourceId   = r.ResourceId,
                       From         = r.FromTs,
                       To           = r.ToTs,
                       Recorded     = r.Recorded
                   })
                   .ToList(),
                ctx.AllocationHistory.Count(r => r.ProjectId == projectId
                                              && (from == null || r.Recorded >= from)
                                              && (to == null || r.Recorded <= to))
            ));

    public GetAllocationHistoryHandler(
        IDbContextFactory<FulfillmentContext> factory,
        ILogger<GetAllocationHistoryHandler> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<PagedResult<AllocationHistoryDto>> Handle(GetAllocationHistoryQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting allocation history for project {ProjectId}, page {Page}, size {PageSize}",
            query.ProjectId, query.Page, query.PageSize);

        await using var ctx = await _factory.CreateDbContextAsync(cancellationToken);
        var (items, total) = await _compiled(ctx, query.ProjectId, query.From, query.To,
                                           (query.Page - 1) * query.PageSize, query.PageSize);

        var result = new PagedResult<AllocationHistoryDto>(total, items);

        _logger.LogInformation("Retrieved {Count} allocation history records for project {ProjectId}",
            items.Count, query.ProjectId);

        return result;
    }
}




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
using FusionOps.Infrastructure.Persistence.SqlServer;

namespace FusionOps.Application.Handlers;

public sealed class GetAllocationsBatchedHandler
    : IRequestHandler<GetAllocationsBatchedQuery, IDictionary<Guid, IEnumerable<AllocationDto>>>
{
    private readonly IDbContextFactory<WorkforceContext> _factory;
    private readonly ILogger<GetAllocationsBatchedHandler> _logger;

    private static readonly Func<WorkforceContext, IReadOnlyList<Guid>, Task<List<(Guid ProjectId, AllocationDto Dto)>>> _compiled =
        EF.CompileAsyncQuery((WorkforceContext ctx, IReadOnlyList<Guid> ids) =>
            ctx.Allocations
               .Where(a => ids.Contains(a.ProjectId))
               .Select(a => new ValueTuple<Guid, AllocationDto>(a.ProjectId, new AllocationDto
               {
                   AllocationId = a.Id,
                   ResourceId   = a.ResourceId,
                   From         = a.Period.Start.UtcDateTime,
                   To           = a.Period.End.UtcDateTime
               }))
               .ToList());

    public GetAllocationsBatchedHandler(IDbContextFactory<WorkforceContext> factory, ILogger<GetAllocationsBatchedHandler> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IDictionary<Guid, IEnumerable<AllocationDto>>> Handle(GetAllocationsBatchedQuery request, CancellationToken cancellationToken)
    {
        await using var ctx = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await _compiled(ctx, request.ProjectIds);

        var dict = rows.GroupBy(x => x.ProjectId)
                       .ToDictionary(g => g.Key, g => (IEnumerable<AllocationDto>)g.Select(t => t.Dto).ToList());

        foreach (var id in request.ProjectIds.Distinct())
            if (!dict.ContainsKey(id)) dict[id] = Array.Empty<AllocationDto>();

        _logger.LogInformation("Batched allocations fetched for {Count} projects", request.ProjectIds.Count);

        return dict;
    }
}

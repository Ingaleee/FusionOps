using System;
using System.Collections.Generic;
using MediatR;
using FusionOps.Application.Dto;

namespace FusionOps.Application.Queries;

public sealed record GetAllocationsBatchedQuery(IReadOnlyList<Guid> ProjectIds)
    : IRequest<IDictionary<Guid, IEnumerable<AllocationDto>>>;

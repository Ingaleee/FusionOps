using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Interfaces;

/// <summary>
/// Repository contract for persisting and querying Allocation aggregates.
/// </summary>
public interface IAllocationRepository
{
    Task AddAsync(Allocation allocation);
    Task<Allocation?> GetAsync(AllocationId id);
    Task<IReadOnlyCollection<Allocation>> FindForResourceAsync(Guid resourceId);
    Task<IReadOnlyCollection<Allocation>> FindFreeAsync(TimeRange period);
}
using MediatR;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared.Ids;

namespace FusionOps.Application.UseCases.AllocateResource;

public class AllocateHandler : IRequestHandler<AllocateCommand, IReadOnlyCollection<Guid>>
{
    private readonly IAllocationRepository _allocationRepo;

    public AllocateHandler(IAllocationRepository allocationRepo)
    {
        _allocationRepo = allocationRepo;
    }

    public async Task<IReadOnlyCollection<Guid>> Handle(AllocateCommand request, CancellationToken cancellationToken)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.ResourceIds == null || request.ResourceIds.Count == 0)
            throw new ArgumentException("ResourceIds cannot be null or empty", nameof(request));

        var period = new TimeRange(request.PeriodFrom, request.PeriodTo);
        var resultIds = new List<Guid>();

        // Consistent lock ordering prevents two multi-resource requests acquiring opposite locks.
        foreach (var resourceId in request.ResourceIds.Order())
        {
            var existing = await _allocationRepo.FindForResourceWithLockAsync(resourceId, cancellationToken);
            var allocation = Allocation.Reserve(resourceId, request.ProjectId, period, existing);
            await _allocationRepo.AddAsync(allocation);
            resultIds.Add(allocation.Id);
        }

        return resultIds;
    }
}

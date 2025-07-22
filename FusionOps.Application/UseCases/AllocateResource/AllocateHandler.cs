using MediatR;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared.Ids;

namespace FusionOps.Application.UseCases.AllocateResource;

public class AllocateHandler : IRequestHandler<AllocateCommand, IReadOnlyCollection<Guid>>
{
    private readonly IAllocationRepository _allocationRepo;
    private readonly IUnitOfWork _uow;

    public AllocateHandler(IAllocationRepository allocationRepo, IUnitOfWork uow)
    {
        _allocationRepo = allocationRepo;
        _uow = uow;
    }

    public async Task<IReadOnlyCollection<Guid>> Handle(AllocateCommand request, CancellationToken cancellationToken)
    {
        var period = new TimeRange(new DateTimeOffset(request.PeriodFrom, TimeSpan.Zero), new DateTimeOffset(request.PeriodTo, TimeSpan.Zero));
        var resultIds = new List<Guid>();

        foreach (var resourceId in request.ResourceIds)
        {
            var existing = await _allocationRepo.FindForResourceAsync(resourceId);
            var allocation = Allocation.Reserve(resourceId, request.ProjectId, period, existing);
            await _allocationRepo.AddAsync(allocation);
            resultIds.Add(allocation.Id);
        }

        await _uow.CommitAsync();
        return resultIds;
    }
} 
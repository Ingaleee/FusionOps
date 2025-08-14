using MediatR;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Shared;

namespace FusionOps.Application.UseCases.License;

public sealed class AllocateLicenseHandler : IRequestHandler<AllocateLicenseCommand, Unit>
{
    private readonly ILicenseRepository _repo;
    public AllocateLicenseHandler(ILicenseRepository repo) => _repo = repo;

    public async Task<Unit> Handle(AllocateLicenseCommand request, CancellationToken cancellationToken)
    {
        var pool = await _repo.FindByProductAsync(request.Product, cancellationToken);
        if (pool is null)
        {
            throw new DomainException($"License pool for '{request.Product}' not found");
        }
        pool.Allocate(request.ProjectId, request.Seats);
        await _repo.UpdateAsync(pool, cancellationToken);
        return Unit.Value;
    }
}

public sealed class ReleaseLicenseHandler : IRequestHandler<ReleaseLicenseCommand, Unit>
{
    private readonly ILicenseRepository _repo;
    public ReleaseLicenseHandler(ILicenseRepository repo) => _repo = repo;

    public async Task<Unit> Handle(ReleaseLicenseCommand request, CancellationToken cancellationToken)
    {
        var pool = await _repo.FindByProductAsync(request.Product, cancellationToken);
        if (pool is null)
        {
            throw new DomainException($"License pool for '{request.Product}' not found");
        }
        pool.Release(request.ProjectId, request.Seats);
        await _repo.UpdateAsync(pool, cancellationToken);
        return Unit.Value;
    }
}



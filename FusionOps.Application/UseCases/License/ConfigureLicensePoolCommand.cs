using FluentValidation;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared;
using MediatR;

namespace FusionOps.Application.UseCases.License;

public sealed record ConfigureLicensePoolCommand(string Product, int TotalSeats) : ICommand<Guid>
{
    public PersistenceBoundary Boundary => PersistenceBoundary.Fulfillment;
}

public sealed class ConfigureLicensePoolValidator : AbstractValidator<ConfigureLicensePoolCommand>
{
    public ConfigureLicensePoolValidator()
    {
        RuleFor(c => c.Product).NotEmpty().MaximumLength(128);
        RuleFor(c => c.TotalSeats).GreaterThanOrEqualTo(0);
    }
}

public sealed class ConfigureLicensePoolHandler(ILicenseRepository repository) : IRequestHandler<ConfigureLicensePoolCommand, Guid>
{
    public async Task<Guid> Handle(ConfigureLicensePoolCommand request, CancellationToken cancellationToken)
    {
        var pool = await repository.FindByProductAsync(request.Product, cancellationToken);
        if (pool is null)
        {
            pool = new LicensePool(request.Product, request.TotalSeats);
            await repository.AddAsync(pool, cancellationToken);
        }
        else if (pool.TotalSeats > request.TotalSeats)
            throw new DomainException("Reducing license capacity is not supported");
        else if (pool.TotalSeats < request.TotalSeats)
            pool.IncreaseCapacity(request.TotalSeats - pool.TotalSeats);
        return pool.Id;
    }
}

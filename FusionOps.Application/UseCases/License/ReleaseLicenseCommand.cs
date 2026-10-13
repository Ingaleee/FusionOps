using FluentValidation;
using MediatR;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Interfaces;

namespace FusionOps.Application.UseCases.License;

public sealed record ReleaseLicenseCommand(string Product, Guid ProjectId, int Seats) : ICommand<Unit>
{
    public PersistenceBoundary Boundary => PersistenceBoundary.Fulfillment;
}

public sealed class ReleaseLicenseValidator : AbstractValidator<ReleaseLicenseCommand>
{
    public ReleaseLicenseValidator()
    {
        RuleFor(x => x.Product).NotEmpty();
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Seats).GreaterThan(0);
    }
}



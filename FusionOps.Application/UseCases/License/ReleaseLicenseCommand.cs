using FluentValidation;
using MediatR;

namespace FusionOps.Application.UseCases.License;

public sealed record ReleaseLicenseCommand(string Product, Guid ProjectId, int Seats) : IRequest;

public sealed class ReleaseLicenseValidator : AbstractValidator<ReleaseLicenseCommand>
{
    public ReleaseLicenseValidator()
    {
        RuleFor(x => x.Product).NotEmpty();
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Seats).GreaterThan(0);
    }
}



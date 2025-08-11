using FluentValidation;
using MediatR;

namespace FusionOps.Application.UseCases.License;

public sealed record AllocateLicenseCommand(string Product, Guid ProjectId, int Seats) : IRequest;

public sealed class AllocateLicenseValidator : AbstractValidator<AllocateLicenseCommand>
{
    public AllocateLicenseValidator()
    {
        RuleFor(x => x.Product).NotEmpty();
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Seats).GreaterThan(0);
    }
}



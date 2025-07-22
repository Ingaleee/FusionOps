using FluentValidation;

namespace FusionOps.Application.UseCases.AllocateResource;

public class AllocateValidator : AbstractValidator<AllocateCommand>
{
    public AllocateValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.ResourceIds).NotEmpty();
        RuleFor(x => x.PeriodFrom).LessThan(x => x.PeriodTo);
    }
} 
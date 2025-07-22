using FluentValidation;
using System.Linq;

namespace FusionOps.Application.UseCases.AllocateResource;

public class AllocateValidator : AbstractValidator<AllocateCommand>
{
    public AllocateValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.ResourceIds)
            .NotEmpty()
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("ResourceIds must be unique");
        RuleFor(x => x.PeriodFrom).LessThan(x => x.PeriodTo);
    }
} 
using FluentValidation;
using System.Linq;

namespace FusionOps.Application.UseCases.AllocateResource;

public class AllocateValidator : AbstractValidator<AllocateCommand>
{
    public AllocateValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.ResourceIds)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(ids => ids.Count <= 100)
            .WithMessage("At most 100 resources can be reserved in one command")
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("ResourceIds must not contain empty identifiers")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("ResourceIds must be unique");
        RuleFor(x => x.PeriodFrom).LessThan(x => x.PeriodTo);
        When(x => x.IdempotencyKey is not null, () => RuleFor(x => x.IdempotencyKey!)
            .Must(key => key.Length is >= 1 and <= 128 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            .WithMessage("Idempotency-Key must contain 1–128 ASCII letters, digits, hyphens, underscores or dots"));
    }
}

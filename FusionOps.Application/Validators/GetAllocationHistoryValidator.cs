using FluentValidation;
using FusionOps.Application.Queries;

namespace FusionOps.Application.Validators;

public sealed class GetAllocationHistoryValidator : AbstractValidator<GetAllocationHistoryQuery>
{
    public GetAllocationHistoryValidator()
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty()
            .WithMessage("ProjectId is required");

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(10, 500)
            .WithMessage("PageSize must be between 10 and 500");

        When(x => x.From.HasValue && x.To.HasValue, () =>
        {
            RuleFor(x => x.From)
                .LessThanOrEqualTo(x => x.To)
                .WithMessage("From date must be less than or equal to To date");
        });
    }
}

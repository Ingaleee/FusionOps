using MediatR;

namespace FusionOps.Application.UseCases.ForecastStock;

public record ForecastQuery(int Days) : IRequest<IReadOnlyCollection<ForecastDto>>; 
namespace FusionOps.Application.UseCases.ForecastStock;

public record ForecastDto(string Sku, int NeedInDays, DateTime? ExpectedShortageDate);
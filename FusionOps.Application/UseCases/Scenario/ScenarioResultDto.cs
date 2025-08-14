using FusionOps.Domain.ValueObjects;

namespace FusionOps.Application.UseCases.Scenario;

public sealed record ScenarioResultDto(
    CostBreakdown TotalCost,
    decimal UtilizationPercentage,
    int BackorderQuantity,
    int LicenseViolations,
    IEnumerable<SuggestedProcurementDto> SuggestedProcurements);

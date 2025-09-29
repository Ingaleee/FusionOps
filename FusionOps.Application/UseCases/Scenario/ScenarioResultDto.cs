using FusionOps.Domain.ValueObjects;

namespace FusionOps.Application.UseCases.Scenario;

public sealed record ScenarioResultDto(
    // Scenario KPIs
    CostBreakdown TotalCost,
    decimal UtilizationPercentage,
    int BackorderQuantity,
    int LicenseViolations,
    IEnumerable<SuggestedProcurementDto> SuggestedProcurements,

    // Baseline KPIs
    CostBreakdown BaselineTotalCost,
    decimal BaselineUtilizationPercentage,
    int BaselineBackorderQuantity,
    int BaselineLicenseViolations,

    // Deltas (scenario - baseline)
    CostBreakdown DeltaTotalCost,
    decimal DeltaUtilizationPercentage,
    int DeltaBackorderQuantity,
    int DeltaLicenseViolations);

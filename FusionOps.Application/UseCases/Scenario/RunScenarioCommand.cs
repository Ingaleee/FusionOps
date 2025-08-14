namespace FusionOps.Application.UseCases.Scenario;

public sealed record RunScenarioCommand(
    Guid ProjectId,
    int DemandDeltaPercent,
    DateTime From,
    DateTime To,
    bool AllowOvertime,
    int MaxLicenseOveragePercent);

namespace FusionOps.Application.UseCases.Scenario;

public sealed record ScenarioPresetDto(
    string Key,
    string Title,
    int DemandDeltaPercent,
    bool AllowOvertime,
    int MaxLicenseOveragePercent);

public sealed record CompareScenariosRequest(
    IEnumerable<RunScenarioCommand> Commands);



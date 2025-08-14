using FusionOps.Application.UseCases.Scenario;

namespace FusionOps.Application.Services.Scenario;

public interface IScenarioRunner
{
    Task<ScenarioResultDto> RunScenario(RunScenarioCommand command, CancellationToken cancellationToken);
}

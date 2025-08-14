using FusionOps.Application.UseCases.Scenario;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Services;
using FusionOps.Application.Services.Costing;

namespace FusionOps.Application.Services.Scenario;

public class ScenarioRunner : IScenarioRunner
{
    private readonly IAllocationRepository _allocationRepository;
    private readonly IStockRepository _stockRepository;
    private readonly ILicenseRepository _licenseRepository;
    private readonly IOptimizerStrategy _optimizerStrategy;
    private readonly ICostEngine _costEngine;
    private readonly IHumanResourceRepository _humanResourceRepository;
    private readonly IEquipmentResourceRepository _equipmentResourceRepository;

    public ScenarioRunner(
        IAllocationRepository allocationRepository,
        IStockRepository stockRepository,
        ILicenseRepository licenseRepository,
        IOptimizerStrategy optimizerStrategy,
        ICostEngine costEngine,
        IHumanResourceRepository humanResourceRepository,
        IEquipmentResourceRepository equipmentResourceRepository)
    {
        _allocationRepository = allocationRepository;
        _stockRepository = stockRepository;
        _licenseRepository = licenseRepository;
        _optimizerStrategy = optimizerStrategy;
        _costEngine = costEngine;
        _humanResourceRepository = humanResourceRepository;
        _equipmentResourceRepository = equipmentResourceRepository;
    }

    public async Task<ScenarioResultDto> RunScenario(RunScenarioCommand command, CancellationToken cancellationToken)
    {
        // 1. Load data from repositories (or use in-memory copies)
        var currentAllocations = (await _allocationRepository.GetAllAsync(cancellationToken)).ToList();
        var currentStockItems = (await _stockRepository.GetAllAsync(cancellationToken)).ToList();
        var currentLicensePools = (await _licenseRepository.GetAllAsync(cancellationToken)).ToList();
        var currentHumanResources = (await _humanResourceRepository.GetAllAsync(cancellationToken)).ToList();
        var currentEquipmentResources = (await _equipmentResourceRepository.GetAllAsync(cancellationToken)).ToList();

        // 2. Create in-memory copies for the "sandbox"
        var scenarioAllocations = new List<Allocation>(currentAllocations);
        var scenarioStockItems = new List<StockItem>(currentStockItems);
        var scenarioLicensePools = new List<LicensePool>(currentLicensePools);
        var scenarioHumanResources = new List<HumanResource>(currentHumanResources);
        var scenarioEquipmentResources = new List<EquipmentResource>(currentEquipmentResources);

        // 3. Apply demand delta to relevant data
        // This is a simplified placeholder. In a real scenario, this would involve complex logic
        // to simulate increased demand, new projects, changes in resource availability, etc.
        // For example, if DemandDeltaPercent is 20, we might proportionally increase some metrics.
        // For now, let's assume it directly impacts the 'required' parameters for a hypothetical optimizer run.
        int adjustedRequiredHumans = (int)(currentHumanResources.Count * (1 + command.DemandDeltaPercent / 100.0));
        int adjustedRequiredEquipment = (int)(currentEquipmentResources.Count * (1 + command.DemandDeltaPercent / 100.0));

        // 4. Run optimizer on the modified data in "sandbox"
        // The current IOptimizerStrategy.AllocateAsync focuses on new allocations, not optimizing existing ones.
        // For the scenario engine, we might need a different optimization strategy or an adapted one.
        // For now, we will simulate an optimization pass and simply use the initial allocations for cost calculation.
        // In a full implementation, this would involve running the optimizer with adjusted demand
        // and potentially new or modified resource pools.
        var optimizedAllocationsResult = await _optimizerStrategy.AllocateAsync(
            scenarioHumanResources,
            scenarioEquipmentResources,
            adjustedRequiredHumans,
            adjustedRequiredEquipment);
        
        var optimizedAllocations = optimizedAllocationsResult.ToList(); // Convert to list if needed

        // 5. Calculate costs using CostEngine
        // This will require iterating over optimizedAllocations and potentially other scenario data.
        // For now, we'll just sum up some conceptual costs.
        var laborCost = _costEngine.ForAllocation(scenarioHumanResources.FirstOrDefault()!, command.From, command.To); // Simplified, assuming not null
        var equipmentCost = _costEngine.ForEquipment(scenarioEquipmentResources.FirstOrDefault()!, command.From, command.To); // Simplified, assuming not null
        var backorderQuantity = 0; // Placeholder for actual calculation after optimization
        var backorderPenalty = _costEngine.ForBackorder("sample_sku", backorderQuantity, 5); // Placeholder
        var licenseViolations = 0; // Placeholder for actual calculation after optimization
        var licensePenalty = _costEngine.ForLicensePenalty("sample_product", licenseViolations, 10); // Placeholder

        var totalCost = _costEngine.Sum(laborCost, equipmentCost, backorderPenalty, licensePenalty);

        // 6. Determine KPI deltas
        // These calculations will be based on the simulated scenario and optimized results.
        var utilizationPercentage = (decimal)optimizedAllocations.Count / (scenarioHumanResources.Count + scenarioEquipmentResources.Count) * 100;
        // BackorderQuantity and LicenseViolations are currently placeholders, will be calculated based on optimization results.
        var suggestedProcurements = Enumerable.Empty<SuggestedProcurementDto>(); // Placeholder

        return new ScenarioResultDto(
            TotalCost: totalCost,
            UtilizationPercentage: utilizationPercentage,
            BackorderQuantity: backorderQuantity,
            LicenseViolations: licenseViolations,
            SuggestedProcurements: suggestedProcurements);
    }
}

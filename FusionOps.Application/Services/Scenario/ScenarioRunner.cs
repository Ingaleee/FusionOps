using FusionOps.Application.UseCases.Scenario;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Services;
using System.Diagnostics.Metrics;
using FusionOps.Domain.ValueObjects;

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

    private readonly Meter _meter;
    private readonly Counter<long> _scenarioRunCounter;
    private readonly Histogram<double> _scenarioRuntimeMs;
    private readonly Counter<double> _scenarioTotalCost;
    private readonly Counter<long> _scenarioBackorderQty;
    private readonly Counter<long> _scenarioLicenseViolations;

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

        _meter = new Meter("FusionOps.ScenarioEngine");
        _scenarioRunCounter = _meter.CreateCounter<long>("scenario_runs_total", "runs", "Total number of scenario runs");
        _scenarioRuntimeMs = _meter.CreateHistogram<double>("scenario_runtime_ms", "ms", "Scenario execution time in milliseconds");
        _scenarioTotalCost = _meter.CreateCounter<double>("scenario_total_cost", "USD", "Total cost of the scenario");
        _scenarioBackorderQty = _meter.CreateCounter<long>("scenario_backorder_qty", "items", "Total backorder quantity in scenario");
        _scenarioLicenseViolations = _meter.CreateCounter<long>("scenario_license_violations", "violations", "Total license violations in scenario");
    }

    public async Task<ScenarioResultDto> RunScenario(RunScenarioCommand command, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _scenarioRunCounter.Add(1);

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
        int adjustedRequiredHumans = (int)(currentHumanResources.Count * (1 + command.DemandDeltaPercent / 100.0));
        int adjustedRequiredEquipment = (int)(currentEquipmentResources.Count * (1 + command.DemandDeltaPercent / 100.0));

        // 4. Run optimizer on the modified data in "sandbox"
        var optimizedAllocationsResult = await _optimizerStrategy.AllocateAsync(
            scenarioHumanResources,
            scenarioEquipmentResources,
            adjustedRequiredHumans,
            adjustedRequiredEquipment);
        
        var optimizedAllocations = optimizedAllocationsResult.ToList();

        // 5. Calculate costs using CostEngine (baseline vs scenario)
        var period = new TimeRange(command.From, command.To);
        // Baseline approximation (no delta)
        var baselineLaborCost = _costEngine.ForAllocation(currentHumanResources.FirstOrDefault()!, period);
        var baselineEquipmentCost = _costEngine.ForEquipment(currentEquipmentResources.FirstOrDefault()!, period);
        var baselineBackorderQty = 0;
        var baselineBackorderPenalty = _costEngine.ForBackorder("sample_sku", baselineBackorderQty, 5);
        var baselineLicenseViolations = 0;
        var baselineLicensePenalty = _costEngine.ForLicensePenalty("sample_product", baselineLicenseViolations, 10);
        var baselineTotalCost = _costEngine.Sum(baselineLaborCost, baselineEquipmentCost, baselineBackorderPenalty, baselineLicensePenalty);

        // Scenario
        var laborCost = _costEngine.ForAllocation(scenarioHumanResources.FirstOrDefault()!, period);
        var equipmentCost = _costEngine.ForEquipment(scenarioEquipmentResources.FirstOrDefault()!, period);
        var backorderQuantity = 0; // Placeholder for actual calculation after optimization
        var backorderPenalty = _costEngine.ForBackorder("sample_sku", backorderQuantity, 5); // Placeholder
        var licenseViolations = 0; // Placeholder for actual calculation after optimization
        var licensePenalty = _costEngine.ForLicensePenalty("sample_product", licenseViolations, 10); // Placeholder
        var totalCost = _costEngine.Sum(laborCost, equipmentCost, backorderPenalty, licensePenalty);

        // 6. Determine KPI and deltas
        var baselineUtilization = (decimal)currentAllocations.Count / (currentHumanResources.Count + currentEquipmentResources.Count == 0 ? 1 : currentHumanResources.Count + currentEquipmentResources.Count) * 100;
        var utilizationPercentage = (decimal)optimizedAllocations.Count / (scenarioHumanResources.Count + scenarioEquipmentResources.Count == 0 ? 1 : scenarioHumanResources.Count + scenarioEquipmentResources.Count) * 100;
        var deltaUtilization = utilizationPercentage - baselineUtilization;

        var deltaBackorderQty = backorderQuantity - baselineBackorderQty;
        var deltaLicenseViolations = licenseViolations - baselineLicenseViolations;
        var deltaTotalCost = new CostBreakdown(new[]
        {
            new CostComponent("DeltaTotal", totalCost.Total - baselineTotalCost.Total)
        });
        var suggestedProcurements = Enumerable.Empty<SuggestedProcurementDto>();

        stopwatch.Stop();
        _scenarioRuntimeMs.Record(stopwatch.Elapsed.TotalMilliseconds);
        _scenarioTotalCost.Add(System.Convert.ToDouble(totalCost.Total.Amount));
        _scenarioBackorderQty.Add(backorderQuantity);
        _scenarioLicenseViolations.Add(licenseViolations);

        return new ScenarioResultDto(
            TotalCost: totalCost,
            UtilizationPercentage: utilizationPercentage,
            BackorderQuantity: backorderQuantity,
            LicenseViolations: licenseViolations,
            SuggestedProcurements: suggestedProcurements,
            BaselineTotalCost: baselineTotalCost,
            BaselineUtilizationPercentage: baselineUtilization,
            BaselineBackorderQuantity: baselineBackorderQty,
            BaselineLicenseViolations: baselineLicenseViolations,
            DeltaTotalCost: deltaTotalCost,
            DeltaUtilizationPercentage: deltaUtilization,
            DeltaBackorderQuantity: deltaBackorderQty,
            DeltaLicenseViolations: deltaLicenseViolations);
    }
}

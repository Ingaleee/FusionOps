using FusionOps.Application.UseCases.Scenario;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Application.Services.Scenario;

/// <summary>A read-only capacity snapshot. Demand percentages apply to resource counts and existing license demand.</summary>
public sealed class ScenarioRunner(
    IAllocationRepository allocations, IStockRepository stock, ILicenseRepository licenses,
    ICostEngine costEngine, IHumanResourceRepository humans, IEquipmentResourceRepository equipment) : IScenarioRunner
{
    public async Task<ScenarioResultDto> RunScenario(RunScenarioCommand command, CancellationToken cancellationToken)
    {
        if (command.ProjectId == Guid.Empty || command.DemandDeltaPercent is < -100 or > 500)
            throw new DomainException("Provide a project and demand delta between -100 and 500 percent");
        if (command.AllowOvertime || command.MaxLicenseOveragePercent != 0)
            throw new DomainException("Overtime and permitted license overage require a policy model and are not supported yet");
        var period = new TimeRange(new DateTimeOffset(DateTime.SpecifyKind(command.From, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(command.To, DateTimeKind.Utc)));

        // Repositories share scoped DbContexts: never run their queries concurrently.
        var calendars = await allocations.GetAllAsync(cancellationToken);
        var inventory = await stock.GetAllAsync(cancellationToken);
        var pools = await licenses.GetAllAsync(cancellationToken);
        var people = await humans.GetAllAsync(cancellationToken);
        var machines = await equipment.GetAllAsync(cancellationToken);
        var factor = 1 + command.DemandDeltaPercent / 100m;
        var humanDemand = (int)Math.Ceiling(people.Count * factor);
        var equipmentDemand = (int)Math.Ceiling(machines.Count * factor);
        if (humanDemand + equipmentDemand > 50) throw new DomainException("Scenario preview supports up to 50 demand slots");

        IReadOnlyList<TimeRange> Busy(Guid resourceId) => calendars.Where(a => !a.IsCancelled && a.ResourceId == resourceId).Select(a => a.Period).ToArray();
        var candidates = people.Select(p => new ResourceCandidate(p.Id.Value, p.FullName, ResourceKind.Human, p.HourRate,
            p.Skills.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.Max(s => s.Level.Value)), Busy(p.Id.Value)))
            .Concat(machines.Select(m => new ResourceCandidate(m.Id.Value, m.Model, ResourceKind.Equipment, m.HourRate,
                new Dictionary<string, int>(), Busy(m.Id.Value), m.Type.Name))).ToArray();
        var slots = Enumerable.Range(0, humanDemand).Select(i => new DemandSlot(Guid.NewGuid(), $"Human {i}", ResourceKind.Human, new Dictionary<string, int>()))
            .Concat(Enumerable.Range(0, equipmentDemand).Select(i => new DemandSlot(Guid.NewGuid(), $"Equipment {i}", ResourceKind.Equipment, new Dictionary<string, int>()))).ToArray();
        var plan = new ResourcePlanner().Plan(new(command.ProjectId, period, Currency.USD, slots, candidates), cancellationToken);
        var parts = new List<CostBreakdown> { new([new CostComponent("AssignedResources", plan.TotalCost)]) };
        var days = Math.Max(1, (int)Math.Ceiling((period.End - period.Start).TotalDays));
        foreach (var item in inventory)
        {
            if (item.UnitCost.Currency != Currency.USD) throw new DomainException("Scenario inventory costs require USD; no FX conversion is configured");
            parts.Add(costEngine.ForInventoryHolding(item, days));
        }
        var violations = 0;
        foreach (var pool in pools)
        {
            var shortage = Math.Max(0, (int)Math.Ceiling(pool.AllocatedSeats * factor) - pool.TotalSeats);
            violations += shortage;
            parts.Add(costEngine.ForLicensePenalty(pool.Product, shortage, days));
        }
        var procurements = inventory.Where(i => i.NeedsReorder())
            .Select(i => new SuggestedProcurementDto(i.Sku, i.ReorderPoint + 1 - i.Quantity)).ToArray();
        return new(costEngine.Sum(parts.ToArray()), candidates.Length == 0 ? 0 : plan.Assignments.Count * 100m / candidates.Length,
            BackorderQuantity: 0, LicenseViolations: violations, SuggestedProcurements: procurements);
    }
}

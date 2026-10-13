using FusionOps.Domain.Projects;
using FusionOps.Domain.Services.Planning;

namespace FusionOps.Domain.Scheduling;

/// <summary>Checks proposed schedules without using search state, pruning rules or cached search costs.</summary>
public sealed class TemporalPlanValidator
{
    public TemporalValidation Validate(TemporalPlanningInput input, IReadOnlyList<ScheduledProjectTask> proposed, bool requireComplete = true)
    {
        TemporalModel.Validate(input);
        var violations = new List<TemporalPlanningIssue>();
        if (proposed is null || proposed.Count > input.Definition.Tasks.Count || proposed.Any(t => t is null))
            return new(false, false, 0, [new("schedule_shape_invalid")]);
        var duplicateIds = proposed.GroupBy(t => t.TaskId).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicateIds.Length > 0) return new(false, false, 0, duplicateIds.Select(id => new TemporalPlanningIssue("task_scheduled_twice", id)).ToArray());
        var definition = input.Definition;
        var tasks = definition.Tasks.ToDictionary(t => t.Id);
        var sites = definition.Sites.ToDictionary(s => s.Id);
        var candidates = input.Candidates.ToDictionary(c => c.Id);
        var schedule = proposed.ToDictionary(t => t.TaskId);
        decimal totalCost = 0;
        var resourceUsage = new Dictionary<Guid, List<ScheduledProjectTask>>();
        var stockUsage = new Dictionary<string, long>(StringComparer.Ordinal);
        var licenseUsage = new Dictionary<string, List<(DateTimeOffset Time, int Delta)>>(StringComparer.Ordinal);
        foreach (var scheduled in proposed)
        {
            if (!tasks.TryGetValue(scheduled.TaskId, out var task)) { violations.Add(new("task_unknown", scheduled.TaskId)); continue; }
            if (scheduled.Period is null) { violations.Add(new("task_period_missing", task.Id)); continue; }
            var period = scheduled.Period;
            if ((period.Start.UtcTicks - definition.Horizon.Start.UtcTicks) % (TimeSpan.TicksPerMinute * input.GridMinutes) != 0)
                violations.Add(new("task_grid_conflict", task.Id));
            if (period.End - period.Start != TimeSpan.FromMinutes(task.DurationMinutes)) violations.Add(new("task_duration_mismatch", task.Id));
            if (period.Start < task.NotBefore || period.End > task.DueAt || period.Start < definition.Horizon.Start || period.End > definition.Horizon.End)
                violations.Add(new("task_window_conflict", task.Id));
            if (!sites[task.SiteId].OpenWindows.Any(w => w.Start <= period.Start && period.End <= w.End))
                violations.Add(new("site_calendar_conflict", task.Id));
            foreach (var dependency in task.Dependencies)
                if (!schedule.TryGetValue(dependency, out var predecessor) || predecessor.Period is null)
                    violations.Add(new("dependency_unscheduled", task.Id, dependency.ToString("D")));
                else if (predecessor.Period.End > period.Start) violations.Add(new("dependency_order_conflict", task.Id, dependency.ToString("D")));
            if (scheduled.Resources is null || scheduled.Resources.Count != task.Resources.Count
                || scheduled.Resources.Any(r => r is null) || scheduled.Resources.Select(r => r.SlotId).Distinct().Count() != scheduled.Resources.Count)
            { violations.Add(new("task_bundle_invalid", task.Id)); continue; }
            decimal taskCost = 0;
            var slots = task.Resources.ToDictionary(s => s.Id);
            foreach (var assignment in scheduled.Resources)
            {
                if (!slots.TryGetValue(assignment.SlotId, out var slot)) { violations.Add(new("slot_unknown", task.Id)); continue; }
                if (!candidates.TryGetValue(assignment.ResourceId, out var candidate))
                { violations.Add(new("resource_unknown", task.Id, assignment.ResourceId.ToString("D"))); continue; }
                if (candidate.Kind != slot.Kind || candidate.HourRate.Currency.Name != definition.Currency
                    || slot.EquipmentType is not null && !string.Equals(slot.EquipmentType, candidate.EquipmentType, StringComparison.OrdinalIgnoreCase)
                    || slot.RequiredSkills.Any(skill => candidate.Skills.Where(s => string.Equals(s.Key, skill.Key, StringComparison.OrdinalIgnoreCase))
                        .Select(s => s.Value).DefaultIfEmpty(-1).Max() < skill.Value))
                    violations.Add(new("resource_requirement_conflict", task.Id, candidate.Id.ToString("D")));
                if (candidate.BusyPeriods.Any(p => p.Overlaps(period))) violations.Add(new("existing_booking_conflict", task.Id, candidate.Id.ToString("D")));
                taskCost += candidate.HourRate.Amount * task.DurationMinutes / 60m;
                if (!resourceUsage.TryGetValue(candidate.Id, out var uses)) resourceUsage[candidate.Id] = uses = [];
                uses.Add(scheduled);
            }
            if (scheduled.Resources.Select(r => r.ResourceId).Distinct().Count() != scheduled.Resources.Count)
                violations.Add(new("resource_reused_in_bundle", task.Id));
            if (taskCost != scheduled.Cost) violations.Add(new("task_cost_mismatch", task.Id));
            totalCost += taskCost;
            foreach (var material in task.Materials)
            {
                stockUsage[material.Sku] = stockUsage.GetValueOrDefault(material.Sku) + material.Quantity;
                if (input.MaterialUnitCosts is { } prices)
                {
                    if (!prices.TryGetValue(material.Sku, out var price)) violations.Add(new("material_price_missing", task.Id, material.Sku));
                    else totalCost += price.Amount * material.Quantity;
                }
            }
            foreach (var license in task.Licenses)
            {
                if (!licenseUsage.TryGetValue(license.Product, out var intervals)) licenseUsage[license.Product] = intervals = [];
                intervals.Add((period.Start, license.Seats)); intervals.Add((period.End, -license.Seats));
            }
        }
        foreach (var (resourceId, uses) in resourceUsage)
        {
            var ordered = uses.OrderBy(t => t.Period.Start).ThenBy(t => t.TaskId).ToArray();
            for (var i = 0; i < ordered.Length; i++) for (var j = i + 1; j < ordered.Length && ordered[j].Period.Start < ordered[i].Period.End; j++)
                violations.Add(new("scheduled_resource_conflict", ordered[j].TaskId, resourceId.ToString("D")));
        }
        foreach (var (sku, quantity) in stockUsage)
            if (quantity > input.Stock.GetValueOrDefault(sku)) violations.Add(new("material_capacity_conflict", Reference: sku));
        foreach (var booking in input.LicenseBookings?.Where(b => b.Period.Overlaps(input.Definition.Horizon)) ?? [])
        {
            if (!licenseUsage.TryGetValue(booking.Product, out var intervals)) licenseUsage[booking.Product] = intervals = [];
            intervals.Add((booking.Period.Start, booking.Seats)); intervals.Add((booking.Period.End, -booking.Seats));
        }
        foreach (var (product, intervals) in licenseUsage)
        {
            long used = 0;
            foreach (var interval in intervals.OrderBy(i => i.Time).ThenBy(i => i.Delta))
            {
                used += interval.Delta; // End events precede start events at a half-open boundary.
                if (used > input.LicenseCapacity.GetValueOrDefault(product)) { violations.Add(new("license_capacity_conflict", Reference: product)); break; }
            }
        }
        foreach (var milestone in definition.Milestones)
            if (milestone.RequiredTasks.Any(id => schedule.TryGetValue(id, out var t) && t.Period is not null && t.Period.End > milestone.DueAt))
                violations.Add(new("milestone_conflict", Reference: milestone.Id.ToString("D")));
        if (totalCost > definition.BudgetAmount) violations.Add(new("project_budget_conflict"));
        var complete = schedule.Count == tasks.Count && tasks.Keys.All(schedule.ContainsKey);
        if (requireComplete && !complete)
            violations.AddRange(tasks.Keys.Where(id => !schedule.ContainsKey(id)).Order().Select(id => new TemporalPlanningIssue("task_unscheduled", id)));
        return new(violations.Count == 0, complete, totalCost, violations);
    }
}

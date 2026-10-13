using System.Diagnostics;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Scheduling;

/// <summary>Enumerates absolute grid starts and distinct resource bundles; validates every returned incumbent independently.</summary>
public sealed class TemporalPlanner
{
    public const string Algorithm = "temporal/bounded-grid-cost-finish-v1";
    public TemporalPlanningResult Plan(TemporalPlanningInput original, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = TemporalInputCodec.Read(TemporalInputCodec.Write(original));
        return new Search(input, cancellationToken).Run();
    }
    private sealed class Search(TemporalPlanningInput input, CancellationToken cancellationToken)
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly Dictionary<Guid, ProjectTaskDefinition> _tasks = input.Definition.Tasks.ToDictionary(t => t.Id);
        private readonly Dictionary<Guid, ProjectSite> _sites = input.Definition.Sites.ToDictionary(s => s.Id);
        private readonly List<ScheduledProjectTask> _selected = [];
        private readonly Dictionary<Guid, ScheduledProjectTask> _byTask = [];
        private readonly Dictionary<Guid, List<TimeRange>> _resourceUses = [];
        private readonly Dictionary<string, List<(TimeRange Period, int Seats)>> _licenseUses = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, (DemandSlot Slot, ResourceCandidate[] Options)[]> _bundles = [];
        private readonly Dictionary<Guid, decimal> _minimumCost = [];
        private ScheduledProjectTask[]? _best;
        private ScheduledProjectTask[] _partial = [];
        private decimal _bestCost = decimal.MaxValue;
        private DateTimeOffset _bestFinish = DateTimeOffset.MaxValue;
        private long _nodes;
        private string? _limit;
        private decimal _materialCost;
        private Guid[] _order = [];
        private decimal[] _remainingCost = [];

        public TemporalPlanningResult Run()
        {
            var issues = Preflight();
            if (issues.Count != 0) return Finish([], true, issues);
            _order = ProjectDefinitionCodec.Analyze(input.Definition).TopologicalOrder.ToArray();
            _remainingCost = new decimal[_order.Length + 1];
            for (var i = _order.Length - 1; i >= 0; i--) _remainingCost[i] = _remainingCost[i + 1] + _minimumCost[_order[i]];
            Visit(0, 0);
            var result = _best ?? _partial;
            var check = new TemporalPlanValidator().Validate(input, result, requireComplete: _best is not null);
            if (!check.IsFeasible) throw new InvalidOperationException("Temporal search produced an invalid incumbent");
            if (_best is null) issues.Add(new(_limit is null ? "no_feasible_schedule_on_grid" : "search_limit_without_complete_plan"));
            if (_limit is not null) issues.Add(new(_limit));
            foreach (var id in _order.Where(id => !result.Any(t => t.TaskId == id))) issues.Add(new("task_unscheduled", id));
            return Finish(result, _limit is null, issues);
        }
        private List<TemporalPlanningIssue> Preflight()
        {
            var issues = new List<TemporalPlanningIssue>();
            foreach (var sku in input.Definition.Tasks.SelectMany(t => t.Materials).GroupBy(m => m.Sku, StringComparer.Ordinal))
            {
                var quantity = sku.Sum(m => (long)m.Quantity);
                if (quantity > input.Stock.GetValueOrDefault(sku.Key)) issues.Add(new("material_capacity_conflict", Reference: sku.Key));
                if (input.MaterialUnitCosts is { } prices)
                {
                    if (!prices.TryGetValue(sku.Key, out var price)) issues.Add(new("material_price_missing", Reference: sku.Key));
                    else _materialCost += price.Amount * quantity;
                }
            }
            foreach (var task in input.Definition.Tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slots = task.Resources.Select(s => (Slot: s, Options: input.Candidates.Where(c => Eligible(s, c))
                    .OrderBy(c => c.HourRate.Amount).ThenBy(c => c.Id).ToArray())).OrderBy(s => s.Options.Length).ThenBy(s => s.Slot.Id).ToArray();
                _bundles[task.Id] = slots;
                foreach (var slot in slots.Where(s => s.Options.Length == 0)) issues.Add(new("resource_requirement_shortage", task.Id, slot.Slot.Id.ToString("D")));
                _minimumCost[task.Id] = slots.Sum(s => s.Options.Length == 0 ? 0 : s.Options[0].HourRate.Amount * task.DurationMinutes / 60m);
                foreach (var license in task.Licenses)
                    if (license.Seats > input.LicenseCapacity.GetValueOrDefault(license.Product)) issues.Add(new("license_capacity_conflict", task.Id, license.Product));
            }
            if (_materialCost + _minimumCost.Values.Sum() > input.Definition.BudgetAmount) issues.Add(new("project_budget_conflict"));
            var bounds = ProjectDefinitionCodec.Analyze(input.Definition);
            issues.AddRange(bounds.WindowConflicts.Select(id => new TemporalPlanningIssue("dependency_window_conflict", id)));
            issues.AddRange(bounds.MilestoneConflicts.Select(id => new TemporalPlanningIssue("milestone_conflict", Reference: id.ToString("D"))));
            return issues;
        }
        private bool Eligible(DemandSlot slot, ResourceCandidate c) => c.Kind == slot.Kind && c.HourRate.Currency.Name == input.Definition.Currency
            && (slot.EquipmentType is null || string.Equals(slot.EquipmentType, c.EquipmentType, StringComparison.OrdinalIgnoreCase))
            && slot.RequiredSkills.All(s => c.Skills.TryGetValue(s.Key.ToLowerInvariant(), out var level) && level >= s.Value);
        private bool Tick()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_limit is not null) return false;
            if (_nodes >= input.MaxSearchNodes) { _limit = "node_budget_exhausted"; return false; }
            _nodes++;
            if ((_nodes & 63) == 1 && _watch.ElapsedMilliseconds >= input.MaxElapsedMilliseconds) { _limit = "time_budget_exhausted"; return false; }
            return true;
        }
        private void Visit(int depth, decimal cost)
        {
            if (!Tick()) return;
            if (_selected.Count > _partial.Length) _partial = _selected.ToArray();
            if (depth == _order.Length)
            {
                var finish = _selected.Max(t => t.Period.End);
                if (cost + _materialCost < _bestCost || cost + _materialCost == _bestCost && finish < _bestFinish)
                { _best = _selected.ToArray(); _bestCost = cost + _materialCost; _bestFinish = finish; }
                return;
            }
            if (cost + _materialCost + _remainingCost[depth] > Math.Min(input.Definition.BudgetAmount, _bestCost)) return;
            var task = _tasks[_order[depth]];
            var earliest = task.Dependencies.Select(d => _byTask[d].Period.End).Append(task.NotBefore).Max();
            var deadline = input.Definition.Milestones.Where(m => m.RequiredTasks.Contains(task.Id)).Select(m => m.DueAt).Append(task.DueAt).Min();
            var duration = TimeSpan.FromMinutes(task.DurationMinutes);
            var gridTicks = TimeSpan.TicksPerMinute * input.GridMinutes;
            foreach (var window in _sites[task.SiteId].OpenWindows.OrderBy(w => w.Start))
            {
                var lower = earliest > window.Start ? earliest : window.Start;
                var upper = deadline < window.End ? deadline : window.End;
                if (upper - lower < duration) continue;
                var offset = lower.UtcTicks - input.Definition.Horizon.Start.UtcTicks;
                var first = input.Definition.Horizon.Start.UtcTicks + ((offset + gridTicks - 1) / gridTicks) * gridTicks;
                var last = upper.UtcTicks - duration.Ticks;
                for (var start = first; start <= last; start += gridTicks)
                {
                    if (!Tick()) return;
                    var period = new TimeRange(new(start, TimeSpan.Zero), new(start + duration.Ticks, TimeSpan.Zero));
                    if (!LicensesFit(task, period)) continue;
                    var chosen = new List<ScheduledResource>();
                    Assign(0, 0);
                    if (_limit is not null) return;
                    void Assign(int index, decimal taskCost)
                    {
                        if (!Tick()) return;
                        if (cost + taskCost + _materialCost > Math.Min(input.Definition.BudgetAmount, _bestCost)) return;
                        if (index == _bundles[task.Id].Length)
                        {
                            var scheduled = new ScheduledProjectTask(task.Id, period, chosen.OrderBy(r => r.SlotId).ToArray(), taskCost);
                            Push(task, scheduled); Visit(depth + 1, cost + taskCost); Pop(task, scheduled); return;
                        }
                        var slot = _bundles[task.Id][index];
                        foreach (var candidate in slot.Options)
                        {
                            if (!Tick()) return;
                            if (chosen.Any(r => r.ResourceId == candidate.Id) || candidate.BusyPeriods.Any(p => p.Overlaps(period))
                                || _resourceUses.TryGetValue(candidate.Id, out var periods) && periods.Any(p => p.Overlaps(period))) continue;
                            chosen.Add(new(slot.Slot.Id, candidate.Id));
                            Assign(index + 1, taskCost + candidate.HourRate.Amount * task.DurationMinutes / 60m);
                            chosen.RemoveAt(chosen.Count - 1);
                        }
                    }
                }
            }
        }
        private bool LicensesFit(ProjectTaskDefinition task, TimeRange period)
        {
            foreach (var requirement in task.Licenses)
            {
                var changes = new List<(DateTimeOffset Time, int Delta)> { (period.Start, requirement.Seats), (period.End, -requirement.Seats) };
                if (_licenseUses.TryGetValue(requirement.Product, out var uses))
                    foreach (var use in uses.Where(u => u.Period.Overlaps(period))) { changes.Add((use.Period.Start, use.Seats)); changes.Add((use.Period.End, -use.Seats)); }
                long seats = 0;
                foreach (var change in changes.OrderBy(c => c.Time).ThenBy(c => c.Delta))
                {
                    seats += change.Delta;
                    if (seats > input.LicenseCapacity.GetValueOrDefault(requirement.Product)) return false;
                }
            }
            return true;
        }
        private void Push(ProjectTaskDefinition task, ScheduledProjectTask scheduled)
        {
            _selected.Add(scheduled); _byTask.Add(task.Id, scheduled);
            foreach (var resource in scheduled.Resources)
            { if (!_resourceUses.TryGetValue(resource.ResourceId, out var uses)) _resourceUses[resource.ResourceId] = uses = []; uses.Add(scheduled.Period); }
            foreach (var license in task.Licenses)
            { if (!_licenseUses.TryGetValue(license.Product, out var uses)) _licenseUses[license.Product] = uses = []; uses.Add((scheduled.Period, license.Seats)); }
        }
        private void Pop(ProjectTaskDefinition task, ScheduledProjectTask scheduled)
        {
            _selected.RemoveAt(_selected.Count - 1); _byTask.Remove(task.Id);
            foreach (var resource in scheduled.Resources) { var uses = _resourceUses[resource.ResourceId]; uses.RemoveAt(uses.Count - 1); }
            foreach (var license in task.Licenses) { var uses = _licenseUses[license.Product]; uses.RemoveAt(uses.Count - 1); }
        }
        private TemporalPlanningResult Finish(ScheduledProjectTask[] tasks, bool completeSearch, IReadOnlyList<TemporalPlanningIssue> issues)
        {
            var validation = new TemporalPlanValidator().Validate(input, tasks, requireComplete: false);
            if (!validation.IsFeasible) throw new InvalidOperationException("Temporal result failed independent feasibility validation");
            return new(input.ProjectId, Algorithm, validation.IsComplete, completeSearch, _nodes, validation.ComputedCost,
                tasks.Length == 0 ? null : tasks.Max(t => t.Period.End), tasks.OrderBy(t => t.TaskId).ToArray(), issues,
                _limit ?? "exhausted", _watch.ElapsedMilliseconds, _materialCost + _minimumCost.Values.Sum());
        }
    }
}

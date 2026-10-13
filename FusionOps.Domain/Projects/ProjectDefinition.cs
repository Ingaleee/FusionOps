using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Workflows;

namespace FusionOps.Domain.Projects;

public sealed record ProjectSite(Guid Id, string Name, IReadOnlyList<TimeRange> OpenWindows);
public sealed record MaterialRequirement(string Sku, int Quantity);
public sealed record ProjectTaskDefinition(Guid Id, string Name, Guid SiteId, int DurationMinutes,
    DateTimeOffset NotBefore, DateTimeOffset DueAt, IReadOnlyList<Guid> Dependencies,
    IReadOnlyList<DemandSlot> Resources, IReadOnlyList<MaterialRequirement> Materials,
    IReadOnlyList<LicenseRequirement> Licenses);
public sealed record ProjectMilestone(Guid Id, string Name, DateTimeOffset DueAt, IReadOnlyList<Guid> RequiredTasks);
public sealed record ProjectDefinition(string Name, TimeRange Horizon, decimal BudgetAmount, string Currency,
    IReadOnlyList<ProjectSite> Sites, IReadOnlyList<ProjectTaskDefinition> Tasks, IReadOnlyList<ProjectMilestone> Milestones);
public sealed record TaskTimeBound(Guid TaskId, DateTimeOffset EarliestStart, DateTimeOffset EarliestFinish);
public sealed record ProjectGraphAnalysis(IReadOnlyList<Guid> TopologicalOrder, IReadOnlyList<TaskTimeBound> TimeBounds,
    IReadOnlyList<Guid> WindowConflicts, IReadOnlyList<Guid> MilestoneConflicts);

/// <summary>Bounded validated graph; stored text is canonical evidence, never a caller-owned mutable graph.</summary>
public static class ProjectDefinitionCodec
{
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    public static ProjectDefinition Read(string text) => JsonSerializer.Deserialize<ProjectDefinition>(text, Json)
        ?? throw new ProjectRuleException("project_evidence_missing");
    public static string Canonicalize(ProjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!Text(definition.Name, 200) || definition.Horizon is null
            || definition.Horizon.End - definition.Horizon.Start > TimeSpan.FromDays(366)
            || !Microseconds(definition.Horizon.Start) || !Microseconds(definition.Horizon.End)
            || definition.BudgetAmount is < 0 or > 1_000_000_000_000m || decimal.Round(definition.BudgetAmount, 6) != definition.BudgetAmount
            || !FusionOps.Domain.Enumerations.Currency.All.Any(c => c.Name == definition.Currency))
            throw new ProjectRuleException("project_commitment_invalid");
        if (definition.Sites is not { Count: > 0 and <= 32 } || definition.Tasks is not { Count: > 0 and <= 200 }
            || definition.Milestones is null || definition.Milestones.Count > 64)
            throw new ProjectRuleException("project_graph_limits");
        var siteIds = new HashSet<Guid>();
        foreach (var site in definition.Sites)
        {
            if (site is null || site.Id == Guid.Empty || !siteIds.Add(site.Id) || !Text(site.Name, 200)
                || site.OpenWindows is not { Count: > 0 and <= 512 }) throw new ProjectRuleException("project_site_invalid");
            TimeRange? previous = null;
            foreach (var window in site.OpenWindows.OrderBy(w => w?.Start))
            {
                if (window is null || window.Start < definition.Horizon.Start || window.End > definition.Horizon.End
                    || !Microseconds(window.Start) || !Microseconds(window.End) || previous?.Overlaps(window) == true)
                    throw new ProjectRuleException("project_calendar_invalid");
                previous = window;
            }
        }
        if (definition.Tasks.Any(t => t is null || t.Id == Guid.Empty) || definition.Tasks.Select(t => t.Id).Distinct().Count() != definition.Tasks.Count)
            throw new ProjectRuleException("project_task_identity_invalid");
        var taskIds = definition.Tasks.Select(t => t.Id).ToHashSet();
        var slotIds = new HashSet<Guid>();
        foreach (var task in definition.Tasks)
        {
            if (!Text(task.Name, 200) || !siteIds.Contains(task.SiteId) || task.DurationMinutes is < 1 or > 10080
                || task.NotBefore < definition.Horizon.Start || task.DueAt > definition.Horizon.End || task.DueAt <= task.NotBefore
                || !Microseconds(task.NotBefore) || !Microseconds(task.DueAt)
                || task.Dependencies is null || task.Dependencies.Count > 199 || task.Dependencies.Distinct().Count() != task.Dependencies.Count
                || task.Dependencies.Any(d => d == task.Id || !taskIds.Contains(d)))
                throw new ProjectRuleException("project_task_invalid");
            if (task.Resources is null || task.Resources.Count > 20 || task.Materials is null || task.Materials.Count > 20
                || task.Licenses is null || task.Licenses.Count > 20) throw new ProjectRuleException("project_demand_limits");
            foreach (var slot in task.Resources)
            {
                if (slot is null || slot.Id == Guid.Empty || !slotIds.Add(slot.Id) || !Text(slot.Name, 200) || !Enum.IsDefined(slot.Kind)
                    || slot.RequiredSkills is null || slot.RequiredSkills.Count > 20
                    || slot.RequiredSkills.Any(s => !Text(s.Key, 64) || s.Value is < 0 or > 5)
                    || slot.RequiredSkills.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != slot.RequiredSkills.Count
                    || slot.Kind == ResourceKind.Human && slot.EquipmentType is not null
                    || slot.Kind == ResourceKind.Equipment && !Text(slot.EquipmentType, 64))
                    throw new ProjectRuleException("project_resource_demand_invalid");
            }
            if (task.Materials.Any(m => m is null || !Text(m.Sku, 128) || m.Quantity is < 1 or > 1_000_000)
                || task.Materials.Select(m => m.Sku).Distinct(StringComparer.Ordinal).Count() != task.Materials.Count
                || task.Licenses.Any(l => l is null || !Text(l.Product, 128) || l.Seats is < 1 or > 1_000_000)
                || task.Licenses.Select(l => l.Product).Distinct(StringComparer.Ordinal).Count() != task.Licenses.Count)
                throw new ProjectRuleException("project_consumable_demand_invalid");
        }
        if (definition.Milestones.Any(m => m is null || m.Id == Guid.Empty || !Text(m.Name, 200)
            || m.DueAt <= definition.Horizon.Start || m.DueAt > definition.Horizon.End || !Microseconds(m.DueAt)
            || m.RequiredTasks is not { Count: > 0 and <= 200 } || m.RequiredTasks.Any(t => !taskIds.Contains(t))
            || m.RequiredTasks.Distinct().Count() != m.RequiredTasks.Count)
            || definition.Milestones.Select(m => m.Id).Distinct().Count() != definition.Milestones.Count)
            throw new ProjectRuleException("project_milestone_invalid");
        Analyze(definition); // A cycle is an invalid specification; infeasible time bounds remain explainable input.
        var normalized = definition with
        {
            BudgetAmount = decimal.Parse(definition.BudgetAmount.ToString("G29", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture),
            Sites = definition.Sites.OrderBy(s => s.Id).Select(s => s with { OpenWindows = s.OpenWindows.OrderBy(w => w.Start).ToArray() }).ToArray(),
            Tasks = definition.Tasks.OrderBy(t => t.Id).Select(t => t with
            {
                NotBefore = t.NotBefore.ToUniversalTime(), DueAt = t.DueAt.ToUniversalTime(), Dependencies = t.Dependencies.Order().ToArray(),
                Resources = t.Resources.OrderBy(s => s.Id).Select(s => s with
                {
                    RequiredSkills = s.RequiredSkills.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value)
                }).ToArray(),
                Materials = t.Materials.OrderBy(m => m.Sku, StringComparer.Ordinal).ToArray(),
                Licenses = t.Licenses.OrderBy(l => l.Product, StringComparer.Ordinal).ToArray()
            }).ToArray(),
            Milestones = definition.Milestones.OrderBy(m => m.Id).Select(m => m with
            { DueAt = m.DueAt.ToUniversalTime(), RequiredTasks = m.RequiredTasks.Order().ToArray() }).ToArray()
        };
        var text = JsonSerializer.Serialize(normalized, Json);
        if (text.Length > 1_000_000) throw new ProjectRuleException("project_evidence_limits");
        return text;
    }
    public static ProjectGraphAnalysis Analyze(ProjectDefinition definition)
    {
        var tasks = definition.Tasks.ToDictionary(t => t.Id);
        var indegrees = tasks.ToDictionary(p => p.Key, p => p.Value.Dependencies.Count);
        var children = tasks.Keys.ToDictionary(id => id, _ => new List<Guid>());
        foreach (var task in tasks.Values) foreach (var dependency in task.Dependencies) children[dependency].Add(task.Id);
        var ready = new SortedSet<Guid>(indegrees.Where(p => p.Value == 0).Select(p => p.Key));
        var order = new List<Guid>();
        var bounds = new Dictionary<Guid, TaskTimeBound>();
        while (ready.Count > 0)
        {
            var id = ready.Min; ready.Remove(id); order.Add(id);
            var task = tasks[id];
            var start = task.Dependencies.Select(d => bounds[d].EarliestFinish).Append(task.NotBefore.ToUniversalTime()).Max();
            DateTimeOffset finish;
            try { finish = start.AddMinutes(task.DurationMinutes); }
            catch (ArgumentOutOfRangeException) { throw new ProjectRuleException("project_time_bounds_overflow"); }
            bounds.Add(id, new(id, start, finish));
            foreach (var child in children[id]) if (--indegrees[child] == 0) ready.Add(child);
        }
        if (order.Count != tasks.Count) throw new ProjectRuleException("project_dependency_cycle");
        return new(order, order.Select(id => bounds[id]).ToArray(),
            order.Where(id => bounds[id].EarliestFinish > tasks[id].DueAt).ToArray(),
            definition.Milestones.Where(m => m.RequiredTasks.Any(id => bounds[id].EarliestFinish > m.DueAt)).Select(m => m.Id).Order().ToArray());
    }
    public static string Digest(Guid tenant, Guid project, long version, string text) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Schema = SchemaVersion, Tenant = tenant, Project = project, Version = version, Definition = text }, Json)));
    public static bool Text(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max && text == text.Trim();
    private static bool Microseconds(DateTimeOffset time) => time.UtcTicks % 10 == 0;
}

public sealed class ProjectRuleException(string code) : DomainException(code) { public string Code { get; } = code; }
public sealed class ProjectVersionException() : DomainException("The project changed; reload its ETag");
public sealed class ProjectAccessException() : DomainException("The project is unavailable to this actor");
public sealed class ProjectSubmissionConflictException() : DomainException("The project ID has another original submission");

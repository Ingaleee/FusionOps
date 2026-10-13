using FusionOps.Domain.Projects;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Tests;

public sealed class ProjectDefinitionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-20T08:00:00Z");
    private static readonly Guid Site = Guid.Parse("01000000-0000-0000-0000-000000000001");
    private static readonly Guid A = Guid.Parse("02000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("02000000-0000-0000-0000-000000000002");
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static ProjectTaskDefinition Task(Guid id, params Guid[] dependencies) => new(id, $"Task {id:N}", Site, 60, Start, Start.AddDays(14), dependencies, [], [], []);
    private static ProjectDefinition Definition() => new("Installation", new(Start, Start.AddDays(14)), 10000, "USD",
        [new(Site, "Assembly site", [new(Start, Start.AddDays(14))])], [Task(A), Task(B, A)], []);
    [Test]
    public void CanonicalEvidenceNormalizesOrderingTimezonesSkillNamesAndDecimalScale()
    {
        var d = Definition();
        d = d with { Tasks = [d.Tasks[0] with { Resources = [new(Guid.NewGuid(), "Lead", ResourceKind.Human, new Dictionary<string, int> { ["CSharp"] = 4 })] }, d.Tasks[1]] };
        var shuffled = d with { BudgetAmount = 10000.000000m, Sites = d.Sites.Reverse().ToArray(), Tasks = d.Tasks.Reverse().Select(t => t with
        { NotBefore = t.NotBefore.ToOffset(TimeSpan.FromHours(3)), DueAt = t.DueAt.ToOffset(TimeSpan.FromHours(3)),
            Resources = t.Resources.Select(s => s with { RequiredSkills = s.RequiredSkills.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value) }).ToArray() }).ToArray() };
        Assert.That(ProjectDefinitionCodec.Canonicalize(shuffled), Is.EqualTo(ProjectDefinitionCodec.Canonicalize(d)));
    }
    [Test]
    public void SnapshotReadsAndCallerMutationCannotChangePersistedEvidence()
    {
        var definition = Definition();
        var tasks = definition.Tasks.ToArray(); definition = definition with { Tasks = tasks };
        var project = OperationalProject.Create(Guid.NewGuid(), Tenant, definition, "owner", Start);
        var digest = project.DefinitionDigest;
        tasks[0] = tasks[0] with { Name = "Caller changed" };
        ((IList<ProjectTaskDefinition>)project.Definition.Tasks)[0] = tasks[0];
        Assert.That(project.Definition.Tasks[0].Name, Is.Not.EqualTo("Caller changed"));
        Assert.That(project.DefinitionDigest, Is.EqualTo(digest)); project.VerifyEvidence();
    }
    [Test]
    public void CyclesAndUnknownDependenciesDoNotMutateTheAggregate()
    {
        var d = Definition(); var project = OperationalProject.Create(Guid.NewGuid(), Tenant, d, "owner", Start); project.ClearDomainEvents();
        var cycle = d with { Tasks = [d.Tasks[0] with { Dependencies = [B] }, d.Tasks[1]] };
        Assert.That(Assert.Throws<ProjectRuleException>(() => project.ReplaceDefinition(cycle, "owner", Start))!.Code, Is.EqualTo("project_dependency_cycle"));
        Assert.That(Assert.Throws<ProjectRuleException>(() => ProjectDefinitionCodec.Canonicalize(d with { Tasks = [d.Tasks[0] with { Dependencies = [Guid.NewGuid()] }, d.Tasks[1]] }))!.Code, Is.EqualTo("project_task_invalid"));
        Assert.That(project.Version, Is.EqualTo(1)); Assert.That(project.DefinitionVersion, Is.EqualTo(1)); Assert.That(project.DomainEvents, Is.Empty);
    }
    [Test]
    public void CalendarWindowsAreHalfOpenBoundedAndNonoverlapping()
    {
        var d = Definition();
        Assert.DoesNotThrow(() => ProjectDefinitionCodec.Canonicalize(d with { Sites = [new(Site, "Site", [new(Start, Start.AddHours(1)), new(Start.AddHours(1), Start.AddHours(2))])] }));
        Assert.That(Assert.Throws<ProjectRuleException>(() => ProjectDefinitionCodec.Canonicalize(d with { Sites = [new(Site, "Site", [new(Start, Start.AddHours(2)), new(Start.AddHours(1), Start.AddHours(3))])] }))!.Code, Is.EqualTo("project_calendar_invalid"));
    }
    [Test]
    public void InfeasibleTimeBoundsRemainVisibleInsteadOfBecomingAnInventedPlan()
    {
        var d = Definition(); var milestone = Guid.NewGuid();
        d = d with { Tasks = [d.Tasks[0], d.Tasks[1] with { DueAt = Start.AddMinutes(90) }], Milestones = [new(milestone, "Deadline", Start.AddMinutes(90), [B])] };
        ProjectDefinitionCodec.Canonicalize(d);
        var analysis = ProjectDefinitionCodec.Analyze(d);
        Assert.That(analysis.WindowConflicts, Is.EqualTo(new[] { B })); Assert.That(analysis.MilestoneConflicts, Is.EqualTo(new[] { milestone }));
        Assert.That(analysis.TimeBounds.Single(t => t.TaskId == B).EarliestFinish, Is.EqualTo(Start.AddHours(2)));
    }
    [Test]
    public void SeededDagOrderAndTimeBoundsAgreeWithIndependentPathEnumeration()
    {
        var random = new Random(7113);
        for (var sample = 0; sample < 120; sample++)
        {
            var count = random.Next(2, 11);
            var ids = Enumerable.Range(0, count).Select(i => new Guid(i + 1, 0, 0, new byte[8])).ToArray();
            var tasks = ids.Select((id, i) => Task(id, ids.Take(i).Where(_ => random.Next(3) == 0).ToArray()) with { DurationMinutes = random.Next(1, 91) }).ToArray();
            var d = Definition() with { Tasks = tasks.Reverse().ToArray() };
            var graph = ProjectDefinitionCodec.Analyze(ProjectDefinitionCodec.Read(ProjectDefinitionCodec.Canonicalize(d)));
            int Longest(Guid id) => tasks.Single(t => t.Id == id).DurationMinutes + tasks.Single(t => t.Id == id).Dependencies.Select(Longest).DefaultIfEmpty(0).Max();
            foreach (var task in tasks)
            {
                var position = graph.TopologicalOrder.ToList().IndexOf(task.Id);
                Assert.That(task.Dependencies.All(dep => graph.TopologicalOrder.ToList().IndexOf(dep) < position), Is.True);
                Assert.That(graph.TimeBounds.Single(t => t.TaskId == task.Id).EarliestFinish, Is.EqualTo(Start.AddMinutes(Longest(task.Id))));
            }
            Assert.That(ProjectDefinitionCodec.Canonicalize(d), Is.EqualTo(ProjectDefinitionCodec.Canonicalize(d with { Tasks = tasks })));
        }
    }
    [TestCase(ProjectRole.Viewer, ProjectPermission.Read, true)]
    [TestCase(ProjectRole.Viewer, ProjectPermission.Plan, false)]
    [TestCase(ProjectRole.Planner, ProjectPermission.EditDefinition, true)]
    [TestCase(ProjectRole.Planner, ProjectPermission.Approve, false)]
    [TestCase(ProjectRole.Approver, ProjectPermission.Approve, true)]
    [TestCase(ProjectRole.Approver, ProjectPermission.EditDefinition, false)]
    [TestCase(ProjectRole.Operator, ProjectPermission.Execute, true)]
    [TestCase(ProjectRole.Operator, ProjectPermission.ManageMembers, false)]
    public void ProjectRolesHaveDistinctAuthority(ProjectRole role, ProjectPermission permission, bool expected)
    {
        var project = OperationalProject.Create(Guid.NewGuid(), Tenant, Definition(), "owner", Start);
        project.SetMember("member", role, "owner", Start); Assert.That(project.Can("member", permission), Is.EqualTo(expected));
        Assert.That(project.Can("absent", permission), Is.False); Assert.That(project.Can("owner", (ProjectPermission)99), Is.False);
    }
    [Test]
    public void OwnershipTransferKeepsExactlyOneOwnerAndSeparatesAclFromDefinitionVersions()
    {
        var project = OperationalProject.Create(Guid.NewGuid(), Tenant, Definition(), "owner", Start);
        project.SetMember("next", ProjectRole.Planner, "owner", Start); project.TransferOwnership("next", "owner", Start);
        Assert.That(project.Members.Count(m => m.Role == ProjectRole.Owner), Is.EqualTo(1));
        Assert.That(project.Can("owner", ProjectPermission.ManageMembers), Is.False);
        Assert.That(project.Can("next", ProjectPermission.ManageMembers), Is.True);
        Assert.That(project.DefinitionVersion, Is.EqualTo(1)); Assert.That(project.Version, Is.EqualTo(3));
        Assert.Throws<ProjectRuleException>(() => project.RemoveMember("next", "next", Start));
        Assert.Throws<ProjectRuleException>(() => project.SetMember("owner", ProjectRole.Owner, "next", Start));
        project.VerifyEvidence();
    }
    [Test]
    public void DefinitionChangesAdvanceEvidenceVersionWhileNoOpsDoNotCreateEvents()
    {
        var d = Definition(); var project = OperationalProject.Create(Guid.NewGuid(), Tenant, d, "owner", Start);
        var first = ProjectDefinitionRevision.Capture(project, "owner"); project.ClearDomainEvents();
        project.ReplaceDefinition(d with { Tasks = d.Tasks.Reverse().ToArray() }, "owner", Start);
        Assert.That(project.Version, Is.EqualTo(1)); Assert.That(project.DomainEvents, Is.Empty);
        project.ReplaceDefinition(d with { BudgetAmount = 11000 }, "owner", Start.AddHours(1));
        Assert.That(project.DefinitionVersion, Is.EqualTo(2)); Assert.That(project.DefinitionDigest, Is.Not.EqualTo(first.Digest));
        Assert.That(first.Definition.BudgetAmount, Is.EqualTo(10000)); first.VerifyEvidence();
        var other = OperationalProject.Create(project.Id, Guid.NewGuid(), d, "owner", Start);
        Assert.That(other.DefinitionDigest, Is.Not.EqualTo(first.Digest));
    }
    [Test]
    public void AmbiguousSkillsRepeatedIdentitiesAndInvalidMaterialDemandsAreRejected()
    {
        var d = Definition();
        Assert.Throws<ProjectRuleException>(() => ProjectDefinitionCodec.Canonicalize(d with { Tasks = [d.Tasks[0], d.Tasks[0]] }));
        Assert.Throws<ProjectRuleException>(() => ProjectDefinitionCodec.Canonicalize(d with { Tasks = [d.Tasks[0] with { Materials = [new("part", 0)] }, d.Tasks[1]] }));
        Assert.Throws<ProjectRuleException>(() => ProjectDefinitionCodec.Canonicalize(d with { Tasks = [d.Tasks[0] with
        { Resources = [new(Guid.NewGuid(), "Engineer", ResourceKind.Human, new Dictionary<string, int> { ["C#"] = 1, ["c#"] = 2 })] }, d.Tasks[1]] }));
    }
}

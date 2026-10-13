using FusionOps.Domain.Events;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.Tenancy;

namespace FusionOps.Domain.Projects;

public enum ProjectRole { Viewer, Planner, Approver, Operator, Owner }
public enum ProjectPermission { Read, EditDefinition, Plan, Approve, Execute, ManageMembers }
public sealed class ProjectMember
{
    private ProjectMember() { }
    internal ProjectMember(string subject, ProjectRole role) => (Subject, Role) = (subject, role);
    public string Subject { get; private set; } = string.Empty;
    public ProjectRole Role { get; private set; }
    internal void SetRole(ProjectRole role) => Role = role;
}

public sealed class OperationalProject : TenantOwnedEntity, IHasDomainEvents
{
    private readonly List<ProjectMember> _members = [];
    private readonly List<IDomainEvent> _events = [];
    private OperationalProject() { }
    public Guid Id { get; private set; }
    public long Version { get; private set; }
    public long DefinitionVersion { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string DefinitionJson { get; private set; } = string.Empty;
    public string DefinitionDigest { get; private set; } = string.Empty;
    public string InitialDefinitionDigest { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ProjectDefinition Definition => ProjectDefinitionCodec.Read(DefinitionJson);
    public IReadOnlyCollection<ProjectMember> Members => _members.AsReadOnly();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _events.Add(domainEvent);
    public void ClearDomainEvents() => _events.Clear();

    public static OperationalProject Create(Guid id, Guid tenant, ProjectDefinition definition, string actor, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new ProjectRuleException("project_id_required");
        CheckActor(actor);
        var text = ProjectDefinitionCodec.Canonicalize(definition);
        var project = new OperationalProject { Id = id, CreatedBy = actor, CreatedAt = Normalize(now), DefinitionVersion = 1 };
        project.BindTenant(tenant);
        project.SetDefinition(text);
        project.InitialDefinitionDigest = project.DefinitionDigest;
        project._members.Add(new(actor, ProjectRole.Owner));
        project.Changed(actor, "created", now);
        return project;
    }
    public bool Can(string subject, ProjectPermission permission)
    {
        if (!Enum.IsDefined(permission)) return false;
        var member = _members.SingleOrDefault(m => m.Subject == subject);
        if (member is null) return false;
        return member.Role == ProjectRole.Owner || permission == ProjectPermission.Read || (member.Role, permission) switch
        {
            (ProjectRole.Planner, ProjectPermission.EditDefinition or ProjectPermission.Plan) => true,
            (ProjectRole.Approver, ProjectPermission.Approve) => true,
            (ProjectRole.Operator, ProjectPermission.Execute) => true,
            _ => false
        };
    }
    public void Require(string actor, ProjectPermission permission) { CheckActor(actor); if (!Can(actor, permission)) throw new ProjectAccessException(); }
    public void ReplaceDefinition(ProjectDefinition definition, string actor, DateTimeOffset now)
    {
        Require(actor, ProjectPermission.EditDefinition);
        var text = ProjectDefinitionCodec.Canonicalize(definition);
        if (text == DefinitionJson) return;
        DefinitionVersion = checked(DefinitionVersion + 1);
        SetDefinition(text);
        Changed(actor, "definition_revised", now);
    }
    public void SetMember(string subject, ProjectRole role, string actor, DateTimeOffset now)
    {
        Require(actor, ProjectPermission.ManageMembers); CheckActor(subject);
        if (!Enum.IsDefined(role) || role == ProjectRole.Owner) throw new ProjectRuleException("project_owner_transfer_required");
        var member = _members.SingleOrDefault(m => m.Subject == subject);
        if (member?.Role == ProjectRole.Owner) throw new ProjectRuleException("project_owner_transfer_required");
        if (member?.Role == role) return;
        if (member is null)
        {
            if (_members.Count >= 200) throw new ProjectRuleException("project_members_limit");
            _members.Add(new(subject, role));
        }
        else member.SetRole(role);
        Changed(actor, "member_set", now);
    }
    public void RemoveMember(string subject, string actor, DateTimeOffset now)
    {
        Require(actor, ProjectPermission.ManageMembers); CheckActor(subject);
        var member = _members.SingleOrDefault(m => m.Subject == subject);
        if (member is null) return;
        if (member.Role == ProjectRole.Owner) throw new ProjectRuleException("project_owner_transfer_required");
        _members.Remove(member);
        Changed(actor, "member_removed", now);
    }
    public void TransferOwnership(string subject, string actor, DateTimeOffset now)
    {
        Require(actor, ProjectPermission.ManageMembers); CheckActor(subject);
        if (subject == actor) return;
        var target = _members.SingleOrDefault(m => m.Subject == subject) ?? throw new ProjectRuleException("project_member_required");
        _members.Single(m => m.Role == ProjectRole.Owner).SetRole(ProjectRole.Planner);
        target.SetRole(ProjectRole.Owner);
        Changed(actor, "ownership_transferred", now);
    }
    public void VerifyEvidence()
    {
        var text = ProjectDefinitionCodec.Canonicalize(Definition);
        if (text != DefinitionJson || ProjectDefinitionCodec.Digest(TenantId, Id, DefinitionVersion, text) != DefinitionDigest)
            throw new ProjectRuleException("project_evidence_invalid");
        if (_members.Count(m => m.Role == ProjectRole.Owner) != 1) throw new ProjectRuleException("project_owner_invalid");
    }
    private void SetDefinition(string text)
    {
        DefinitionJson = text; Name = ProjectDefinitionCodec.Read(text).Name;
        DefinitionDigest = ProjectDefinitionCodec.Digest(TenantId, Id, DefinitionVersion, text);
    }
    private void Changed(string actor, string change, DateTimeOffset now)
    {
        Version = checked(Version + 1); UpdatedAt = Normalize(now);
        _events.Add(new ProjectChanged(Id, Version, DefinitionVersion, DefinitionDigest, actor, change) { OccurredOn = UpdatedAt });
    }
    private static void CheckActor(string actor) { if (!ProjectDefinitionCodec.Text(actor, 200)) throw new ProjectRuleException("project_subject_invalid"); }
    private static DateTimeOffset Normalize(DateTimeOffset value) => new(value.UtcTicks / 10 * 10, TimeSpan.Zero);
}

public sealed class ProjectDefinitionRevision : TenantOwnedEntity
{
    private ProjectDefinitionRevision() { }
    public Guid ProjectId { get; private set; }
    public long DefinitionVersion { get; private set; }
    public long ProjectVersion { get; private set; }
    public string DefinitionJson { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; private set; }
    public ProjectDefinition Definition => ProjectDefinitionCodec.Read(DefinitionJson);
    public static ProjectDefinitionRevision Capture(OperationalProject project, string actor)
    {
        project.VerifyEvidence();
        var revision = new ProjectDefinitionRevision { ProjectId = project.Id, DefinitionVersion = project.DefinitionVersion,
            ProjectVersion = project.Version, DefinitionJson = project.DefinitionJson, Digest = project.DefinitionDigest, Actor = actor, RecordedAt = project.UpdatedAt };
        revision.BindTenant(project.TenantId);
        return revision;
    }
    public void VerifyEvidence()
    {
        if (ProjectDefinitionCodec.Canonicalize(Definition) != DefinitionJson
            || ProjectDefinitionCodec.Digest(TenantId, ProjectId, DefinitionVersion, DefinitionJson) != Digest)
            throw new ProjectRuleException("project_evidence_invalid");
    }
}

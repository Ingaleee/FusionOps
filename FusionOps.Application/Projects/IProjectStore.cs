using FusionOps.Domain.Projects;

namespace FusionOps.Application.Projects;

public interface IProjectAccess
{
    Task<bool> CanAsync(Guid projectId, ProjectPermission permission, CancellationToken ct);
    Task RequireAsync(Guid projectId, ProjectPermission permission, CancellationToken ct);
}
public interface IProjectStore : IProjectAccess
{
    Task<OperationalProject> CreateAsync(Guid id, ProjectDefinition definition, CancellationToken ct);
    Task<OperationalProject?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProjectSummary>> ListAsync(Guid? after, int limit, CancellationToken ct);
    Task<OperationalProject?> ReplaceAsync(Guid id, long version, ProjectDefinition definition, CancellationToken ct);
    Task<OperationalProject?> SetMemberAsync(Guid id, long version, string subject, ProjectRole role, CancellationToken ct);
    Task<OperationalProject?> RemoveMemberAsync(Guid id, long version, string subject, CancellationToken ct);
    Task<OperationalProject?> TransferOwnerAsync(Guid id, long version, string subject, CancellationToken ct);
    Task<IReadOnlyList<ProjectDefinitionRevision>?> RevisionsAsync(Guid id, long? before, int limit, CancellationToken ct);
}
public sealed record ProjectAccessPolicy(bool AllowUnregisteredDevelopmentProjects);
public sealed record ProjectSummary(Guid Id, string Name, long Version, long DefinitionVersion, string DefinitionDigest,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, ProjectRole Role);

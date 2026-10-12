using FusionOps.Domain.Events;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Domain.Shared.Interfaces;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Catalog;

public sealed record CatalogSkill(string Name, int Level);

public sealed class CatalogResource : IHasDomainEvents
{
    private readonly List<CatalogSkill> _skills = [];
    private readonly List<IDomainEvent> _events = [];
    private CatalogResource() { }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ResourceKind Kind { get; private set; }
    public Money HourRate { get; private set; }
    public string? EquipmentType { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyCollection<CatalogSkill> Skills => _skills.AsReadOnly();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _events.Add(domainEvent);
    public void ClearDomainEvents() => _events.Clear();

    public static CatalogResource Register(Guid id, string name, ResourceKind kind, Money rate,
        IReadOnlyDictionary<string, int> skills, string? equipmentType = null)
    {
        if (id == Guid.Empty) throw new DomainException("A catalog resource ID is required");
        var resource = new CatalogResource { Id = id, Kind = kind };
        resource.Revise(name, rate, skills, equipmentType, true);
        return resource;
    }

    public void Revise(string name, Money rate, IReadOnlyDictionary<string, int> skills, string? equipmentType, bool active)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 200 || !Enum.IsDefined(Kind))
            throw new DomainException("Catalog resources require a name of up to 200 characters and a valid kind");
        if (!Currency.All.Contains(rate.Currency) || rate.Amount is < 0 or > 1_000_000 || decimal.Round(rate.Amount, 6) != rate.Amount)
            throw new DomainException("Hourly rates require a supported currency and 0–1000000 with at most six decimal places");
        if (Kind == ResourceKind.Human && equipmentType is not null
            || Kind == ResourceKind.Equipment && (string.IsNullOrWhiteSpace(equipmentType) || equipmentType.Length > 64 || equipmentType != equipmentType.Trim()))
            throw new DomainException("Equipment requires a type; human resources cannot have an equipment type");
        if (skills is null || skills.Count > 20 || skills.Any(s => string.IsNullOrWhiteSpace(s.Key) || s.Key.Length > 64 || s.Key != s.Key.Trim() || s.Value is < 0 or > 5)
            || skills.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skills.Count)
            throw new DomainException("Provide up to 20 distinct skills with levels 0–5");
        Name = name;
        HourRate = rate;
        EquipmentType = equipmentType;
        IsActive = active;
        Revision = checked(Revision + 1);
        _skills.Clear();
        _skills.AddRange(skills.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Select(s => new CatalogSkill(s.Key.ToLowerInvariant(), s.Value)));
        AddDomainEvent(new CatalogResourceChanged(Id, Revision, IsActive));
    }
}

public sealed class ResourceAdmissionException(string code, Guid resourceId) : DomainException($"Resource {resourceId:D}: {code}")
{
    public string Code { get; } = code;
}
public sealed class CatalogVersionException() : DomainException("The catalog resource changed; reload its ETag");

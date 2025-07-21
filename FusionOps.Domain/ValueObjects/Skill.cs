using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Shared;

namespace FusionOps.Domain.ValueObjects;

/// <summary>
/// Combination of skill name and proficiency level.
/// </summary>
public readonly record struct Skill
{
    public string Name { get; }
    public SkillLevel Level { get; }

    public Skill(string name, SkillLevel level)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name)).Trim().ToLowerInvariant();
        Level = level;
    }

    public override string ToString() => $"{Name} (L{Level.Value})";
} 
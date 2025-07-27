using System.Collections.Generic;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Enumerations;

/// <summary>
/// Seniority levels used for human-resource skills.
/// </summary>
public readonly record struct SkillLevel(int Value, string Name) : IEnumeration
{
    public static readonly SkillLevel Junior = new(1, nameof(Junior));
    public static readonly SkillLevel Middle = new(2, nameof(Middle));
    public static readonly SkillLevel Senior = new(3, nameof(Senior));
    public static readonly SkillLevel Expert = new(4, nameof(Expert));

    public static IReadOnlyCollection<SkillLevel> All => new[] { Junior, Middle, Senior, Expert };

    public override string ToString() => Name;
}
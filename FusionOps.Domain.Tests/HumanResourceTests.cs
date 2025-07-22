using FusionOps.Domain.Entities;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.ValueObjects;
using NUnit.Framework;

namespace FusionOps.Domain.Tests;

public class HumanResourceTests
{
    [Test]
    public void HasSkill_ShouldReturnExpectedResult()
    {
        var hr = new HumanResource(new HumanResourceId(Guid.NewGuid()), "John Doe", Money.Usd(50));
        var skill = new Skill("csharp", SkillLevel.Senior);
        hr.AddSkill(skill);

        Assert.That(hr.HasSkill("csharp", SkillLevel.Senior), Is.True);
        Assert.That(hr.HasSkill("java", SkillLevel.Junior), Is.False);
    }
} 
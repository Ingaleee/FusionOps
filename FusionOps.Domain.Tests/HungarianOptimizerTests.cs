using System.Collections.Generic;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Services;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Shared.Ids;
using FusionOps.Domain.Enumerations;
using NUnit.Framework;

namespace FusionOps.Domain.Tests;

public class HungarianOptimizerTests
{
    [Test]
    public async Task AllocateAsync_ReturnsRequestedNumberOfAllocations()
    {
        // Arrange
        var humans = new List<HumanResource>
        {
            new(new HumanResourceId(System.Guid.NewGuid()), "A", Money.Usd(10)),
            new(new HumanResourceId(System.Guid.NewGuid()), "B", Money.Usd(20)),
            new(new HumanResourceId(System.Guid.NewGuid()), "C", Money.Usd(15))
        };

        var equipment = new List<EquipmentResource>
        {
            new(new EquipmentResourceId(System.Guid.NewGuid()), "E1", EquipmentType.CNC, Money.Usd(50)),
            new(new EquipmentResourceId(System.Guid.NewGuid()), "E2", EquipmentType.Printer, Money.Usd(40)),
            new(new EquipmentResourceId(System.Guid.NewGuid()), "E3", EquipmentType.GPU, Money.Usd(30))
        };

        var optimizer = new HungarianOptimizerStrategy();

        // Act
        var result = await optimizer.AllocateAsync(humans, equipment, requiredHumans: 2, requiredEquipment: 2);

        // Assert
        Assert.That(result.Count, Is.EqualTo(4));
    }
}
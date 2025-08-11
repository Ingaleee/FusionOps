using FusionOps.Domain.Entities;
using NUnit.Framework;

namespace FusionOps.Domain.Tests;

public class LicensePoolTests
{
    [Test]
    public void Allocate_WithinCapacity_IncreasesAllocated()
    {
        var pool = new LicensePool("ProductA", 10);
        pool.Allocate(Guid.NewGuid(), 3);
        Assert.That(pool.AllocatedSeats, Is.EqualTo(3));
    }

    [Test]
    public void Allocate_OverCapacity_Throws()
    {
        var pool = new LicensePool("ProductB", 2);
        Assert.Throws<Exception>(() => pool.Allocate(Guid.NewGuid(), 3));
    }

    [Test]
    public void Release_ReducesAllocated()
    {
        var pool = new LicensePool("ProductC", 5);
        var pid = Guid.NewGuid();
        pool.Allocate(pid, 4);
        pool.Release(pid, 2);
        Assert.That(pool.AllocatedSeats, Is.EqualTo(2));
    }
}



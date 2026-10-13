namespace FusionOps.Domain.Tests;

public sealed class ArchitectureTests
{
    [Test]
    public void DomainAndApplicationDoNotReferencePersistenceOrHttpImplementation()
    {
        foreach (var assembly in new[] { typeof(FusionOps.Domain.Entities.Allocation).Assembly, typeof(FusionOps.Application.AssemblyReference).Assembly })
        {
            var forbidden = assembly.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => n.StartsWith("FusionOps.Infrastructure") || n.StartsWith("Microsoft.EntityFrameworkCore") || n.StartsWith("Microsoft.AspNetCore"));
            Assert.That(forbidden, Is.Empty, assembly.GetName().Name);
        }
    }
}

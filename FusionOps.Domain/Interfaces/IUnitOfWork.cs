using System.Threading.Tasks;

namespace FusionOps.Domain.Interfaces;

public interface IUnitOfWork
{
    Task CommitAsync();
    Task RollbackAsync();
} 
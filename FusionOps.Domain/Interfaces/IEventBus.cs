using System.Threading.Tasks;
using FusionOps.Domain.Shared.Interfaces;

namespace FusionOps.Domain.Interfaces;

public interface IEventBus
{
    Task PublishAsync(IDomainEvent domainEvent);
}
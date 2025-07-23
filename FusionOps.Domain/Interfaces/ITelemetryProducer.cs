using System.Threading.Tasks;

namespace FusionOps.Domain.Interfaces;

public interface ITelemetryProducer
{
    Task PublishAsync(string eventType, object payload);
} 
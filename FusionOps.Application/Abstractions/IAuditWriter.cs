using System.Threading;
using System.Threading.Tasks;
using FusionOps.Domain.Events;

namespace FusionOps.Application.Abstractions;

public interface IAuditWriter
{
    Task WriteAsync<T>(AuditEnvelope<T> envelope, CancellationToken ct);
}



using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FusionOps.Domain.Events;
using FusionOps.Domain.Events.Attributes;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using FusionOps.Domain.Interfaces;
using FusionOps.Application.Abstractions;

namespace FusionOps.Application.Pipelines;

public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAuditWriter _auditWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUnitOfWork _uow;

    public AuditBehavior(IAuditWriter auditWriter, IHttpContextAccessor httpContextAccessor, IUnitOfWork uow)
    {
        _auditWriter = auditWriter;
        _httpContextAccessor = httpContextAccessor;
        _uow = uow;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();
        var domainEvents = _uow.GetDomainEventsAndClear();
        var actor = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "system";
        var correlationId = _httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
        foreach (var de in domainEvents)
        {
            var eventType = de.GetType().GetCustomAttributes(typeof(EventTypeAttribute), false) is EventTypeAttribute[] attrs && attrs.Length > 0
                ? attrs[0].Name
                : de.GetType().Name;

            var aggregateId = de switch
            {
                ResourceAllocated e => (Guid)e.AllocationId,
                ResourceAllocationCancelled e => (Guid)e.AllocationId,
                StockReplenished e => (Guid)e.WarehouseId,
                ReorderPointReached e => (Guid)e.WarehouseId,
                _ => Guid.Empty
            };

            var envelope = new AuditEnvelope<object>(
                Guid.NewGuid(),
                eventType,
                aggregateId,
                actor,
                correlationId,
                de,
                DateTime.UtcNow
            );
            await _auditWriter.WriteAsync(envelope, cancellationToken);
        }
        return response;
    }
}
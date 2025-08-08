using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FusionOps.Domain.Events;
using FusionOps.Domain.Events.Attributes;
using Microsoft.AspNetCore.Http;
using EventStore.Client;
using System.Text.Json;
using FusionOps.Domain.Interfaces;

namespace FusionOps.Application.Pipelines;

public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly EventStoreClient _eventStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUnitOfWork _uow;

    public AuditBehavior(EventStoreClient eventStore, IHttpContextAccessor httpContextAccessor, IUnitOfWork uow)
    {
        _eventStore = eventStore;
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
            var envelope = new AuditEnvelope<object>(
                Guid.NewGuid(),
                eventType,
                de.AggregateId,
                actor,
                correlationId,
                de,
                DateTime.UtcNow
            );
            var eventData = new EventData(
                Uuid.NewUuid(),
                envelope.EventType,
                JsonSerializer.SerializeToUtf8Bytes(envelope, envelope.GetType(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            );
            await _eventStore.AppendToStreamAsync($"audit-{envelope.AggregateId}", StreamState.Any, new[] { eventData }, cancellationToken: cancellationToken);
        }
        return response;
    }
}
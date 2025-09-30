using FusionOps.Application.Policies;
using FusionOps.Application.Abstractions;
using MediatR;

namespace FusionOps.Application.Pipelines;

public sealed class PolicyBehavior<TReq, TRes> : IPipelineBehavior<TReq, TRes>
{
    private readonly IPolicyEngine _engine;
    private readonly ITenantProvider _tenant;

    public PolicyBehavior(IPolicyEngine engine, ITenantProvider tenant)
    {
        _engine = engine;
        _tenant = tenant;
    }

    public async Task<TRes> Handle(TReq request, RequestHandlerDelegate<TRes> next, CancellationToken cancellationToken)
    {
        // Guard: only run for known commands (example placeholder)
        if (request is not object)
            return await next();

        // TODO: map request -> PolicyInput (domain-specific adapter). Placeholder decision allows.
        var input = new PolicyInput(
            _tenant.IsSet ? _tenant.TenantId : "_",
            "user",
            null,
            DateTime.UtcNow,
            0m,
            false,
            new FusionOps.Domain.ValueObjects.TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            Array.Empty<string>(),
            Array.Empty<string>());

        var _ = await _engine.EvaluateAsync("allocation", input, cancellationToken);
        return await next();
    }
}




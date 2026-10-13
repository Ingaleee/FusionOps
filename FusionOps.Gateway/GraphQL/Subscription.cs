using HotChocolate;
using HotChocolate.Subscriptions;
using HotChocolate.Execution;
using FusionOps.Gateway.Models;

namespace FusionOps.Gateway.GraphQL;

public class Subscription
{
    [Subscribe(With = nameof(SubscribeLowStock))]
    public LowStockAlert LowStock([EventMessage] LowStockAlert alert) => alert;

    public ValueTask<ISourceStream<LowStockAlert>> SubscribeLowStock([Service] ITopicEventReceiver receiver,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        var user = accessor.HttpContext?.User;
        var claims = user?.FindAll("tenant_id").ToArray();
        if (user?.Identity?.IsAuthenticated != true || !user.IsInRole("Stock.Admin") || claims is not { Length: 1 }
            || !Guid.TryParseExact(claims[0].Value, "D", out var tenant) || tenant == Guid.Empty)
            throw new GraphQLException("A verified tenant is required for subscriptions");
        return receiver.SubscribeAsync<LowStockAlert>($"tenant:{tenant:N}:lowStock", cancellationToken);
    }
}

using HotChocolate;
using HotChocolate.Subscriptions;
using FusionOps.Gateway.Models;

namespace FusionOps.Gateway.GraphQL;

public class Subscription
{
    [Subscribe]
    [Topic]
    public LowStockAlert LowStock([EventMessage] LowStockAlert alert) => alert;
}

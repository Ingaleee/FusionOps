using FusionOps.Infrastructure.SagaContracts;
using MassTransit;

namespace FusionOps.Infrastructure.Saga;

public class AllocationStateMachine : MassTransitStateMachine<AllocationState>
{
    public State Pending { get; private set; } = null!;
    public State Reserved { get; private set; } = null!;
    public State Shipped { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<ReserveResources> ReserveResourcesEvent { get; private set; } = null!;
    public Event<ShipStock> ShipStockEvent { get; private set; } = null!;
    public Event<AllocationCompleted> AllocationCompletedEvent { get; private set; } = null!;

    public AllocationStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => ReserveResourcesEvent, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => ShipStockEvent, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => AllocationCompletedEvent, x => x.CorrelateById(m => m.Message.CorrelationId));

        Initially(
            When(ReserveResourcesEvent)
                .Then(context =>
                {
                    context.Instance.ProjectId = context.Data.ProjectId;
                    context.Instance.ReservedAt = DateTime.UtcNow;
                })
                .TransitionTo(Reserved));

        During(Reserved,
            When(ShipStockEvent)
                .Then(ctx => ctx.Instance.ShippedAt = DateTime.UtcNow)
                .TransitionTo(Shipped));

        During(Shipped,
            When(AllocationCompletedEvent)
                .TransitionTo(Completed)
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
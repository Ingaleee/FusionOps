namespace FusionOps.Application.Abstractions;

public interface IExecutionContext
{
    string Actor { get; }
    string CorrelationId { get; }
}

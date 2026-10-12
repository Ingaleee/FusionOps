using System.Globalization;
using FusionOps.Application.Workflows;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Workflows;

namespace FusionOps.Presentation.Modules;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/workflows").WithTags("Reservation workflows");
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            routes.RequireAuthorization("ManageReservations");

        routes.MapPost("/", async (ProposalDto dto, ReservationWorkflowService service, HttpResponse response, CancellationToken ct) =>
        {
            var workflow = await service.ProposeAsync(dto.WorkflowId, new(dto.ProjectId, dto.ResourceIds,
                new TimeRange(dto.PeriodFrom, dto.PeriodTo), dto.Licenses, dto.CompletionTimeoutSeconds), ct);
            SetETag(response, workflow);
            return Results.Created($"/api/v1/workflows/{workflow.Id:D}", WorkflowView.From(workflow));
        });
        routes.MapGet("/{id:guid}", async (Guid id, ReservationWorkflowService service, HttpResponse response, CancellationToken ct) =>
        {
            var workflow = await service.GetAsync(id, ct);
            if (workflow is null) return Results.NotFound();
            SetETag(response, workflow);
            return Results.Ok(WorkflowView.From(workflow));
        });
        routes.MapGet("/", async (WorkflowState? state, int? limit, ReservationWorkflowService service, CancellationToken ct) =>
            Results.Ok((await service.ListAsync(state, limit ?? 20, ct)).Select(WorkflowView.From)));

        foreach (var mutation in Enum.GetValues<WorkflowMutation>())
        {
            routes.MapPost($"/{{id:guid}}/{mutation.ToString().ToLowerInvariant()}", async (Guid id, HttpRequest request,
                HttpResponse response, ReservationWorkflowService service, CancellationToken ct) =>
            {
                if (!request.Headers.TryGetValue("If-Match", out var values))
                    return Results.Problem(statusCode: 428, title: "Supply the current workflow ETag in If-Match", type: "precondition_required");
                var text = values.ToString();
                if (values.Count != 1 || text.Length < 3 || text[0] != '"' || text[^1] != '"'
                    || !long.TryParse(text.AsSpan(1, text.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                    || version < 1)
                    return Results.Problem(statusCode: 400, title: "If-Match must contain one quoted positive workflow version", type: "invalid_precondition");
                var workflow = await service.MutateAsync(id, version, mutation, ct);
                if (workflow is null) return Results.NotFound();
                SetETag(response, workflow);
                return Results.Accepted($"/api/v1/workflows/{id:D}", WorkflowView.From(workflow));
            });
        }
        return endpoints;
    }

    private static void SetETag(HttpResponse response, ReservationWorkflow workflow)
    {
        response.Headers.ETag = $"\"{workflow.Version.ToString(CultureInfo.InvariantCulture)}\"";
        response.Headers.CacheControl = "no-store";
    }

    public sealed record ProposalDto(Guid WorkflowId, Guid ProjectId, Guid[] ResourceIds, DateTimeOffset PeriodFrom,
        DateTimeOffset PeriodTo, LicenseRequirement[] Licenses, int CompletionTimeoutSeconds = 300);
    public sealed record WorkflowView(Guid Id, Guid ProjectId, WorkflowState State, WorkflowOutcome Outcome, long Version,
        string SpecificationHash, string SubmittedBy, string? ApprovedBy, TimeRange Period, IReadOnlyList<Guid> ResourceIds,
        IReadOnlyList<Guid> AllocationIds, IReadOnlyCollection<LicenseRequirement> Licenses, bool CancellationRequested,
        DateTimeOffset? DeadlineAt, DateTimeOffset? NextAttemptAt, int StepAttempts, WorkflowState? PausedStep,
        string? FailureCode, string? LastErrorCode, IReadOnlyList<WorkflowTransition> Transitions)
    {
        public static WorkflowView From(ReservationWorkflow w) => new(w.Id, w.ProjectId, w.State, w.Outcome, w.Version,
            w.SpecificationHash, w.SubmittedBy, w.ApprovedBy, w.Period, w.ResourceIds, w.AllocationIds, w.Licenses,
            w.CancellationRequested, w.DeadlineAt, w.NextAttemptAt, w.StepAttempts, w.PausedStep, w.FailureCode,
            w.LastErrorCode, w.Transitions.OrderBy(t => t.Version).ToArray());
    }
}

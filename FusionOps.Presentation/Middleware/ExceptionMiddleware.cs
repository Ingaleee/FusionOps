using FluentValidation;
using FusionOps.Domain.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Net;
using Microsoft.EntityFrameworkCore;
using FusionOps.Domain.Workflows;
using FusionOps.Domain.Catalog;
using FusionOps.Domain.Services.Planning;

namespace FusionOps.Presentation.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (WorkflowVersionException ex)
        {
            await WriteProblem(context, HttpStatusCode.PreconditionFailed, "workflow_version_mismatch", ex.Message);
        }
        catch (CatalogVersionException ex)
        {
            await WriteProblem(context, HttpStatusCode.PreconditionFailed, "catalog_version_mismatch", ex.Message);
        }
        catch (ResourceAdmissionException ex)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, ex.Code, ex.Message);
        }
        catch (QuoteAdmissionException ex)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, ex.Code, ex.Message);
        }
        catch (WorkflowApprovalException ex)
        {
            await WriteProblem(context, HttpStatusCode.Forbidden, "separate_approver_required", ex.Message);
        }
        catch (WorkflowSubmissionConflictException ex)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, "workflow_submission_conflict", ex.Message);
        }
        catch (IdempotencyConflictException ex)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, "idempotency_conflict", ex.Message);
        }
        catch (IdempotencyInProgressException ex)
        {
            context.Response.Headers.RetryAfter = "2";
            await WriteProblem(context, HttpStatusCode.Conflict, "idempotency_in_progress", ex.Message);
        }
        catch (AllocationConflictException ex)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, "allocation_conflict", ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteProblem(context, HttpStatusCode.Conflict, "concurrency_conflict", "The resource changed; reload and retry");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (ArgumentException ex)
        {
            await WriteProblem(context, HttpStatusCode.BadRequest, "invalid_argument", ex.Message);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain error");
            await WriteProblem(context, HttpStatusCode.UnprocessableEntity, "domain_error", ex.Message);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error");
            var errors = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage });
            await WriteProblem(context, HttpStatusCode.BadRequest, "validation_error", "Validation failed", errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error");
            await WriteProblem(context, HttpStatusCode.InternalServerError, "internal_error", "Unexpected error");
        }
    }

    private Task WriteProblem(HttpContext ctx, HttpStatusCode code, string type, string message, object? details = null)
    {
        var pb = new ProblemDetails
        {
            Status = (int)code,
            Title = message,
            Type = type,
            Extensions = { }
        };
        if (details != null)
            pb.Extensions["violations"] = details;

        ctx.Response.StatusCode = pb.Status ?? (int)code;
        pb.Extensions["correlationId"] = ctx.TraceIdentifier;
        return ctx.Response.WriteAsJsonAsync(pb, options: null, contentType: "application/problem+json");
    }
}

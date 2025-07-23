using FluentValidation;
using FusionOps.Domain.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Net;

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
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsJsonAsync(pb);
    }
} 
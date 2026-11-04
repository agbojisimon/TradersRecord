using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace TraderRecords.Api.API.ErrorHandling;

/// <summary>
/// Converts unexpected exceptions into the public INTERNAL_ERROR contract. The
/// exception and correlation identifier stay in server logs; clients see no stack trace.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        _logger.LogError(
            exception,
            "Unhandled exception. TraceId: {TraceId}",
            traceId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = ApiProblemDetails.Create(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "INTERNAL_ERROR",
            "An unexpected error occurred.");

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}

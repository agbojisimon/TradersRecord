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

        if (exception is BadHttpRequestException
            {
                StatusCode: StatusCodes.Status413PayloadTooLarge
            })
        {
            // Kestrel raises this while reading a body that exceeds the action's
            // RequestSizeLimit. It is a client transport failure, not a server fault.
            _logger.LogWarning(
                "Request body too large. TraceId: {TraceId}",
                traceId);

            httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

            var tooLargeProblem = ApiProblemDetails.Create(
                httpContext,
                StatusCodes.Status413PayloadTooLarge,
                "PAYLOAD_TOO_LARGE",
                "The request body exceeds the 256 KiB limit.");

            await httpContext.Response.WriteAsJsonAsync(
                tooLargeProblem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

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

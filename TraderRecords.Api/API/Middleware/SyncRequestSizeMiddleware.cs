using TraderRecords.Api.API.Controllers;
using TraderRecords.Api.API.ErrorHandling;

namespace TraderRecords.Api.API.Middleware;

/// <summary>
/// Enforces the transaction sync request-size limit before MVC body binding. The
/// in-process test server does not implement IHttpMaxRequestBodySizeFeature, so
/// this Content-Length guard complements the controller's Kestrel size limit.
/// </summary>
public sealed class SyncRequestSizeMiddleware
{
    private readonly RequestDelegate _next;

    public SyncRequestSizeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/transactions/sync")
            && context.Request.ContentLength > TransactionsController.MaximumRequestBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

            var problem = ApiProblemDetails.Create(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "PAYLOAD_TOO_LARGE",
                "The request body exceeds the 256 KiB limit.");

            await context.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);

            return;
        }

        await _next(context);
    }
}

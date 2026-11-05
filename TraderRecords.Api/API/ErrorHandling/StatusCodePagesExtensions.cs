using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;

namespace TraderRecords.Api.API.ErrorHandling;

/// <summary>
/// Adds the API status-code-page convention. Routing and status-code middleware
/// should not leave API clients with an empty 404/405/413 body; authentication
/// challenges format their own 401/403 responses. UseStatusCodePages only invokes
/// this handler when the response has no body or content type to preserve.
/// </summary>
public static class StatusCodePagesExtensions
{
    public static IApplicationBuilder UseApiStatusCodePages(this IApplicationBuilder app)
    {
        return app.UseStatusCodePages(async (StatusCodeContext statusCodeContext) =>
        {
            var context = statusCodeContext.HttpContext;

            if (!context.Request.Path.StartsWithSegments("/api")
                || context.Response.HasStarted)
            {
                return;
            }

            var statusCode = context.Response.StatusCode;

            if (statusCode < 400)
            {
                return;
            }

            var (code, detail) = statusCode switch
            {
                StatusCodes.Status404NotFound =>
                    ("NOT_FOUND", "The requested resource was not found."),
                StatusCodes.Status405MethodNotAllowed =>
                    ("METHOD_NOT_ALLOWED", "The HTTP method is not allowed for this resource."),
                StatusCodes.Status413PayloadTooLarge =>
                    ("PAYLOAD_TOO_LARGE", "The request body exceeds the 256 KiB limit."),
                _ =>
                    ("HTTP_ERROR", "The request could not be completed.")
            };

            var problem = ApiProblemDetails.Create(context, statusCode, code, detail);

            await context.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
        });
    }
}

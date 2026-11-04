using Microsoft.AspNetCore.Mvc;
using TraderRecords.Api.Application.Common;

namespace TraderRecords.Api.API.ErrorHandling;

/// <summary>
/// Translates application-level failures into HTTP ProblemDetails without putting
/// HTTP-specific types or status codes inside application services.
/// </summary>
public static class ResultExtensions
{
    public static IActionResult ToActionResult(this ControllerBase controller, Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        var problem = ApiProblemDetails.Create(
            controller.HttpContext,
            statusCode,
            error.Code,
            error.Message,
            errors: error.FieldErrors);

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}

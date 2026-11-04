using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace TraderRecords.Api.API.ErrorHandling;

/// <summary>
/// Creates the single ProblemDetails shape used by controllers, authentication
/// challenges, model-state failures, status-code responses and global exceptions.
/// </summary>
public static class ApiProblemDetails
{
    public static ProblemDetails Create(
        HttpContext context,
        int statusCode,
        string code,
        string detail,
        string? title = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Type = GetTypeUri(code),
            Title = title ?? ReasonPhrases.GetReasonPhrase(statusCode),
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path.ToString()
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }

    private static string GetTypeUri(string code) => code switch
    {
        "VALIDATION_FAILED" => "urn:traderrecords:error:validation",
        "INVALID_CREDENTIALS" or "UNAUTHORIZED" => "urn:traderrecords:error:authentication",
        "FORBIDDEN" or "CONSENT_REQUIRED" => "urn:traderrecords:error:authorization",
        "EMAIL_IN_USE" or "TARGET_NOT_LENDER" => "urn:traderrecords:error:conflict",
        "BUSINESS_NOT_FOUND" or "LENDER_NOT_FOUND" or "NOT_FOUND" => "urn:traderrecords:error:not-found",
        "DATABASE_BUSY" => "urn:traderrecords:error:unavailable",
        "PAYLOAD_TOO_LARGE" => "urn:traderrecords:error:payload-too-large",
        "INTERNAL_ERROR" => "urn:traderrecords:error:internal",
        _ => $"urn:traderrecords:error:{code.ToLowerInvariant()}"
    };
}

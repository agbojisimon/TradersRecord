using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TraderRecords.Api.API.ErrorHandling;
using TraderRecords.Api.API.Json;
using TraderRecords.Api.API.OpenApi;

namespace TraderRecords.Api.API;

/// <summary>
/// Registers HTTP-facing API concerns: controllers, the JSON contract, validation
/// ProblemDetails, exception handling, and OpenAPI document customisation.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddTraderRecordsApi(this IServiceCollection services)
    {
        // Controllers are used instead of minimal APIs because the specification maps
        // each business capability to an attribute-routed controller.
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                // The public contract is camelCase and enum values are strings (sections 05 and 08).
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                // Reject numeric enum values; the public contract uses names only.
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(allowIntegerValues: false));
                // Occurrence timestamps must carry Z or an explicit offset. Offset-less
                // values become item-level INVALID_OCCURRED_AT instead of local-time data.
                options.JsonSerializerOptions.Converters.Add(
                    new RequiredOffsetDateTimeOffsetConverter());
            });

        // Model-binding failures use the same ProblemDetails contract as service failures.
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value!.Errors
                            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                                ? "The value is invalid."
                                : error.ErrorMessage)
                            .ToArray(),
                        StringComparer.OrdinalIgnoreCase);

                var problem = ApiProblemDetails.Create(
                    context.HttpContext,
                    StatusCodes.Status400BadRequest,
                    "VALIDATION_FAILED",
                    "One or more validation errors occurred.",
                    errors: errors);

                return new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = { "application/problem+json" }
                };
            };
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // .NET 9's built-in OpenAPI generator produces /openapi/v1.json in Development.
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
            options.AddSchemaTransformer<StringEnumSchemaTransformer>();
        });

        return services;
    }
}

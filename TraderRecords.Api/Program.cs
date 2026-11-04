using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TraderRecords.Api.API.ErrorHandling;
using TraderRecords.Api.API.OpenApi;
using TraderRecords.Api.Infrastructure;
using TraderRecords.Api.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Controllers are used instead of minimal APIs because the specification maps
// each business capability to an attribute-routed controller.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // The public contract is camelCase and enum values are strings (sections 05 and 08).
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        // Reject numeric enum values; the public contract uses names only.
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(allowIntegerValues: false));
    });

// Model-binding failures use the same ProblemDetails contract as service failures.
builder.Services.Configure<ApiBehaviorOptions>(options =>
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

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// .NET 9's built-in OpenAPI generator produces /openapi/v1.json in Development.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});

var sqliteConnection = SqliteConnectionStringResolver.Resolve(
    builder.Configuration,
    builder.Environment.ContentRootPath);

builder.Services.AddTraderRecordsInfrastructure(
    builder.Configuration,
    builder.Environment,
    sqliteConnection.ConnectionString);

var app = builder.Build();

app.Logger.LogInformation(
    "Using SQLite database at {DatabasePath}",
    sqliteConnection.DataSource);

if (app.Environment.IsDevelopment())
{
    // Development startup owns schema creation and demo seeding. Non-development
    // startup deliberately does not silently migrate or seed.
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
    await seeder.SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    // Keep the specification and Scalar UI development-only so they are not
    // accidentally exposed as part of a future production deployment.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();

// Routing and status-code middleware should not leave API clients with an empty
// 404/405 body. Authentication challenges format their own 401/403 responses.
app.UseStatusCodePages(async (StatusCodeContext statusCodeContext) =>
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

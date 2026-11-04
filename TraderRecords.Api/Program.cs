using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using TraderRecords.Api.API.ErrorHandling;
using TraderRecords.Api.API.OpenApi;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Application.Services;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Infrastructure.Authentication;
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

// Resolve relative SQLite paths against ContentRootPath so launching from another
// working directory cannot select a different database. Absolute paths and
// special SQLite data sources are preserved so configuration overrides work.
var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured.");

var sqliteConnectionString = new SqliteConnectionStringBuilder(configuredConnectionString);
var configuredDataSource = sqliteConnectionString.DataSource;

if (string.IsNullOrWhiteSpace(configuredDataSource))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' must specify a SQLite Data Source.");
}

if (!IsSpecialSqliteDataSource(configuredDataSource) && !Path.IsPathRooted(configuredDataSource))
{
    sqliteConnectionString.DataSource = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, configuredDataSource));
}

var resolvedDataSource = sqliteConnectionString.DataSource;

if (!IsSpecialSqliteDataSource(resolvedDataSource))
{
    var databaseDirectory = Path.GetDirectoryName(resolvedDataSource);

    if (!string.IsNullOrEmpty(databaseDirectory))
    {
        Directory.CreateDirectory(databaseDirectory);
    }
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqliteConnectionString.ConnectionString));

// TimeProvider keeps time-dependent behaviour replaceable with a fixed clock in tests.
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// Use the framework password hasher; no custom password algorithm is introduced.
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Bind and validate JWT settings before any token can be created. A missing or
// short signing key is a startup configuration error, not a runtime surprise.
var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>() ?? new JwtOptions();

if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey must be configured with at least 32 bytes. " +
        "Use dotnet user-secrets or the Jwt__SigningKey environment variable.");
}

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Issuer),
        "Jwt:Issuer is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Audience),
        "Jwt:Audience is required.")
    .Validate(
        options => options.ExpiryMinutes > 0,
        "Jwt:ExpiryMinutes must be positive.")
    .Validate(
        options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
        "Jwt:SigningKey must contain at least 32 bytes.")
    .ValidateOnStart();

var signingKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the compact claim names ("sub", "role") instead of the framework's
        // default inbound claim mapping. Ownership checks read "sub" explicitly.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            NameClaimType = JwtClaimNames.Subject,
            RoleClaimType = JwtClaimNames.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();

                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers["WWW-Authenticate"] = "Bearer";

                var problem = ApiProblemDetails.Create(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "UNAUTHORIZED",
                    "Authentication is required.");

                await context.Response.WriteAsJsonAsync(
                    problem,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: context.HttpContext.RequestAborted);
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;

                var problem = ApiProblemDetails.Create(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "FORBIDDEN",
                    "You do not have permission to access this resource.");

                await context.Response.WriteAsJsonAsync(
                    problem,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: context.HttpContext.RequestAborted);
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<DevelopmentSeeder>();
}

var app = builder.Build();

app.Logger.LogInformation("Using SQLite database at {DatabasePath}", resolvedDataSource);

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

static bool IsSpecialSqliteDataSource(string dataSource) =>
    dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
    || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

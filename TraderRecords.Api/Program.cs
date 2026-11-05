using Scalar.AspNetCore;
using TraderRecords.Api.API;
using TraderRecords.Api.API.ErrorHandling;
using TraderRecords.Api.API.Middleware;
using TraderRecords.Api.Infrastructure;
using TraderRecords.Api.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// HTTP-facing registrations: controllers, JSON contract, validation ProblemDetails,
// exception handling, and OpenAPI document customisation.
builder.Services.AddTraderRecordsApi();

var sqliteConnection = SqliteConnectionStringResolver.Resolve(
    builder.Configuration,
    builder.Environment.ContentRootPath);

// Infrastructure registrations: SQLite persistence, application services, JWT
// authentication, and role authorisation. Connection resolution stays here so the
// startup log can report the exact database location.
builder.Services.AddTraderRecordsInfrastructure(
    builder.Configuration,
    builder.Environment,
    sqliteConnection.ConnectionString);

var app = builder.Build();

app.Logger.LogInformation(
    "Using SQLite database at {DatabasePath}",
    sqliteConnection.DataSource);

// Development startup owns schema creation and demo seeding. Non-development
// startup deliberately does not silently migrate or seed, and failures surface.
await app.InitializeDevelopmentDatabaseAsync(app.Lifetime.ApplicationStopping);

if (app.Environment.IsDevelopment())
{
    // Keep the specification and Scalar UI development-only so they are not
    // accidentally exposed as part of a future production deployment.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Middleware order is intentional:
// exception handling -> sync size guard -> status-code pages -> authentication -> authorisation.
app.UseExceptionHandler();

// Reject oversized sync requests before MVC reads or binds the body. The controller
// keeps its RequestSizeLimit for Kestrel; this guard also covers the in-process test server.
app.UseMiddleware<SyncRequestSizeMiddleware>();

// Give API 404/405/413 responses a ProblemDetails body without replacing responses
// that already have content. Authentication challenges format their own 401/403.
app.UseApiStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the generated top-level Program class to WebApplicationFactory in tests.
public partial class Program
{
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TraderRecords.Api.Domain.Entities;
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
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// .NET 9's built-in OpenAPI generator produces /openapi/v1.json in Development.
builder.Services.AddOpenApi();

// Resolve the SQLite file against ContentRootPath so launching from another
// working directory cannot accidentally create or read a different database.
var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured.");

var sqliteConnectionString = new SqliteConnectionStringBuilder(configuredConnectionString);
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
sqliteConnectionString.DataSource = Path.Combine(dataDirectory, "traderrecords.db");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqliteConnectionString.ConnectionString));

// TimeProvider keeps time-dependent behaviour replaceable with a fixed clock in tests.
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// Use the framework password hasher; no custom password algorithm is introduced.
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<DevelopmentSeeder>();
}

var app = builder.Build();

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

// Gate 2 uses the loopback-only HTTP profile. HTTPS redirection will be added
// when a trusted local HTTPS profile is introduced in a later gate.
app.MapControllers();

app.Run();

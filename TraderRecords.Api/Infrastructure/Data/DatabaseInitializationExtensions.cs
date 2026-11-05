using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TraderRecords.Api.Infrastructure.Data;

/// <summary>
/// Applies development-only database startup work: migrate the schema and seed demo
/// data. Non-development environments deliberately do nothing here, and startup
/// failures are allowed to surface rather than being swallowed.
/// </summary>
public static class DatabaseInitializationExtensions
{
    public static async Task InitializeDevelopmentDatabaseAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}

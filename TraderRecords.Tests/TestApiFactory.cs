using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TraderRecords.Api.Infrastructure.Data;

namespace TraderRecords.Tests;

/// <summary>
/// Creates one isolated SQLite database per test instance. The fixed clock makes
/// the 365-day admission window deterministic, and the test signing key satisfies
/// startup validation without using a real secret.
/// </summary>
public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset FixedNow = TruncateToMilliseconds(
        DateTimeOffset.UtcNow.AddMinutes(-1));

    private const string TestSigningKey =
        "test-signing-key-at-least-32-bytes-long-1234567890";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"traderrecords-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection",
            $"Data Source={_databasePath};Foreign Keys=True;Default Timeout=5;Pooling=False");
        builder.UseSetting("Jwt:Issuer", "TraderRecords.Api");
        builder.UseSetting("Jwt:Audience", "TraderRecords.Demo");
        builder.UseSetting("Jwt:ExpiryMinutes", "60");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"Data Source={_databasePath};Foreign Keys=True;Default Timeout=5;Pooling=False",
                ["Jwt:Issuer"] = "TraderRecords.Api",
                ["Jwt:Audience"] = "TraderRecords.Demo",
                ["Jwt:ExpiryMinutes"] = "60",
                ["Jwt:SigningKey"] = TestSigningKey
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value)
    {
        var utcTicks = value.UtcDateTime.Ticks;

        return new DateTimeOffset(
            utcTicks - (utcTicks % TimeSpan.TicksPerMillisecond),
            TimeSpan.Zero);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        foreach (var path in new[]
                 {
                     _databasePath,
                     $"{_databasePath}-wal",
                     $"{_databasePath}-shm"
                 })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // The operating system may briefly retain a handle after host disposal.
            }
        }
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

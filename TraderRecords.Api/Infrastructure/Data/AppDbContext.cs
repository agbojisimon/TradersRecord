using Microsoft.EntityFrameworkCore;
using TraderRecords.Api.Domain.Entities;

namespace TraderRecords.Api.Infrastructure.Data;

/// <summary>
/// The single application DbContext. Entity mappings are discovered from this
/// assembly only; configuration scanning does not register services or seed data.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Business> Businesses => Set<Business>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Consent> Consents => Set<Consent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // All four IEntityTypeConfiguration<T> classes live in this same assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Apply the UTC convention once instead of repeating a converter on every
        // timestamp property. This is important for SQLite TEXT date comparisons.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }
}

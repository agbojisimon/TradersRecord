using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TraderRecords.Api.Infrastructure.Data;

/// <summary>
/// Normalises every DateTime written to SQLite to UTC and restores the UTC kind
/// when reading. SQLite itself stores the value as TEXT; this converter prevents
/// mixed local/unspecified kinds from leaking into range queries and responses.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}

/// <summary>
/// Nullable counterpart for timestamps such as Consent.RevokedAtUtc.
/// </summary>
public sealed class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public UtcNullableDateTimeConverter()
        : base(
            value => value.HasValue
                ? (value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime())
                : value,
            value => value.HasValue
                ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                : value)
    {
    }
}

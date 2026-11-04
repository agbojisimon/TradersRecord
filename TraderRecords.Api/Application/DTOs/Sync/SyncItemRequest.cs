namespace TraderRecords.Api.Application.DTOs.Sync;

/// <summary>
/// One offline-created transaction. Fields are nullable on purpose: missing or
/// malformed semantic values become item-level rejections, not a rejected batch.
/// Type is a string rather than an enum so an unknown value is also item-level.
/// </summary>
public sealed class SyncItemRequest
{
    public Guid? ClientId { get; init; }

    public string? Type { get; init; }

    public long? AmountMinor { get; init; }

    public string? Note { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }
}

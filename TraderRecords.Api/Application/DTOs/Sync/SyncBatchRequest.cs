namespace TraderRecords.Api.Application.DTOs.Sync;

/// <summary>
/// Sync envelope. The list is nullable so a missing or null items property can be
/// rejected as one request-wide validation failure rather than a per-item result.
/// </summary>
public sealed class SyncBatchRequest
{
    public List<SyncItemRequest?>? Items { get; init; }
}

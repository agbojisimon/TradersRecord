namespace TraderRecords.Api.Application.DTOs.Sync;

/// <summary>
/// Ordered partial-success response. Counts always partition ReceivedCount:
/// Accepted + Duplicate + Rejected equals Received.
/// </summary>
public sealed class SyncBatchResponse
{
    public int ReceivedCount { get; init; }

    public int AcceptedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int RejectedCount { get; init; }

    public IReadOnlyList<SyncItemResult> Results { get; init; } = Array.Empty<SyncItemResult>();
}

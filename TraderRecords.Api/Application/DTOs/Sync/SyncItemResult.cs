namespace TraderRecords.Api.Application.DTOs.Sync;

/// <summary>
/// Per-item outcome. TransactionId is present only for Accepted and Duplicate,
/// because those outcomes point at a stored transaction.
/// </summary>
public sealed class SyncItemResult
{
    public int Index { get; init; }

    public Guid? ClientId { get; init; }

    public SyncItemStatus Status { get; init; }

    public Guid? TransactionId { get; init; }

    public IReadOnlyList<ItemError> Errors { get; init; } = Array.Empty<ItemError>();
}

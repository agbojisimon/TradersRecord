namespace TraderRecords.Api.Application.DTOs.Sync;

/// <summary>
/// A single item-level reason. Field is optional because some errors apply to the
/// whole item, such as a null array entry.
/// </summary>
public sealed class ItemError
{
    public string Code { get; init; } = string.Empty;

    public string? Field { get; init; }

    public string Message { get; init; } = string.Empty;
}

namespace TraderRecords.Api.Application.Common;

/// <summary>
/// Represents a successful operation that has no response payload.
/// </summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}

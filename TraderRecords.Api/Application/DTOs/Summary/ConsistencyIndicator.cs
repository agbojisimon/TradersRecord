namespace TraderRecords.Api.Application.DTOs.Summary;

/// <summary>
/// Transparent record-coverage indicator. It measures recording consistency, not
/// income quality, truthfulness, or creditworthiness.
/// </summary>
public sealed class ConsistencyIndicator
{
    public int Score { get; init; }

    public int ActiveDays { get; init; }

    public int ActiveWeeks { get; init; }

    public int HistorySpanWeeks { get; init; }

    public string Disclaimer { get; init; } = string.Empty;

    public IReadOnlyList<string> Guidance { get; init; } = Array.Empty<string>();
}

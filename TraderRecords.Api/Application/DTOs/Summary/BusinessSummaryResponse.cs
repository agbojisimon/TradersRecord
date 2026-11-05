namespace TraderRecords.Api.Application.DTOs.Summary;

/// <summary>
/// Trader-facing cash-flow summary for the fixed eight-week Lagos reporting window.
/// Money is always integer kobo and weekly buckets are chronological.
/// </summary>
public sealed class BusinessSummaryResponse
{
    public Guid BusinessId { get; init; }

    public string BusinessName { get; init; } = string.Empty;

    public string Currency { get; init; } = "NGN";

    public DateTime GeneratedAtUtc { get; init; }

    public DateTime WindowStartUtc { get; init; }

    public DateTime WindowEndExclusiveUtc { get; init; }

    public long TotalSalesMinor { get; init; }

    public long TotalExpensesMinor { get; init; }

    public long NetCashFlowMinor { get; init; }

    public int TransactionCount { get; init; }

    public IReadOnlyList<WeeklySummary> Weekly { get; init; } = Array.Empty<WeeklySummary>();

    public ConsistencyIndicator Indicator { get; init; } = new();
}

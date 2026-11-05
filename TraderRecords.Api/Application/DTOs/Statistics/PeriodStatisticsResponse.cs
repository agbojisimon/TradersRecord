namespace TraderRecords.Api.Application.DTOs.Statistics;

/// <summary>
/// Date-range cash-flow statistics for a business. Money is integer kobo, and
/// net cash flow is sales minus expenses, not accounting profit.
/// </summary>
public sealed class PeriodStatisticsResponse
{
    public const string MissingRecordsWarning =
        "Missing records do not prove inactivity. Net cash flow is sales minus expenses, " +
        "not accounting profit; these statistics are not annual income or a credit score.";

    public Guid BusinessId { get; init; }

    public string BusinessName { get; init; } = string.Empty;

    public string Currency { get; init; } = "NGN";

    public DateOnly RequestedFromLocalDate { get; init; }

    public DateOnly RequestedToLocalDate { get; init; }

    public StatisticsGroupBy GroupBy { get; init; }

    public DateTime GeneratedAtUtc { get; init; }

    public DateTime WindowStartUtc { get; init; }

    public DateTime WindowEndExclusiveUtc { get; init; }

    public DateOnly? EarliestAvailableLocalDate { get; init; }

    public DateOnly? LatestAvailableLocalDate { get; init; }

    public long TotalSalesMinor { get; init; }

    public long TotalExpensesMinor { get; init; }

    public long NetCashFlowMinor { get; init; }

    public int SaleCount { get; init; }

    public int ExpenseCount { get; init; }

    public int RecordedActivityDays { get; init; }

    public string Warning { get; init; } = MissingRecordsWarning;

    public IReadOnlyList<PeriodStatisticsGroup> Groups { get; init; } =
        Array.Empty<PeriodStatisticsGroup>();
}

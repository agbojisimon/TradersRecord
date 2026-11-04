namespace TraderRecords.Api.Application.DTOs.Summary;

/// <summary>
/// One Monday-to-Sunday Lagos calendar bucket. RecordedDays counts distinct local
/// occurrence dates, not receipts.
/// </summary>
public sealed class WeeklySummary
{
    public DateOnly WeekStartLocalDate { get; init; }

    public long SalesMinor { get; init; }

    public long ExpensesMinor { get; init; }

    public long NetCashFlowMinor { get; init; }

    public int TransactionCount { get; init; }

    public int RecordedDays { get; init; }
}

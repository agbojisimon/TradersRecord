namespace TraderRecords.Api.Application.DTOs.Statistics;

/// <summary>
/// One chronological statistics bucket. Period boundaries are inclusive Lagos
/// dates and are clipped to the requested range.
/// </summary>
public sealed class PeriodStatisticsGroup
{
    public DateOnly PeriodStartLocalDate { get; init; }

    public DateOnly PeriodEndLocalDate { get; init; }

    public long SalesMinor { get; init; }

    public long ExpensesMinor { get; init; }

    public long NetCashFlowMinor { get; init; }

    public int SaleCount { get; init; }

    public int ExpenseCount { get; init; }

    public int RecordedActivityDays { get; init; }
}

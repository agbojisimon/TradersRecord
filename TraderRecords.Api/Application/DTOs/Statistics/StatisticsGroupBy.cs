namespace TraderRecords.Api.Application.DTOs.Statistics;

/// <summary>
/// Supported chronological grouping for date-range statistics. Weeks start on
/// Monday in the Africa/Lagos calendar.
/// </summary>
public enum StatisticsGroupBy
{
    Day,
    Week,
    Month
}

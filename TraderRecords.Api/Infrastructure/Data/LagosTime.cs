namespace TraderRecords.Api.Infrastructure.Data;

/// <summary>
/// Converts between UTC and the Africa/Lagos calendar used by the eight-week
/// reporting window. Windows uses the fallback ID when the IANA ID is unavailable.
/// </summary>
public static class LagosTime
{
    private static readonly TimeZoneInfo Zone = ResolveLagosTimeZone();

    public static DateTime GetWeekStartLocal(DateTime utcNow)
    {
        var local = ToLocal(utcNow);

        // Monday is day 1; this expression maps Monday to 0 and Sunday to 6.
        var daysSinceMonday = ((int)local.DayOfWeek + 6) % 7;
        return local.Date.AddDays(-daysSinceMonday);
    }

    public static DateTime ToLocal(DateTime utc)
    {
        var utcValue = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcValue, Zone);
    }

    public static DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    private static TimeZoneInfo ResolveLagosTimeZone()
    {
        foreach (var timeZoneId in new[] { "Africa/Lagos", "W. Central Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException(
            "The Africa/Lagos time zone is not available on this machine.");
    }
}

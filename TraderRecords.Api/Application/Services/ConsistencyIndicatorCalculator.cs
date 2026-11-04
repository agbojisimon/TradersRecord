using TraderRecords.Api.Application.DTOs.Summary;

namespace TraderRecords.Api.Application.Services;

/// <summary>
/// Pure implementation of record-consistency-v1. Keeping the formula free of EF and
/// time-zone code makes the rounding and clamping behaviour directly testable.
/// </summary>
public static class ConsistencyIndicatorCalculator
{
    public const string Disclaimer =
        "Based on self-reported records; not verified income or a creditworthiness assessment.";

    private const int WindowDays = 56;
    private const int WindowWeeks = 8;
    private const int ActiveDayTarget = 40;

    public static ConsistencyIndicator Create(
        int activeDays,
        int activeWeeks,
        int historySpanWeeks,
        bool hasTransactions)
    {
        var guidance = new List<string>();

        if (!hasTransactions)
        {
            guidance.Add("Start recording transactions to build a history.");
        }
        else
        {
            if (activeWeeks < WindowWeeks)
            {
                guidance.Add(
                    "Build weekly history by recording transactions across more weeks.");
            }

            if (activeDays < ActiveDayTarget)
            {
                guidance.Add("Record activity consistently across more days.");
            }
        }

        return new ConsistencyIndicator
        {
            Score = CalculateScore(activeDays, activeWeeks, historySpanWeeks),
            ActiveDays = activeDays,
            ActiveWeeks = activeWeeks,
            HistorySpanWeeks = historySpanWeeks,
            Disclaimer = Disclaimer,
            Guidance = guidance
        };
    }

    public static int CalculateScore(
        int activeDays,
        int activeWeeks,
        int historySpanWeeks)
    {
        var rawScore =
            (50m * activeDays / WindowDays)
            + (30m * activeWeeks / WindowWeeks)
            + (20m * historySpanWeeks / WindowWeeks);

        var rounded = Math.Round(rawScore, 0, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, 0m, 100m);
    }
}

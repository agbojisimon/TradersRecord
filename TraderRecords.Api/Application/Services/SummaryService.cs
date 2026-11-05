using Microsoft.EntityFrameworkCore;
using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Statistics;
using TraderRecords.Api.Application.DTOs.Summary;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;

namespace TraderRecords.Api.Application.Services;

/// <summary>
/// Computes the fixed eight-week Lagos summary and date-range statistics from
/// persisted transactions. Reporting is based on occurrence time, never receipt
/// time, and all money remains integer kobo.
/// </summary>
public sealed class SummaryService : ISummaryService
{
    private const int WeekCount = 8;
    private const int DaysPerWeek = 7;
    private const int MaximumStatisticsRangeDays = 366;

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public SummaryService(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<Result<BusinessSummaryResponse>> GetForTraderAsync(
        Guid traderUserId,
        CancellationToken ct)
    {
        var businessResult = await ResolveBusinessForTraderAsync(traderUserId, ct);

        if (!businessResult.IsSuccess)
        {
            return Result<BusinessSummaryResponse>.Failure(businessResult.Error);
        }

        return await BuildSummaryAsync(businessResult.Value, ct);
    }

    public async Task<Result<BusinessSummaryResponse>> GetForLenderAsync(
        Guid lenderUserId,
        Guid businessId,
        CancellationToken ct)
    {
        var businessResult = await ResolveBusinessForLenderAsync(
            lenderUserId,
            businessId,
            ct);

        if (!businessResult.IsSuccess)
        {
            return Result<BusinessSummaryResponse>.Failure(businessResult.Error);
        }

        return await BuildSummaryAsync(businessResult.Value, ct);
    }

    public async Task<Result<PeriodStatisticsResponse>> GetStatisticsForTraderAsync(
        Guid traderUserId,
        DateOnly? fromLocalDate,
        DateOnly? toLocalDate,
        string? groupBy,
        CancellationToken ct)
    {
        var businessResult = await ResolveBusinessForTraderAsync(traderUserId, ct);

        if (!businessResult.IsSuccess)
        {
            return Result<PeriodStatisticsResponse>.Failure(businessResult.Error);
        }

        return await BuildPeriodStatisticsAsync(
            businessResult.Value,
            fromLocalDate,
            toLocalDate,
            groupBy,
            ct);
    }

    public async Task<Result<PeriodStatisticsResponse>> GetStatisticsForLenderAsync(
        Guid lenderUserId,
        Guid businessId,
        DateOnly? fromLocalDate,
        DateOnly? toLocalDate,
        string? groupBy,
        CancellationToken ct)
    {
        var businessResult = await ResolveBusinessForLenderAsync(
            lenderUserId,
            businessId,
            ct);

        if (!businessResult.IsSuccess)
        {
            return Result<PeriodStatisticsResponse>.Failure(businessResult.Error);
        }

        return await BuildPeriodStatisticsAsync(
            businessResult.Value,
            fromLocalDate,
            toLocalDate,
            groupBy,
            ct);
    }

    private async Task<Result<Business>> ResolveBusinessForTraderAsync(
        Guid traderUserId,
        CancellationToken ct)
    {
        var business = await _db.Businesses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OwnerUserId == traderUserId,
                ct);

        if (business is null)
        {
            return Result<Business>.Failure(
                Error.NotFound(
                    "BUSINESS_NOT_FOUND",
                    "The trader's business was not found."));
        }

        return Result<Business>.Success(business);
    }

    private async Task<Result<Business>> ResolveBusinessForLenderAsync(
        Guid lenderUserId,
        Guid businessId,
        CancellationToken ct)
    {
        if (businessId == Guid.Empty)
        {
            return Result<Business>.Failure(
                Error.Validation(
                    "A valid business ID is required.",
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["businessId"] = ["A non-empty business ID is required."]
                    }));
        }

        var business = await _db.Businesses
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == businessId, ct);

        if (business is null)
        {
            return Result<Business>.Failure(
                Error.NotFound("BUSINESS_NOT_FOUND", "The business was not found."));
        }

        var hasActiveConsent = await _db.Consents
            .AsNoTracking()
            .AnyAsync(
                consent => consent.BusinessId == businessId
                    && consent.LenderUserId == lenderUserId
                    && consent.RevokedAtUtc == null,
                ct);

        if (!hasActiveConsent)
        {
            return Result<Business>.Failure(
                Error.Forbidden(
                    "CONSENT_REQUIRED",
                    "Active consent is required."));
        }

        return Result<Business>.Success(business);
    }

    private async Task<Result<BusinessSummaryResponse>> BuildSummaryAsync(
        Business business,
        CancellationToken ct)
    {
        var nowUtc = TruncateToMilliseconds(_timeProvider.GetUtcNow().UtcDateTime);
        var currentWeekStartLocal = LagosTime.GetWeekStartLocal(nowUtc);
        var windowStartLocal = currentWeekStartLocal.AddDays(
            -(WeekCount - 1) * DaysPerWeek);
        var windowStartUtc = LagosTime.ToUtc(windowStartLocal);
        var windowEndExclusiveUtc = LagosTime.ToUtc(
            currentWeekStartLocal.AddDays(DaysPerWeek));

        // The lower bound includes the current Monday boundary and the upper bound
        // excludes future occurrences, including future entries in the partial week.
        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.BusinessId == business.Id
                && transaction.OccurredAtUtc >= windowStartUtc
                && transaction.OccurredAtUtc <= nowUtc)
            .Select(transaction => new
            {
                transaction.OccurredAtUtc,
                transaction.Type,
                transaction.AmountMinor
            })
            .ToListAsync(ct);

        var weekly = new WeeklyAccumulator[WeekCount];

        for (var index = 0; index < weekly.Length; index++)
        {
            weekly[index] = new WeeklyAccumulator(
                DateOnly.FromDateTime(windowStartLocal.AddDays(index * DaysPerWeek)));
        }

        var recordedDays = new HashSet<DateOnly>();

        foreach (var transaction in transactions)
        {
            var localWeekStart = LagosTime.GetWeekStartLocal(transaction.OccurredAtUtc);
            var weekIndex = (localWeekStart - windowStartLocal).Days / DaysPerWeek;

            if (weekIndex is < 0 or >= WeekCount)
            {
                throw new InvalidOperationException(
                    "A transaction was selected outside the reporting window.");
            }

            var localDate = DateOnly.FromDateTime(
                LagosTime.ToLocal(transaction.OccurredAtUtc));
            var bucket = weekly[weekIndex];

            checked
            {
                switch (transaction.Type)
                {
                    case TransactionType.Sale:
                        bucket.SalesMinor += transaction.AmountMinor;
                        break;
                    case TransactionType.Expense:
                        bucket.ExpensesMinor += transaction.AmountMinor;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported transaction type '{transaction.Type}'.");
                }

                bucket.TransactionCount++;
            }

            bucket.RecordedDays.Add(localDate);
            recordedDays.Add(localDate);
        }

        long totalSalesMinor = 0;
        long totalExpensesMinor = 0;
        var transactionCount = 0;
        var activeWeeks = 0;
        var firstActiveWeekIndex = -1;

        for (var index = 0; index < weekly.Length; index++)
        {
            var bucket = weekly[index];

            checked
            {
                totalSalesMinor += bucket.SalesMinor;
                totalExpensesMinor += bucket.ExpensesMinor;
                transactionCount += bucket.TransactionCount;
            }

            if (bucket.TransactionCount > 0)
            {
                activeWeeks++;

                if (firstActiveWeekIndex < 0)
                {
                    firstActiveWeekIndex = index;
                }
            }
        }

        var historySpanWeeks = firstActiveWeekIndex < 0
            ? 0
            : WeekCount - firstActiveWeekIndex;
        var activeDays = recordedDays.Count;

        var weeklySummaries = weekly
            .Select(bucket => new WeeklySummary
            {
                WeekStartLocalDate = bucket.WeekStartLocalDate,
                SalesMinor = bucket.SalesMinor,
                ExpensesMinor = bucket.ExpensesMinor,
                NetCashFlowMinor = checked(bucket.SalesMinor - bucket.ExpensesMinor),
                TransactionCount = bucket.TransactionCount,
                RecordedDays = bucket.RecordedDays.Count
            })
            .ToArray();

        return Result<BusinessSummaryResponse>.Success(new BusinessSummaryResponse
        {
            BusinessId = business.Id,
            BusinessName = business.Name,
            Currency = business.Currency,
            GeneratedAtUtc = nowUtc,
            WindowStartUtc = windowStartUtc,
            WindowEndExclusiveUtc = windowEndExclusiveUtc,
            TotalSalesMinor = totalSalesMinor,
            TotalExpensesMinor = totalExpensesMinor,
            NetCashFlowMinor = checked(totalSalesMinor - totalExpensesMinor),
            TransactionCount = transactionCount,
            Weekly = weeklySummaries,
            Indicator = ConsistencyIndicatorCalculator.Create(
                activeDays,
                activeWeeks,
                historySpanWeeks,
                transactionCount > 0)
        });
    }

    private async Task<Result<PeriodStatisticsResponse>> BuildPeriodStatisticsAsync(
        Business business,
        DateOnly? fromLocalDate,
        DateOnly? toLocalDate,
        string? groupBy,
        CancellationToken ct)
    {
        var nowUtc = TruncateToMilliseconds(_timeProvider.GetUtcNow().UtcDateTime);
        var todayLocalDate = DateOnly.FromDateTime(LagosTime.ToLocal(nowUtc));
        var criteriaResult = ValidateStatisticsQuery(
            fromLocalDate,
            toLocalDate,
            groupBy,
            todayLocalDate);

        if (!criteriaResult.IsSuccess)
        {
            return Result<PeriodStatisticsResponse>.Failure(criteriaResult.Error);
        }

        var criteria = criteriaResult.Value;
        var windowStartUtc = LagosTime.ToUtc(
            criteria.FromLocalDate.ToDateTime(TimeOnly.MinValue));
        var windowEndExclusiveUtc = LagosTime.ToUtc(
            criteria.ToLocalDate.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.BusinessId == business.Id
                && transaction.OccurredAtUtc >= windowStartUtc
                && transaction.OccurredAtUtc < windowEndExclusiveUtc)
            .Select(transaction => new
            {
                transaction.OccurredAtUtc,
                transaction.Type,
                transaction.AmountMinor
            })
            .ToListAsync(ct);

        var earliestAvailableUtc = await _db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.BusinessId == business.Id)
            .Select(transaction => (DateTime?)transaction.OccurredAtUtc)
            .MinAsync(ct);

        var latestAvailableUtc = await _db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.BusinessId == business.Id)
            .Select(transaction => (DateTime?)transaction.OccurredAtUtc)
            .MaxAsync(ct);

        var groups = CreateStatisticsGroups(
            criteria.FromLocalDate,
            criteria.ToLocalDate,
            criteria.GroupBy);
        var recordedDays = new HashSet<DateOnly>();

        foreach (var transaction in transactions)
        {
            var localDate = DateOnly.FromDateTime(
                LagosTime.ToLocal(transaction.OccurredAtUtc));
            var groupIndex = GetStatisticsGroupIndex(
                localDate,
                criteria.FromLocalDate,
                criteria.GroupBy);

            if (groupIndex < 0 || groupIndex >= groups.Count)
            {
                throw new InvalidOperationException(
                    "A transaction was selected outside the requested statistics range.");
            }

            var group = groups[groupIndex];

            checked
            {
                switch (transaction.Type)
                {
                    case TransactionType.Sale:
                        group.SalesMinor += transaction.AmountMinor;
                        group.SaleCount++;
                        break;
                    case TransactionType.Expense:
                        group.ExpensesMinor += transaction.AmountMinor;
                        group.ExpenseCount++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported transaction type '{transaction.Type}'.");
                }
            }

            group.RecordedDays.Add(localDate);
            recordedDays.Add(localDate);
        }

        long totalSalesMinor = 0;
        long totalExpensesMinor = 0;
        var saleCount = 0;
        var expenseCount = 0;

        foreach (var group in groups)
        {
            checked
            {
                totalSalesMinor += group.SalesMinor;
                totalExpensesMinor += group.ExpensesMinor;
                saleCount += group.SaleCount;
                expenseCount += group.ExpenseCount;
            }
        }

        var groupedStatistics = groups
            .Select(group => new PeriodStatisticsGroup
            {
                PeriodStartLocalDate = group.PeriodStartLocalDate,
                PeriodEndLocalDate = group.PeriodEndLocalDate,
                SalesMinor = group.SalesMinor,
                ExpensesMinor = group.ExpensesMinor,
                NetCashFlowMinor = checked(group.SalesMinor - group.ExpensesMinor),
                SaleCount = group.SaleCount,
                ExpenseCount = group.ExpenseCount,
                RecordedActivityDays = group.RecordedDays.Count
            })
            .ToArray();

        return Result<PeriodStatisticsResponse>.Success(new PeriodStatisticsResponse
        {
            BusinessId = business.Id,
            BusinessName = business.Name,
            Currency = business.Currency,
            RequestedFromLocalDate = criteria.FromLocalDate,
            RequestedToLocalDate = criteria.ToLocalDate,
            GroupBy = criteria.GroupBy,
            GeneratedAtUtc = nowUtc,
            WindowStartUtc = windowStartUtc,
            WindowEndExclusiveUtc = windowEndExclusiveUtc,
            EarliestAvailableLocalDate = earliestAvailableUtc is null
                ? null
                : DateOnly.FromDateTime(LagosTime.ToLocal(earliestAvailableUtc.Value)),
            LatestAvailableLocalDate = latestAvailableUtc is null
                ? null
                : DateOnly.FromDateTime(LagosTime.ToLocal(latestAvailableUtc.Value)),
            TotalSalesMinor = totalSalesMinor,
            TotalExpensesMinor = totalExpensesMinor,
            NetCashFlowMinor = checked(totalSalesMinor - totalExpensesMinor),
            SaleCount = saleCount,
            ExpenseCount = expenseCount,
            RecordedActivityDays = recordedDays.Count,
            Groups = groupedStatistics
        });
    }

    private static Result<StatisticsCriteria> ValidateStatisticsQuery(
        DateOnly? fromLocalDate,
        DateOnly? toLocalDate,
        string? groupBy,
        DateOnly todayLocalDate)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (fromLocalDate is null)
        {
            errors["from"] = ["A from date is required."];
        }

        if (toLocalDate is null)
        {
            errors["to"] = ["A to date is required."];
        }

        var parsedGroupBy = ParseStatisticsGroupBy(groupBy);

        if (parsedGroupBy is null)
        {
            errors["groupBy"] = ["groupBy must be day, week, or month."];
        }

        if (fromLocalDate is not null && toLocalDate is not null)
        {
            if (fromLocalDate.Value > toLocalDate.Value)
            {
                errors["to"] = ["to must be on or after from."];
            }
            else
            {
                if (toLocalDate.Value > todayLocalDate)
                {
                    errors["to"] = ["to must not be in the future."];
                }

                var inclusiveDayCount = toLocalDate.Value.DayNumber
                    - fromLocalDate.Value.DayNumber
                    + 1;

                if (inclusiveDayCount > MaximumStatisticsRangeDays)
                {
                    errors["to"] =
                    [
                        $"The requested range must not exceed {MaximumStatisticsRangeDays} calendar days."
                    ];
                }
            }
        }

        if (errors.Count > 0)
        {
            return Result<StatisticsCriteria>.Failure(
                Error.Validation(
                    "One or more statistics query parameters are invalid.",
                    errors));
        }

        return Result<StatisticsCriteria>.Success(
            new StatisticsCriteria(
                fromLocalDate!.Value,
                toLocalDate!.Value,
                parsedGroupBy!.Value));
    }

    private static StatisticsGroupBy? ParseStatisticsGroupBy(string? groupBy)
    {
        if (string.IsNullOrWhiteSpace(groupBy))
        {
            return null;
        }

        return groupBy.Trim().ToLowerInvariant() switch
        {
            "day" => StatisticsGroupBy.Day,
            "week" => StatisticsGroupBy.Week,
            "month" => StatisticsGroupBy.Month,
            _ => null
        };
    }

    private static List<StatisticsGroupAccumulator> CreateStatisticsGroups(
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        StatisticsGroupBy groupBy)
    {
        var groups = new List<StatisticsGroupAccumulator>();

        switch (groupBy)
        {
            case StatisticsGroupBy.Day:
                for (var date = fromLocalDate;
                     date <= toLocalDate;
                     date = date.AddDays(1))
                {
                    groups.Add(new StatisticsGroupAccumulator(date, date));
                }

                break;

            case StatisticsGroupBy.Week:
                var firstWeekStart = GetWeekStartLocalDate(fromLocalDate);
                var lastWeekStart = GetWeekStartLocalDate(toLocalDate);

                for (var weekStart = firstWeekStart;
                     weekStart <= lastWeekStart;
                     weekStart = weekStart.AddDays(DaysPerWeek))
                {
                    var periodStart = weekStart < fromLocalDate
                        ? fromLocalDate
                        : weekStart;
                    var weekEnd = weekStart.AddDays(DaysPerWeek - 1);
                    var periodEnd = weekEnd > toLocalDate
                        ? toLocalDate
                        : weekEnd;

                    groups.Add(new StatisticsGroupAccumulator(periodStart, periodEnd));
                }

                break;

            case StatisticsGroupBy.Month:
                var firstMonthStart = new DateOnly(
                    fromLocalDate.Year,
                    fromLocalDate.Month,
                    1);
                var lastMonthStart = new DateOnly(
                    toLocalDate.Year,
                    toLocalDate.Month,
                    1);

                for (var monthStart = firstMonthStart;
                     monthStart <= lastMonthStart;
                     monthStart = monthStart.AddMonths(1))
                {
                    var periodStart = monthStart < fromLocalDate
                        ? fromLocalDate
                        : monthStart;
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    var periodEnd = monthEnd > toLocalDate
                        ? toLocalDate
                        : monthEnd;

                    groups.Add(new StatisticsGroupAccumulator(periodStart, periodEnd));
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported statistics grouping '{groupBy}'.");
        }

        return groups;
    }

    private static int GetStatisticsGroupIndex(
        DateOnly localDate,
        DateOnly fromLocalDate,
        StatisticsGroupBy groupBy)
    {
        return groupBy switch
        {
            StatisticsGroupBy.Day =>
                localDate.DayNumber - fromLocalDate.DayNumber,
            StatisticsGroupBy.Week =>
                (GetWeekStartLocalDate(localDate).DayNumber
                    - GetWeekStartLocalDate(fromLocalDate).DayNumber) / DaysPerWeek,
            StatisticsGroupBy.Month =>
                ((localDate.Year - fromLocalDate.Year) * 12)
                + localDate.Month
                - fromLocalDate.Month,
            _ => throw new InvalidOperationException(
                $"Unsupported statistics grouping '{groupBy}'.")
        };
    }

    private static DateOnly GetWeekStartLocalDate(DateOnly localDate)
    {
        var daysSinceMonday = ((int)localDate.DayOfWeek + 6) % 7;
        return localDate.AddDays(-daysSinceMonday);
    }

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

        return new DateTime(
            utcValue.Ticks - (utcValue.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }

    private sealed record StatisticsCriteria(
        DateOnly FromLocalDate,
        DateOnly ToLocalDate,
        StatisticsGroupBy GroupBy);

    private sealed class StatisticsGroupAccumulator
    {
        public StatisticsGroupAccumulator(
            DateOnly periodStartLocalDate,
            DateOnly periodEndLocalDate)
        {
            PeriodStartLocalDate = periodStartLocalDate;
            PeriodEndLocalDate = periodEndLocalDate;
        }

        public DateOnly PeriodStartLocalDate { get; }

        public DateOnly PeriodEndLocalDate { get; }

        public long SalesMinor { get; set; }

        public long ExpensesMinor { get; set; }

        public int SaleCount { get; set; }

        public int ExpenseCount { get; set; }

        public HashSet<DateOnly> RecordedDays { get; } = [];
    }

    private sealed class WeeklyAccumulator
    {
        public WeeklyAccumulator(DateOnly weekStartLocalDate)
        {
            WeekStartLocalDate = weekStartLocalDate;
        }

        public DateOnly WeekStartLocalDate { get; }

        public long SalesMinor { get; set; }

        public long ExpensesMinor { get; set; }

        public int TransactionCount { get; set; }

        public HashSet<DateOnly> RecordedDays { get; } = [];
    }
}

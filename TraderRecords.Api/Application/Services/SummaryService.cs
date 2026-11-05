using Microsoft.EntityFrameworkCore;
using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Summary;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;

namespace TraderRecords.Api.Application.Services;

/// <summary>
/// Computes the fixed eight-week Lagos summary from persisted transactions. The
/// reporting window is based on occurrence time, never receipt time.
/// </summary>
public sealed class SummaryService : ISummaryService
{
    private const int WeekCount = 8;
    private const int DaysPerWeek = 7;

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
        // Ownership always comes from the validated subject claim, never from a
        // caller-supplied business ID.
        var business = await _db.Businesses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OwnerUserId == traderUserId,
                ct);

        if (business is null)
        {
            return Result<BusinessSummaryResponse>.Failure(
                Error.NotFound(
                    "BUSINESS_NOT_FOUND",
                    "The trader's business was not found."));
        }

        return await BuildSummaryAsync(business, ct);
    }

    public async Task<Result<BusinessSummaryResponse>> GetForLenderAsync(
        Guid lenderUserId,
        Guid businessId,
        CancellationToken ct)
    {
        if (businessId == Guid.Empty)
        {
            return Result<BusinessSummaryResponse>.Failure(
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
            return Result<BusinessSummaryResponse>.Failure(
                Error.NotFound("BUSINESS_NOT_FOUND", "The business was not found."));
        }

        // Consent is read from the database for every request. The business ID and
        // lender ID must match the same active row; another lender's consent never
        // grants access.
        var hasActiveConsent = await _db.Consents
            .AsNoTracking()
            .AnyAsync(
                consent => consent.BusinessId == businessId
                    && consent.LenderUserId == lenderUserId
                    && consent.RevokedAtUtc == null,
                ct);

        if (!hasActiveConsent)
        {
            return Result<BusinessSummaryResponse>.Failure(
                Error.Forbidden(
                    "CONSENT_REQUIRED",
                    "Active consent is required."));
        }

        return await BuildSummaryAsync(business, ct);
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

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

        return new DateTime(
            utcValue.Ticks - (utcValue.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
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

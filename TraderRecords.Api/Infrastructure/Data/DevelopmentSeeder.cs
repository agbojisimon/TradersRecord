using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;

namespace TraderRecords.Api.Infrastructure.Data;

/// <summary>
/// Development-only, idempotent demo data. It inserts only missing identities and
/// transactions; it never overwrites existing records or grants consent.
/// </summary>
public sealed class DevelopmentSeeder
{
    private const string TraderOnePassword = "Trader1!Demo2026";
    private const string TraderTwoPassword = "Trader2!Demo2026";
    private const string LenderPassword = "Lender!Demo2026";

    private static readonly Guid TraderOneId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TraderTwoId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid LenderId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid BusinessOneId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid BusinessTwoId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DevelopmentSeeder> _logger;

    public DevelopmentSeeder(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        TimeProvider timeProvider,
        ILogger<DevelopmentSeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var traderOne = await EnsureUserAsync(
            TraderOneId,
            "Trader One",
            "trader1@example.test",
            UserRole.Trader,
            TraderOnePassword,
            nowUtc,
            ct);

        var traderTwo = await EnsureUserAsync(
            TraderTwoId,
            "Trader Two",
            "trader2@example.test",
            UserRole.Trader,
            TraderTwoPassword,
            nowUtc,
            ct);

        await EnsureUserAsync(
            LenderId,
            "Demo Lender",
            "lender@example.test",
            UserRole.Lender,
            LenderPassword,
            nowUtc,
            ct);

        var businessOne = await EnsureBusinessAsync(
            BusinessOneId,
            traderOne,
            "Trader One Stores",
            nowUtc,
            ct);

        var businessTwo = await EnsureBusinessAsync(
            BusinessTwoId,
            traderTwo,
            "Trader Two Supplies",
            nowUtc,
            ct);

        var addedTransactions = 0;
        addedTransactions += await EnsureTransactionsAsync(
            businessOne,
            businessOrdinal: 1,
            seedBothTypesEveryWeek: true,
            nowUtc,
            ct);

        addedTransactions += await EnsureTransactionsAsync(
            businessTwo,
            businessOrdinal: 2,
            seedBothTypesEveryWeek: false,
            nowUtc,
            ct);

        if (addedTransactions > 0)
        {
            _logger.LogInformation(
                "Development seed added {TransactionCount} transactions.",
                addedTransactions);
        }
        else
        {
            _logger.LogInformation(
                "Development seed data already present; no transactions added.");
        }
    }

    private async Task<User> EnsureUserAsync(
        Guid id,
        string fullName,
        string email,
        UserRole role,
        string password,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var normalizedEmail = email.ToUpperInvariant();

        // Match either the deterministic identity or the unique email so a
        // previously created account is never overwritten or duplicated.
        var existing = await _db.Users.FirstOrDefaultAsync(
            user => user.Id == id || user.NormalizedEmail == normalizedEmail,
            ct);

        if (existing is not null)
        {
            return existing;
        }

        var user = new User
        {
            Id = id,
            FullName = fullName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            Role = role,
            CreatedAtUtc = nowUtc
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    private async Task<Business> EnsureBusinessAsync(
        Guid id,
        User owner,
        string name,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var existing = await _db.Businesses.FirstOrDefaultAsync(
            business => business.Id == id || business.OwnerUserId == owner.Id,
            ct);

        if (existing is not null)
        {
            return existing;
        }

        var business = new Business
        {
            Id = id,
            OwnerUserId = owner.Id,
            Name = name,
            Currency = "NGN",
            CreatedAtUtc = nowUtc
        };

        _db.Businesses.Add(business);
        await _db.SaveChangesAsync(ct);
        return business;
    }

    private async Task<int> EnsureTransactionsAsync(
        Business business,
        int businessOrdinal,
        bool seedBothTypesEveryWeek,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var existingClientIds = (await _db.Transactions
                .Where(transaction => transaction.BusinessId == business.Id)
                .Select(transaction => transaction.ClientId)
                .ToListAsync(ct))
            .ToHashSet();

        var currentWeekStartLocal = LagosTime.GetWeekStartLocal(nowUtc);
        var newTransactions = new List<Transaction>();

        // Seed the current Lagos week plus the seven preceding weeks. Week zero
        // uses "now" so a current-week entry can never be in the future.
        for (var weekOffset = 0; weekOffset < 8; weekOffset++)
        {
            if (seedBothTypesEveryWeek || weekOffset % 2 == 0)
            {
                var saleClientId = CreateSeedClientId(businessOrdinal, weekOffset, kind: 0);
                if (!existingClientIds.Contains(saleClientId))
                {
                    newTransactions.Add(CreateSeedTransaction(
                        business.Id,
                        saleClientId,
                        TransactionType.Sale,
                        amountMinor: 4_500_000 + (weekOffset * 100_000),
                        note: $"Seed sale week {weekOffset + 1}",
                        occurredAtUtc: GetOccurrenceUtc(nowUtc, currentWeekStartLocal, weekOffset, hourLocal: 9),
                        receivedAtUtc: nowUtc));
                }
            }

            if (seedBothTypesEveryWeek || weekOffset % 2 == 1)
            {
                var expenseClientId = CreateSeedClientId(businessOrdinal, weekOffset, kind: 1);
                if (!existingClientIds.Contains(expenseClientId))
                {
                    newTransactions.Add(CreateSeedTransaction(
                        business.Id,
                        expenseClientId,
                        TransactionType.Expense,
                        amountMinor: 1_200_000 + (weekOffset * 50_000),
                        note: $"Seed expense week {weekOffset + 1}",
                        occurredAtUtc: GetOccurrenceUtc(nowUtc, currentWeekStartLocal, weekOffset, hourLocal: 15),
                        receivedAtUtc: nowUtc));
                }
            }
        }

        if (newTransactions.Count == 0)
        {
            return 0;
        }

        _db.Transactions.AddRange(newTransactions);
        await _db.SaveChangesAsync(ct);
        return newTransactions.Count;
    }

    private static Transaction CreateSeedTransaction(
        Guid businessId,
        Guid clientId,
        TransactionType type,
        long amountMinor,
        string note,
        DateTime occurredAtUtc,
        DateTime receivedAtUtc)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            ClientId = clientId,
            Type = type,
            AmountMinor = amountMinor,
            Note = note,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = receivedAtUtc
        };
    }

    private static Guid CreateSeedClientId(int businessOrdinal, int weekOffset, int kind)
    {
        // Deterministic, readable client IDs: business 1 -> 1xx, business 2 -> 2xx.
        var serial = (businessOrdinal * 100) + (weekOffset * 2) + kind + 1;
        return Guid.Parse($"10000000-0000-4000-8000-{serial:D12}");
    }

    private static DateTime GetOccurrenceUtc(
        DateTime nowUtc,
        DateTime currentWeekStartLocal,
        int weekOffset,
        int hourLocal)
    {
        if (weekOffset == 0)
        {
            return nowUtc;
        }

        var localOccurrence = currentWeekStartLocal
            .AddDays(-7 * weekOffset)
            .AddHours(hourLocal);

        return LagosTime.ToUtc(localOccurrence);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TraderRecords.Api.Application.DTOs.Auth;
using TraderRecords.Api.Application.DTOs.Summary;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Application.Services;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;
using Xunit;

namespace TraderRecords.Tests;

public sealed class SummaryTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private TestApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestApiFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task EmptyHistorySummary_ReturnsEightZeroBucketsAndZeroIndicator()
    {
        var (client, auth) = await CreateTraderAsync();

        var response = await client.GetAsync("/api/businesses/me/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await ReadSummaryAsync(response);
        var nowUtc = TestApiFactory.FixedNow.UtcDateTime;
        var currentWeekStartLocal = LagosTime.GetWeekStartLocal(nowUtc);
        var windowStartLocal = currentWeekStartLocal.AddDays(-49);

        Assert.Equal(auth.BusinessId, summary.BusinessId);
        Assert.Equal("Test Business", summary.BusinessName);
        Assert.Equal("NGN", summary.Currency);
        Assert.Equal(nowUtc, summary.GeneratedAtUtc);
        Assert.Equal(LagosTime.ToUtc(windowStartLocal), summary.WindowStartUtc);
        Assert.Equal(
            LagosTime.ToUtc(currentWeekStartLocal.AddDays(7)),
            summary.WindowEndExclusiveUtc);

        Assert.Equal(8, summary.Weekly.Count);
        Assert.Equal(
            DateOnly.FromDateTime(windowStartLocal),
            summary.Weekly[0].WeekStartLocalDate);
        Assert.Equal(
            DateOnly.FromDateTime(currentWeekStartLocal),
            summary.Weekly[7].WeekStartLocalDate);

        Assert.All(summary.Weekly, week =>
        {
            Assert.Equal(0, week.SalesMinor);
            Assert.Equal(0, week.ExpensesMinor);
            Assert.Equal(0, week.NetCashFlowMinor);
            Assert.Equal(0, week.TransactionCount);
            Assert.Equal(0, week.RecordedDays);
        });

        Assert.Equal(0, summary.TotalSalesMinor);
        Assert.Equal(0, summary.TotalExpensesMinor);
        Assert.Equal(0, summary.NetCashFlowMinor);
        Assert.Equal(0, summary.TransactionCount);
        Assert.Equal(0, summary.Indicator.Score);
        Assert.Equal(0, summary.Indicator.ActiveDays);
        Assert.Equal(0, summary.Indicator.ActiveWeeks);
        Assert.Equal(0, summary.Indicator.HistorySpanWeeks);
        Assert.Equal(
            ConsistencyIndicatorCalculator.Disclaimer,
            summary.Indicator.Disclaimer);
        Assert.Contains(
            summary.Indicator.Guidance,
            guidance => guidance.Contains(
                "Start recording",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LagosWeekBoundarySummary_ComputesExactTotalsAndIndicator()
    {
        var (client, auth) = await CreateTraderAsync();
        var businessId = auth.BusinessId!.Value;
        var nowUtc = TestApiFactory.FixedNow.UtcDateTime;
        var currentWeekStartLocal = LagosTime.GetWeekStartLocal(nowUtc);
        var windowStartLocal = currentWeekStartLocal.AddDays(-49);
        var windowStartUtc = LagosTime.ToUtc(windowStartLocal);
        var weekFourStartLocal = windowStartLocal.AddDays(28);
        var weekFourOccurrenceUtc = LagosTime.ToUtc(
            weekFourStartLocal.AddHours(12));

        await AddTransactionsAsync(
            businessId,
            (windowStartUtc.AddMilliseconds(-1), TransactionType.Sale, 9_000_000),
            (windowStartUtc, TransactionType.Sale, 1_000_000),
            (windowStartUtc.AddMinutes(30), TransactionType.Expense, 200_000),
            (weekFourOccurrenceUtc, TransactionType.Sale, 2_000_000),
            (nowUtc, TransactionType.Sale, 3_000_000),
            (nowUtc, TransactionType.Expense, 500_000),
            (nowUtc.AddMinutes(1), TransactionType.Sale, 7_000_000));

        var response = await client.GetAsync("/api/businesses/me/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await ReadSummaryAsync(response);

        Assert.Equal(6_000_000, summary.TotalSalesMinor);
        Assert.Equal(700_000, summary.TotalExpensesMinor);
        Assert.Equal(5_300_000, summary.NetCashFlowMinor);
        Assert.Equal(5, summary.TransactionCount);

        var firstWeek = summary.Weekly[0];
        Assert.Equal(
            DateOnly.FromDateTime(windowStartLocal),
            firstWeek.WeekStartLocalDate);
        Assert.Equal(1_000_000, firstWeek.SalesMinor);
        Assert.Equal(200_000, firstWeek.ExpensesMinor);
        Assert.Equal(800_000, firstWeek.NetCashFlowMinor);
        Assert.Equal(2, firstWeek.TransactionCount);
        Assert.Equal(1, firstWeek.RecordedDays);

        var fourthWeek = summary.Weekly[4];
        Assert.Equal(
            DateOnly.FromDateTime(weekFourStartLocal),
            fourthWeek.WeekStartLocalDate);
        Assert.Equal(2_000_000, fourthWeek.SalesMinor);
        Assert.Equal(0, fourthWeek.ExpensesMinor);
        Assert.Equal(2_000_000, fourthWeek.NetCashFlowMinor);
        Assert.Equal(1, fourthWeek.TransactionCount);
        Assert.Equal(1, fourthWeek.RecordedDays);

        var currentWeek = summary.Weekly[7];
        Assert.Equal(
            DateOnly.FromDateTime(currentWeekStartLocal),
            currentWeek.WeekStartLocalDate);
        Assert.Equal(3_000_000, currentWeek.SalesMinor);
        Assert.Equal(500_000, currentWeek.ExpensesMinor);
        Assert.Equal(2_500_000, currentWeek.NetCashFlowMinor);
        Assert.Equal(2, currentWeek.TransactionCount);
        Assert.Equal(1, currentWeek.RecordedDays);

        Assert.Equal(3, summary.Indicator.ActiveDays);
        Assert.Equal(3, summary.Indicator.ActiveWeeks);
        Assert.Equal(8, summary.Indicator.HistorySpanWeeks);
        Assert.Equal(34, summary.Indicator.Score);
    }

    [Fact]
    public async Task SummaryIndicator_MatchesSpecificationExample()
    {
        var (client, auth) = await CreateTraderAsync();
        var businessId = auth.BusinessId!.Value;
        var nowUtc = TestApiFactory.FixedNow.UtcDateTime;
        var currentWeekStartLocal = LagosTime.GetWeekStartLocal(nowUtc);
        var windowStartLocal = currentWeekStartLocal.AddDays(-49);

        var transactions = new List<(DateTime, TransactionType, long)>();

        // 4 active days in week index 4, 4 in week 5, 3 in week 6, and the
        // current partial week contributes one more day.
        foreach (var dayOffset in new[] { 0, 1, 2, 3 })
        {
            transactions.Add((
                LagosTime.ToUtc(windowStartLocal.AddDays(28 + dayOffset).AddHours(12)),
                TransactionType.Sale,
                100_000));
        }

        foreach (var dayOffset in new[] { 0, 1, 2, 3 })
        {
            transactions.Add((
                LagosTime.ToUtc(windowStartLocal.AddDays(35 + dayOffset).AddHours(12)),
                TransactionType.Sale,
                100_000));
        }

        foreach (var dayOffset in new[] { 0, 1, 2 })
        {
            transactions.Add((
                LagosTime.ToUtc(windowStartLocal.AddDays(42 + dayOffset).AddHours(12)),
                TransactionType.Sale,
                100_000));
        }

        transactions.Add((nowUtc, TransactionType.Sale, 100_000));

        await AddTransactionsAsync(businessId, transactions.ToArray());

        var response = await client.GetAsync("/api/businesses/me/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await ReadSummaryAsync(response);

        Assert.Equal(12, summary.Indicator.ActiveDays);
        Assert.Equal(4, summary.Indicator.ActiveWeeks);
        Assert.Equal(4, summary.Indicator.HistorySpanWeeks);
        Assert.Equal(36, summary.Indicator.Score);
    }

    [Fact]
    public async Task Summary_DoesNotIncludeAnotherTradersTransactions()
    {
        var (firstClient, firstAuth) = await CreateTraderAsync();
        var (secondClient, _) = await CreateTraderAsync();
        var nowUtc = TestApiFactory.FixedNow.UtcDateTime;

        await AddTransactionsAsync(
            firstAuth.BusinessId!.Value,
            (nowUtc.AddDays(-1), TransactionType.Sale, 1_000_000));

        var firstSummary = await ReadSummaryAsync(
            await firstClient.GetAsync("/api/businesses/me/summary"));
        Assert.Equal(1_000_000, firstSummary.TotalSalesMinor);
        Assert.Equal(1, firstSummary.TransactionCount);

        // /me resolves ownership from the subject claim. A second trader must see
        // their own empty summary, never the first trader's rows.
        var secondSummary = await ReadSummaryAsync(
            await secondClient.GetAsync("/api/businesses/me/summary"));
        Assert.Equal(0, secondSummary.TotalSalesMinor);
        Assert.Equal(0, secondSummary.TotalExpensesMinor);
        Assert.Equal(0, secondSummary.TransactionCount);
        Assert.All(
            secondSummary.Weekly,
            week => Assert.Equal(0, week.TransactionCount));
    }

    [Fact]
    public void SummaryIndicatorCalculator_ReturnsZeroForEmptyAndHundredForFullCoverage()
    {
        var empty = ConsistencyIndicatorCalculator.Create(
            activeDays: 0,
            activeWeeks: 0,
            historySpanWeeks: 0,
            hasTransactions: false);

        Assert.Equal(0, empty.Score);
        Assert.Contains(
            empty.Guidance,
            guidance => guidance.Contains(
                "Start recording",
                StringComparison.OrdinalIgnoreCase));

        var full = ConsistencyIndicatorCalculator.Create(
            activeDays: 56,
            activeWeeks: 8,
            historySpanWeeks: 8,
            hasTransactions: true);

        Assert.Equal(100, full.Score);
        Assert.Empty(full.Guidance);
        Assert.Equal(
            ConsistencyIndicatorCalculator.Disclaimer,
            full.Disclaimer);

        Assert.Equal(36, ConsistencyIndicatorCalculator.CalculateScore(12, 4, 4));
    }

    [Fact]
    public async Task Summary_RequiresTraderRoleAndExistingBusiness()
    {
        using var anonymousClient = _factory.CreateClient();

        var unauthorized = await anonymousClient.GetAsync("/api/businesses/me/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ReadProblemCodeAsync(unauthorized));

        var lenderToken = await CreateTokenAsync(UserRole.Lender, createBusiness: false);
        using var lenderClient = CreateClient(lenderToken);

        var forbidden = await lenderClient.GetAsync("/api/businesses/me/summary");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("FORBIDDEN", await ReadProblemCodeAsync(forbidden));

        var traderWithoutBusinessToken = await CreateTokenAsync(
            UserRole.Trader,
            createBusiness: false);
        using var traderWithoutBusinessClient = CreateClient(
            traderWithoutBusinessToken);

        var notFound = await traderWithoutBusinessClient.GetAsync(
            "/api/businesses/me/summary");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("BUSINESS_NOT_FOUND", await ReadProblemCodeAsync(notFound));
    }

    private async Task<(HttpClient Client, AuthResponse Auth)> CreateTraderAsync()
    {
        var client = _factory.CreateClient();
        var email = $"trader-{Guid.NewGuid():N}@example.test";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                fullName = "Test Trader",
                email,
                password = "Trader!Demo2026",
                businessName = "Test Business"
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return (client, auth);
    }

    private async Task<string> CreateTokenAsync(UserRole role, bool createBusiness)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var suffix = Guid.NewGuid().ToString("N");

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = $"{role} Test",
            Email = $"{role.ToString().ToLowerInvariant()}-{suffix}@example.test",
            NormalizedEmail = $"{role.ToString().ToUpperInvariant()}-{suffix}@EXAMPLE.TEST",
            Role = role,
            CreatedAtUtc = TestApiFactory.FixedNow.UtcDateTime
        };

        user.PasswordHash = passwordHasher.HashPassword(user, "DemoPassword!2026");
        db.Users.Add(user);

        Guid? businessId = null;

        if (createBusiness)
        {
            var business = new Business
            {
                Id = Guid.NewGuid(),
                OwnerUserId = user.Id,
                Name = "Test Business",
                Currency = "NGN",
                CreatedAtUtc = TestApiFactory.FixedNow.UtcDateTime
            };

            db.Businesses.Add(business);
            businessId = business.Id;
        }

        await db.SaveChangesAsync();

        return tokenService.Create(user, businessId).AccessToken;
    }

    private HttpClient CreateClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task AddTransactionsAsync(
        Guid businessId,
        params (DateTime OccurredAtUtc, TransactionType Type, long AmountMinor)[] transactions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var transaction in transactions)
        {
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                ClientId = Guid.NewGuid(),
                Type = transaction.Type,
                AmountMinor = transaction.AmountMinor,
                Note = null,
                OccurredAtUtc = DateTime.SpecifyKind(
                    transaction.OccurredAtUtc,
                    DateTimeKind.Utc),
                ReceivedAtUtc = TestApiFactory.FixedNow.UtcDateTime
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<BusinessSummaryResponse> ReadSummaryAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<BusinessSummaryResponse>(
            JsonOptions);

        Assert.NotNull(body);
        return body!;
    }

    private static async Task<string?> ReadProblemCodeAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }
}

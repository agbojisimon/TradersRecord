using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TraderRecords.Api.Application.DTOs.Auth;
using TraderRecords.Api.Application.DTOs.Statistics;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;
using Xunit;

namespace TraderRecords.Tests;

public sealed class PeriodStatisticsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
    public async Task DayStatistics_ClipsLagosBoundariesAndReturnsExactTotals()
    {
        var (client, auth) = await CreateTraderAsync();
        var businessId = auth.BusinessId!.Value;
        var rangeStartUtc = LagosTime.ToUtc(new DateTime(2026, 1, 1, 0, 0, 0));
        var rangeEndExclusiveUtc = LagosTime.ToUtc(new DateTime(2026, 1, 4, 0, 0, 0));

        await AddTransactionsAsync(
            businessId,
            (rangeStartUtc.AddMilliseconds(-1), TransactionType.Sale, 9_000_000),
            (rangeStartUtc, TransactionType.Sale, 1_000_000),
            (rangeStartUtc.AddMinutes(30), TransactionType.Expense, 200_000),
            (LagosTime.ToUtc(new DateTime(2026, 1, 2, 12, 0, 0)),
                TransactionType.Sale, 2_000_000),
            (LagosTime.ToUtc(new DateTime(2026, 1, 3, 12, 0, 0)),
                TransactionType.Expense, 500_000),
            (rangeEndExclusiveUtc, TransactionType.Sale, 7_000_000));

        var response = await client.GetAsync(
            "/api/businesses/me/statistics?from=2026-01-01&to=2026-01-03&groupBy=day");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statistics = await ReadStatisticsAsync(response);

        Assert.Equal(businessId, statistics.BusinessId);
        Assert.Equal("Test Business", statistics.BusinessName);
        Assert.Equal("NGN", statistics.Currency);
        Assert.Equal(new DateOnly(2026, 1, 1), statistics.RequestedFromLocalDate);
        Assert.Equal(new DateOnly(2026, 1, 3), statistics.RequestedToLocalDate);
        Assert.Equal(StatisticsGroupBy.Day, statistics.GroupBy);
        Assert.Equal(TestApiFactory.FixedNow.UtcDateTime, statistics.GeneratedAtUtc);
        Assert.Equal(rangeStartUtc, statistics.WindowStartUtc);
        Assert.Equal(rangeEndExclusiveUtc, statistics.WindowEndExclusiveUtc);
        Assert.Equal(new DateOnly(2025, 12, 31), statistics.EarliestAvailableLocalDate);
        Assert.Equal(new DateOnly(2026, 1, 4), statistics.LatestAvailableLocalDate);

        Assert.Equal(3_000_000, statistics.TotalSalesMinor);
        Assert.Equal(700_000, statistics.TotalExpensesMinor);
        Assert.Equal(2_300_000, statistics.NetCashFlowMinor);
        Assert.Equal(2, statistics.SaleCount);
        Assert.Equal(2, statistics.ExpenseCount);
        Assert.Equal(3, statistics.RecordedActivityDays);
        Assert.Equal(
            PeriodStatisticsResponse.MissingRecordsWarning,
            statistics.Warning);

        Assert.Collection(
            statistics.Groups,
            first =>
            {
                Assert.Equal(new DateOnly(2026, 1, 1), first.PeriodStartLocalDate);
                Assert.Equal(new DateOnly(2026, 1, 1), first.PeriodEndLocalDate);
                Assert.Equal(1_000_000, first.SalesMinor);
                Assert.Equal(200_000, first.ExpensesMinor);
                Assert.Equal(800_000, first.NetCashFlowMinor);
                Assert.Equal(1, first.SaleCount);
                Assert.Equal(1, first.ExpenseCount);
                Assert.Equal(1, first.RecordedActivityDays);
            },
            second =>
            {
                Assert.Equal(new DateOnly(2026, 1, 2), second.PeriodStartLocalDate);
                Assert.Equal(new DateOnly(2026, 1, 2), second.PeriodEndLocalDate);
                Assert.Equal(2_000_000, second.SalesMinor);
                Assert.Equal(0, second.ExpensesMinor);
                Assert.Equal(2_000_000, second.NetCashFlowMinor);
                Assert.Equal(1, second.SaleCount);
                Assert.Equal(0, second.ExpenseCount);
                Assert.Equal(1, second.RecordedActivityDays);
            },
            third =>
            {
                Assert.Equal(new DateOnly(2026, 1, 3), third.PeriodStartLocalDate);
                Assert.Equal(new DateOnly(2026, 1, 3), third.PeriodEndLocalDate);
                Assert.Equal(0, third.SalesMinor);
                Assert.Equal(500_000, third.ExpensesMinor);
                Assert.Equal(-500_000, third.NetCashFlowMinor);
                Assert.Equal(0, third.SaleCount);
                Assert.Equal(1, third.ExpenseCount);
                Assert.Equal(1, third.RecordedActivityDays);
            });
    }

    [Fact]
    public async Task WeekStatistics_StartsOnMondayAndClipsFirstAndLastPeriods()
    {
        var (client, auth) = await CreateTraderAsync();
        var businessId = auth.BusinessId!.Value;

        await AddTransactionsAsync(
            businessId,
            (LagosTime.ToUtc(new DateTime(2025, 12, 30, 23, 59, 0)),
                TransactionType.Sale, 888_000),
            (LagosTime.ToUtc(new DateTime(2025, 12, 31, 12, 0, 0)),
                TransactionType.Sale, 100_000),
            (LagosTime.ToUtc(new DateTime(2026, 1, 5, 0, 0, 0)),
                TransactionType.Sale, 200_000),
            (LagosTime.ToUtc(new DateTime(2026, 1, 6, 23, 59, 0)),
                TransactionType.Expense, 50_000),
            (LagosTime.ToUtc(new DateTime(2026, 1, 7, 0, 0, 0)),
                TransactionType.Sale, 999_000));

        var response = await client.GetAsync(
            "/api/businesses/me/statistics?from=2025-12-31&to=2026-01-06&groupBy=week");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statistics = await ReadStatisticsAsync(response);

        Assert.Equal(StatisticsGroupBy.Week, statistics.GroupBy);
        Assert.Equal(300_000, statistics.TotalSalesMinor);
        Assert.Equal(50_000, statistics.TotalExpensesMinor);
        Assert.Equal(250_000, statistics.NetCashFlowMinor);
        Assert.Equal(2, statistics.SaleCount);
        Assert.Equal(1, statistics.ExpenseCount);
        Assert.Equal(3, statistics.RecordedActivityDays);
        Assert.Equal(new DateOnly(2025, 12, 30), statistics.EarliestAvailableLocalDate);
        Assert.Equal(new DateOnly(2026, 1, 7), statistics.LatestAvailableLocalDate);

        Assert.Collection(
            statistics.Groups,
            first =>
            {
                Assert.Equal(new DateOnly(2025, 12, 31), first.PeriodStartLocalDate);
                Assert.Equal(new DateOnly(2026, 1, 4), first.PeriodEndLocalDate);
                Assert.Equal(100_000, first.SalesMinor);
                Assert.Equal(0, first.ExpensesMinor);
                Assert.Equal(1, first.SaleCount);
                Assert.Equal(1, first.RecordedActivityDays);
            },
            second =>
            {
                Assert.Equal(new DateOnly(2026, 1, 5), second.PeriodStartLocalDate);
                Assert.Equal(new DateOnly(2026, 1, 6), second.PeriodEndLocalDate);
                Assert.Equal(200_000, second.SalesMinor);
                Assert.Equal(50_000, second.ExpensesMinor);
                Assert.Equal(150_000, second.NetCashFlowMinor);
                Assert.Equal(1, second.SaleCount);
                Assert.Equal(1, second.ExpenseCount);
                Assert.Equal(2, second.RecordedActivityDays);
            });
    }

    [Fact]
    public async Task MonthStatistics_ClipsPartialMonthsAndIncludesEmptyGroups()
    {
        var (client, auth) = await CreateTraderAsync();
        var businessId = auth.BusinessId!.Value;

        await AddTransactionsAsync(
            businessId,
            (LagosTime.ToUtc(new DateTime(2025, 11, 14, 23, 59, 0)),
                TransactionType.Sale, 999_000),
            (LagosTime.ToUtc(new DateTime(2025, 12, 15, 0, 0, 0)),
                TransactionType.Sale, 100_000),
            (LagosTime.ToUtc(new DateTime(2025, 12, 31, 23, 59, 0)),
                TransactionType.Expense, 20_000),
            (LagosTime.ToUtc(new DateTime(2026, 1, 1, 0, 0, 0)),
                TransactionType.Sale, 300_000),
            (LagosTime.ToUtc(new DateTime(2026, 1, 7, 12, 0, 0)),
                TransactionType.Expense, 30_000),
            (LagosTime.ToUtc(new DateTime(2026, 1, 8, 0, 0, 0)),
                TransactionType.Sale, 888_000));

        var response = await client.GetAsync(
            "/api/businesses/me/statistics?from=2025-11-15&to=2026-01-07&groupBy=month");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statistics = await ReadStatisticsAsync(response);

        Assert.Equal(StatisticsGroupBy.Month, statistics.GroupBy);
        Assert.Equal(400_000, statistics.TotalSalesMinor);
        Assert.Equal(50_000, statistics.TotalExpensesMinor);
        Assert.Equal(350_000, statistics.NetCashFlowMinor);
        Assert.Equal(2, statistics.SaleCount);
        Assert.Equal(2, statistics.ExpenseCount);
        Assert.Equal(4, statistics.RecordedActivityDays);
        Assert.Equal(new DateOnly(2025, 11, 14), statistics.EarliestAvailableLocalDate);
        Assert.Equal(new DateOnly(2026, 1, 8), statistics.LatestAvailableLocalDate);

        Assert.Collection(
            statistics.Groups,
            november =>
            {
                Assert.Equal(new DateOnly(2025, 11, 15), november.PeriodStartLocalDate);
                Assert.Equal(new DateOnly(2025, 11, 30), november.PeriodEndLocalDate);
                Assert.Equal(0, november.SalesMinor);
                Assert.Equal(0, november.ExpensesMinor);
                Assert.Equal(0, november.NetCashFlowMinor);
                Assert.Equal(0, november.SaleCount);
                Assert.Equal(0, november.ExpenseCount);
                Assert.Equal(0, november.RecordedActivityDays);
            },
            december =>
            {
                Assert.Equal(new DateOnly(2025, 12, 1), december.PeriodStartLocalDate);
                Assert.Equal(new DateOnly(2025, 12, 31), december.PeriodEndLocalDate);
                Assert.Equal(100_000, december.SalesMinor);
                Assert.Equal(20_000, december.ExpensesMinor);
                Assert.Equal(80_000, december.NetCashFlowMinor);
                Assert.Equal(1, december.SaleCount);
                Assert.Equal(1, december.ExpenseCount);
                Assert.Equal(2, december.RecordedActivityDays);
            },
            january =>
            {
                Assert.Equal(new DateOnly(2026, 1, 1), january.PeriodStartLocalDate);
                Assert.Equal(new DateOnly(2026, 1, 7), january.PeriodEndLocalDate);
                Assert.Equal(300_000, january.SalesMinor);
                Assert.Equal(30_000, january.ExpensesMinor);
                Assert.Equal(270_000, january.NetCashFlowMinor);
                Assert.Equal(1, january.SaleCount);
                Assert.Equal(1, january.ExpenseCount);
                Assert.Equal(2, january.RecordedActivityDays);
            });
    }

    [Fact]
    public async Task Statistics_RejectsMissingReversedFutureOversizedAndUnknownQueries()
    {
        var (client, _) = await CreateTraderAsync();

        var missing = await client.GetAsync("/api/businesses/me/statistics");
        await AssertValidationErrorAsync(missing, "from", "to", "groupBy");

        var reversed = await client.GetAsync(
            "/api/businesses/me/statistics?from=2026-01-07&to=2026-01-01&groupBy=day");
        await AssertValidationErrorAsync(reversed, "to");

        var future = await client.GetAsync(
            "/api/businesses/me/statistics?from=2026-01-01&to=2026-01-08&groupBy=day");
        await AssertValidationErrorAsync(future, "to");

        var oversized = await client.GetAsync(
            "/api/businesses/me/statistics?from=2025-01-01&to=2026-01-07&groupBy=month");
        await AssertValidationErrorAsync(oversized, "to");

        var maximumAllowed = await client.GetAsync(
            "/api/businesses/me/statistics?from=2025-01-07&to=2026-01-07&groupBy=month");
        Assert.Equal(HttpStatusCode.OK, maximumAllowed.StatusCode);

        var unknownGroup = await client.GetAsync(
            "/api/businesses/me/statistics?from=2026-01-01&to=2026-01-07&groupBy=year");
        await AssertValidationErrorAsync(unknownGroup, "groupBy");
    }

    [Fact]
    public async Task Statistics_DoesNotIncludeAnotherTradersTransactions()
    {
        var (firstClient, firstAuth) = await CreateTraderAsync();
        var (secondClient, _) = await CreateTraderAsync();

        await AddTransactionsAsync(
            firstAuth.BusinessId!.Value,
            (LagosTime.ToUtc(new DateTime(2026, 1, 2, 12, 0, 0)),
                TransactionType.Sale, 1_000_000));

        var firstStatistics = await ReadStatisticsAsync(
            await firstClient.GetAsync(
                "/api/businesses/me/statistics?from=2026-01-01&to=2026-01-07&groupBy=day"));
        Assert.Equal(1_000_000, firstStatistics.TotalSalesMinor);
        Assert.Equal(1, firstStatistics.SaleCount);

        var secondStatistics = await ReadStatisticsAsync(
            await secondClient.GetAsync(
                "/api/businesses/me/statistics?from=2026-01-01&to=2026-01-07&groupBy=day"));

        Assert.Equal(0, secondStatistics.TotalSalesMinor);
        Assert.Equal(0, secondStatistics.TotalExpensesMinor);
        Assert.Equal(0, secondStatistics.NetCashFlowMinor);
        Assert.Equal(0, secondStatistics.SaleCount);
        Assert.Equal(0, secondStatistics.ExpenseCount);
        Assert.Equal(0, secondStatistics.RecordedActivityDays);
        Assert.Null(secondStatistics.EarliestAvailableLocalDate);
        Assert.Null(secondStatistics.LatestAvailableLocalDate);
        Assert.All(
            secondStatistics.Groups,
            group =>
            {
                Assert.Equal(0, group.SalesMinor);
                Assert.Equal(0, group.ExpensesMinor);
                Assert.Equal(0, group.RecordedActivityDays);
            });
    }

    [Fact]
    public async Task LenderStatistics_RequiresCurrentConsentBeforeAndAfterRevoke()
    {
        var (traderClient, traderAuth) = await CreateTraderAsync();
        var lender = await CreateUserAsync(UserRole.Lender);
        var otherLender = await CreateUserAsync(UserRole.Lender);
        var businessId = traderAuth.BusinessId!.Value;

        await AddTransactionsAsync(
            businessId,
            (LagosTime.ToUtc(new DateTime(2026, 1, 2, 12, 0, 0)),
                TransactionType.Sale, 1_000_000));

        using var lenderClient = CreateClient(lender.Token);
        using var otherLenderClient = CreateClient(otherLender.Token);

        var beforeGrant = await lenderClient.GetAsync(
            $"/api/lender/businesses/{businessId}/statistics" +
            "?from=2026-01-01&to=2026-01-07&groupBy=day");
        Assert.Equal(HttpStatusCode.Forbidden, beforeGrant.StatusCode);
        Assert.Equal("CONSENT_REQUIRED", await ReadProblemCodeAsync(beforeGrant));

        var grantResponse = await traderClient.PutAsync(
            $"/api/businesses/me/consents/{lender.UserId}",
            content: null);
        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);

        var whileActive = await lenderClient.GetAsync(
            $"/api/lender/businesses/{businessId}/statistics" +
            "?from=2026-01-01&to=2026-01-07&groupBy=day");
        Assert.Equal(HttpStatusCode.OK, whileActive.StatusCode);

        var statistics = await ReadStatisticsAsync(whileActive);
        Assert.Equal(businessId, statistics.BusinessId);
        Assert.Equal(1_000_000, statistics.TotalSalesMinor);
        Assert.Equal(1, statistics.SaleCount);

        var otherLenderResponse = await otherLenderClient.GetAsync(
            $"/api/lender/businesses/{businessId}/statistics" +
            "?from=2026-01-01&to=2026-01-07&groupBy=day");
        Assert.Equal(HttpStatusCode.Forbidden, otherLenderResponse.StatusCode);
        Assert.Equal(
            "CONSENT_REQUIRED",
            await ReadProblemCodeAsync(otherLenderResponse));

        var revokeResponse = await traderClient.DeleteAsync(
            $"/api/businesses/me/consents/{lender.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var afterRevoke = await lenderClient.GetAsync(
            $"/api/lender/businesses/{businessId}/statistics" +
            "?from=2026-01-01&to=2026-01-07&groupBy=day");
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.StatusCode);
        Assert.Equal("CONSENT_REQUIRED", await ReadProblemCodeAsync(afterRevoke));
    }

    [Fact]
    public async Task Statistics_RequiresRoleAndExistingBusiness()
    {
        using var anonymousClient = _factory.CreateClient();

        var unauthorized = await anonymousClient.GetAsync(
            "/api/businesses/me/statistics" +
            "?from=2026-01-01&to=2026-01-07&groupBy=day");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ReadProblemCodeAsync(unauthorized));

        var lender = await CreateUserAsync(UserRole.Lender);
        using var lenderClient = CreateClient(lender.Token);

        var forbidden = await lenderClient.GetAsync(
            "/api/businesses/me/statistics" +
            "?from=2026-01-01&to=2026-01-07&groupBy=day");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("FORBIDDEN", await ReadProblemCodeAsync(forbidden));

        var traderWithoutBusiness = await CreateUserAsync(UserRole.Trader, createBusiness: false);
        using var traderWithoutBusinessClient = CreateClient(traderWithoutBusiness.Token);

        var notFound = await traderWithoutBusinessClient.GetAsync(
            "/api/businesses/me/statistics" +
            "?from=2026-01-01&to=2026-01-07&groupBy=day");
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

    private async Task<(Guid UserId, string Token)> CreateUserAsync(
        UserRole role,
        bool createBusiness = true)
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

        if (role == UserRole.Trader && createBusiness)
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

        return (user.Id, tokenService.Create(user, businessId).AccessToken);
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

    private static async Task<PeriodStatisticsResponse> ReadStatisticsAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<PeriodStatisticsResponse>(
            JsonOptions);

        Assert.NotNull(body);
        return body!;
    }

    private static async Task AssertValidationErrorAsync(
        HttpResponseMessage response,
        params string[] expectedFields)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ReadProblemCodeAsync(response));

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.True(document.RootElement.TryGetProperty("errors", out var errors));

        foreach (var field in expectedFields)
        {
            Assert.True(
                errors.TryGetProperty(field, out _),
                $"Expected a validation error for '{field}'.");
        }
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

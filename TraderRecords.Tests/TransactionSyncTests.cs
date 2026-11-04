using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TraderRecords.Api.Application.DTOs.Auth;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;
using Xunit;

namespace TraderRecords.Tests;

public sealed class TransactionSyncTests : IAsyncLifetime
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
    public async Task T02_FirstBatch_IdenticalRetry_AndChangedRetry_AreClassified()
    {
        var (client, auth) = await CreateTraderAsync();
        var businessId = auth.BusinessId!.Value;

        var firstClientId = Guid.NewGuid();
        var secondClientId = Guid.NewGuid();
        var invalidClientId = Guid.NewGuid();

        var firstItems = new object?[]
        {
            Item(
                firstClientId,
                "Sale",
                4_500_000,
                "  Sold rice  ",
                OffsetTimestamp(TimeSpan.FromDays(-1), TimeSpan.FromHours(1))),
            Item(
                secondClientId,
                "Expense",
                1_200_000,
                "Stock purchase",
                OffsetTimestamp(
                    TimeSpan.FromDays(-1).Add(TimeSpan.FromMinutes(5)),
                    TimeSpan.FromHours(1))),
            Item(
                invalidClientId,
                "Sale",
                -100,
                null,
                OffsetTimestamp(
                    TimeSpan.FromDays(-1).Add(TimeSpan.FromMinutes(10)),
                    TimeSpan.FromHours(1)))
        };

        var firstResponse = await PostBatchAsync(client, firstItems);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstBody = await ReadSyncResponseAsync(firstResponse);
        Assert.Equal(3, firstBody.ReceivedCount);
        Assert.Equal(2, firstBody.AcceptedCount);
        Assert.Equal(0, firstBody.DuplicateCount);
        Assert.Equal(1, firstBody.RejectedCount);
        Assert.Equal([0, 1, 2], firstBody.Results.Select(result => result.Index));
        Assert.Equal("Accepted", firstBody.Results[0].Status);
        Assert.Equal("Accepted", firstBody.Results[1].Status);
        Assert.Equal("Rejected", firstBody.Results[2].Status);
        Assert.NotNull(firstBody.Results[0].TransactionId);
        Assert.NotNull(firstBody.Results[1].TransactionId);
        Assert.Null(firstBody.Results[2].TransactionId);
        Assert.Equal("INVALID_AMOUNT", Assert.Single(firstBody.Results[2].Errors).Code);

        var firstTransactionIds = firstBody.Results
            .Take(2)
            .Select(result => result.TransactionId)
            .ToArray();

        Assert.Equal(2, (await GetTransactionsAsync(businessId)).Count);

        // Equivalent UTC strings and trimmed notes must still compare as the same
        // canonical transaction, so an identical retry creates no new rows.
        var retryItems = new object?[]
        {
            Item(firstClientId, "Sale", 4_500_000, "Sold rice", UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(
                secondClientId,
                "Expense",
                1_200_000,
                "Stock purchase",
                UtcTimestamp(TimeSpan.FromDays(-1).Add(TimeSpan.FromMinutes(5)))),
            Item(
                invalidClientId,
                "Sale",
                -100,
                null,
                OffsetTimestamp(
                    TimeSpan.FromDays(-1).Add(TimeSpan.FromMinutes(10)),
                    TimeSpan.FromHours(1)))
        };

        var retryResponse = await PostBatchAsync(client, retryItems);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        var retryBody = await ReadSyncResponseAsync(retryResponse);
        Assert.Equal(3, retryBody.ReceivedCount);
        Assert.Equal(0, retryBody.AcceptedCount);
        Assert.Equal(2, retryBody.DuplicateCount);
        Assert.Equal(1, retryBody.RejectedCount);
        Assert.Equal("Duplicate", retryBody.Results[0].Status);
        Assert.Equal("Duplicate", retryBody.Results[1].Status);
        Assert.Equal(firstTransactionIds[0], retryBody.Results[0].TransactionId);
        Assert.Equal(firstTransactionIds[1], retryBody.Results[1].TransactionId);
        Assert.Equal(2, (await GetTransactionsAsync(businessId)).Count);

        var changedItems = new object?[]
        {
            Item(firstClientId, "Sale", 5_000_000, "Sold rice", UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(
                secondClientId,
                "Expense",
                1_200_000,
                "Stock purchase",
                UtcTimestamp(TimeSpan.FromDays(-1).Add(TimeSpan.FromMinutes(5)))),
            Item(
                invalidClientId,
                "Sale",
                -100,
                null,
                OffsetTimestamp(
                    TimeSpan.FromDays(-1).Add(TimeSpan.FromMinutes(10)),
                    TimeSpan.FromHours(1)))
        };

        var changedResponse = await PostBatchAsync(client, changedItems);
        Assert.Equal(HttpStatusCode.OK, changedResponse.StatusCode);

        var changedBody = await ReadSyncResponseAsync(changedResponse);
        Assert.Equal(0, changedBody.AcceptedCount);
        Assert.Equal(1, changedBody.DuplicateCount);
        Assert.Equal(2, changedBody.RejectedCount);
        Assert.Equal("CLIENT_ID_CONFLICT", Assert.Single(changedBody.Results[0].Errors).Code);
        Assert.Null(changedBody.Results[0].TransactionId);

        var rowsAfterConflict = await GetTransactionsAsync(businessId);
        Assert.Equal(2, rowsAfterConflict.Count);
        Assert.Equal(
            4_500_000,
            rowsAfterConflict.Single(transaction => transaction.ClientId == firstClientId).AmountMinor);
    }

    [Fact]
    public async Task T02_DuplicateWithinBatch_AndConflictWithinBatch_AreOrdered()
    {
        var (client, auth) = await CreateTraderAsync();
        var clientId = Guid.NewGuid();

        var items = new object?[]
        {
            Item(clientId, "Sale", 1_000, "first", UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(clientId, "Sale", 1_000, "first", UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(clientId, "Sale", 2_000, "first", UtcTimestamp(TimeSpan.FromDays(-1)))
        };

        var response = await PostBatchAsync(client, items);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadSyncResponseAsync(response);
        Assert.Equal(3, body.ReceivedCount);
        Assert.Equal(1, body.AcceptedCount);
        Assert.Equal(1, body.DuplicateCount);
        Assert.Equal(1, body.RejectedCount);
        Assert.Equal("Accepted", body.Results[0].Status);
        Assert.Equal("Duplicate", body.Results[1].Status);
        Assert.Equal("Rejected", body.Results[2].Status);
        Assert.Equal(body.Results[0].TransactionId, body.Results[1].TransactionId);
        Assert.Equal("CLIENT_ID_CONFLICT", Assert.Single(body.Results[2].Errors).Code);

        var rows = await GetTransactionsAsync(auth.BusinessId!.Value);
        Assert.Single(rows);
        Assert.Equal(1_000, rows[0].AmountMinor);
    }

    [Fact]
    public async Task T03_Envelope_RejectsZero_101_AndMissingItems()
    {
        var (client, _) = await CreateTraderAsync();

        var emptyResponse = await PostBatchAsync(client, Array.Empty<object?>());
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ReadProblemCodeAsync(emptyResponse));

        var tooManyItems = Enumerable
            .Range(0, 101)
            .Select(_ => Item(
                Guid.NewGuid(),
                "Sale",
                1,
                null,
                UtcTimestamp(TimeSpan.FromDays(-1))))
            .ToArray();

        var tooManyResponse = await PostBatchAsync(client, tooManyItems);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ReadProblemCodeAsync(tooManyResponse));

        var missingResponse = await client.PostAsync(
            "/api/transactions/sync",
            JsonContent("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ReadProblemCodeAsync(missingResponse));
    }

    [Fact]
    public async Task T03_NullItem_And_AmountBoundaries_ArePerItemOutcomes()
    {
        var (client, auth) = await CreateTraderAsync();

        var items = new object?[]
        {
            null,
            Item(Guid.NewGuid(), "Sale", 1, null, UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(
                Guid.NewGuid(),
                "Expense",
                100_000_000_000,
                null,
                UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(Guid.NewGuid(), "Sale", 0, null, UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(
                Guid.NewGuid(),
                "Sale",
                100_000_000_001,
                null,
                UtcTimestamp(TimeSpan.FromDays(-1)))
        };

        var response = await PostBatchAsync(client, items);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadSyncResponseAsync(response);
        Assert.Equal(5, body.ReceivedCount);
        Assert.Equal(2, body.AcceptedCount);
        Assert.Equal(0, body.DuplicateCount);
        Assert.Equal(3, body.RejectedCount);
        Assert.Equal("INVALID_ITEM", Assert.Single(body.Results[0].Errors).Code);
        Assert.Null(body.Results[0].ClientId);
        Assert.Equal("Accepted", body.Results[1].Status);
        Assert.Equal("Accepted", body.Results[2].Status);
        Assert.Equal("INVALID_AMOUNT", Assert.Single(body.Results[3].Errors).Code);
        Assert.Equal("INVALID_AMOUNT", Assert.Single(body.Results[4].Errors).Code);

        var rows = await GetTransactionsAsync(auth.BusinessId!.Value);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, transaction => transaction.AmountMinor == 1);
        Assert.Contains(rows, transaction => transaction.AmountMinor == 100_000_000_000);
    }

    [Fact]
    public async Task T03_Type_Note_And_Timestamp_Validation_ArePerItemOutcomes()
    {
        var (client, auth) = await CreateTraderAsync();

        var yesterday = UtcTimestamp(TimeSpan.FromDays(-1));
        var items = new object?[]
        {
            Item(Guid.NewGuid(), "Transfer", 1_000, null, yesterday),
            Item(Guid.NewGuid(), "Sale", 1_000, new string('a', 281), yesterday),
            Item(Guid.NewGuid(), "Sale", 1_000, "   ", yesterday),
            Item(Guid.NewGuid(), "Sale", 1_000, null, null),
            Item(Guid.NewGuid(), "Sale", 1_000, null, NoOffsetTimestamp(TimeSpan.FromDays(-1))),
            Item(
                Guid.NewGuid(),
                "Sale",
                1_000,
                null,
                UtcTimestamp(TimeSpan.FromMilliseconds(1))),
            Item(
                Guid.NewGuid(),
                "Sale",
                1_000,
                null,
                UtcTimestamp(TimeSpan.FromDays(-365).Add(TimeSpan.FromMilliseconds(-1)))),
            Item(
                Guid.NewGuid(),
                "Sale",
                1_000,
                null,
                UtcTimestamp(TimeSpan.FromDays(-365))),
            Item(Guid.NewGuid(), "Sale", 1_000, null, UtcTimestamp(TimeSpan.Zero))
        };

        var response = await PostBatchAsync(client, items);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadSyncResponseAsync(response);
        Assert.Equal(9, body.ReceivedCount);
        Assert.Equal(3, body.AcceptedCount);
        Assert.Equal(0, body.DuplicateCount);
        Assert.Equal(6, body.RejectedCount);
        Assert.Equal("INVALID_TYPE", Assert.Single(body.Results[0].Errors).Code);
        Assert.Equal("NOTE_TOO_LONG", Assert.Single(body.Results[1].Errors).Code);
        Assert.Equal("Accepted", body.Results[2].Status);
        Assert.Equal("INVALID_OCCURRED_AT", Assert.Single(body.Results[3].Errors).Code);
        Assert.Equal("INVALID_OCCURRED_AT", Assert.Single(body.Results[4].Errors).Code);
        Assert.Equal("INVALID_OCCURRED_AT", Assert.Single(body.Results[5].Errors).Code);
        Assert.Equal("INVALID_OCCURRED_AT", Assert.Single(body.Results[6].Errors).Code);
        Assert.Equal("Accepted", body.Results[7].Status);
        Assert.Equal("Accepted", body.Results[8].Status);

        var rows = await GetTransactionsAsync(auth.BusinessId!.Value);
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, transaction => transaction.Note is null);
    }

    [Fact]
    public async Task T03_MalformedJson_And_OversizedBody_AreRequestWideFailures()
    {
        var (client, _) = await CreateTraderAsync();

        var malformedResponse = await client.PostAsync(
            "/api/transactions/sync",
            JsonContent("{"));
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ReadProblemCodeAsync(malformedResponse));

        var oversizedItem = Item(
            Guid.NewGuid(),
            "Sale",
            1,
            new string('x', 300_000),
            UtcTimestamp(TimeSpan.FromDays(-1)));

        var oversizedResponse = await PostBatchAsync(client, new[] { oversizedItem });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        Assert.Equal("PAYLOAD_TOO_LARGE", await ReadProblemCodeAsync(oversizedResponse));
    }

    [Fact]
    public async Task T04_ConcurrentRequests_WithSameClientId_LeaveOneRow()
    {
        var (_, auth) = await CreateTraderAsync();
        var clientId = Guid.NewGuid();

        var items = new object?[]
        {
            Item(clientId, "Sale", 1_234, "race", UtcTimestamp(TimeSpan.FromDays(-1)))
        };

        using var firstClient = CreateClient(auth.AccessToken);
        using var secondClient = CreateClient(auth.AccessToken);

        var responses = await Task.WhenAll(
            PostBatchAsync(firstClient, items),
            PostBatchAsync(secondClient, items));

        Assert.All(
            responses,
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var bodies = await Task.WhenAll(responses.Select(ReadSyncResponseAsync));
        Assert.Equal(1, bodies.Sum(body => body.AcceptedCount));
        Assert.Equal(1, bodies.Sum(body => body.DuplicateCount));
        Assert.Equal(0, bodies.Sum(body => body.RejectedCount));

        var acceptedId = bodies
            .SelectMany(body => body.Results)
            .Single(result => result.Status == "Accepted")
            .TransactionId;
        var duplicateId = bodies
            .SelectMany(body => body.Results)
            .Single(result => result.Status == "Duplicate")
            .TransactionId;

        Assert.Equal(acceptedId, duplicateId);

        var rows = await GetTransactionsAsync(auth.BusinessId!.Value);
        Assert.Single(rows);
        Assert.Equal(1_234, rows[0].AmountMinor);
    }

    [Fact]
    public async Task T07_SyncRequiresTrader_AndAnOwnedBusiness()
    {
        var items = new object?[]
        {
            Item(Guid.NewGuid(), "Sale", 1_000, null, UtcTimestamp(TimeSpan.FromDays(-1)))
        };

        using var anonymousClient = _factory.CreateClient();
        var missingTokenResponse = await PostBatchAsync(anonymousClient, items);
        Assert.Equal(HttpStatusCode.Unauthorized, missingTokenResponse.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ReadProblemCodeAsync(missingTokenResponse));

        var lenderToken = await CreateTokenAsync(UserRole.Lender, createBusiness: false);
        using var lenderClient = CreateClient(lenderToken);
        var wrongRoleResponse = await PostBatchAsync(lenderClient, items);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRoleResponse.StatusCode);
        Assert.Equal("FORBIDDEN", await ReadProblemCodeAsync(wrongRoleResponse));

        var traderWithoutBusinessToken = await CreateTokenAsync(
            UserRole.Trader,
            createBusiness: false);
        using var traderWithoutBusinessClient = CreateClient(traderWithoutBusinessToken);
        var missingBusinessResponse = await PostBatchAsync(
            traderWithoutBusinessClient,
            items);
        Assert.Equal(HttpStatusCode.NotFound, missingBusinessResponse.StatusCode);
        Assert.Equal("BUSINESS_NOT_FOUND", await ReadProblemCodeAsync(missingBusinessResponse));
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

    private static Task<HttpResponseMessage> PostBatchAsync(
        HttpClient client,
        object?[] items)
    {
        return client.PostAsJsonAsync(
            "/api/transactions/sync",
            new { items },
            JsonOptions);
    }

    private static object Item(
        Guid? clientId,
        string? type,
        long? amountMinor,
        string? note,
        string? occurredAt)
    {
        return occurredAt is null
            ? new { clientId, type, amountMinor, note }
            : new { clientId, type, amountMinor, note, occurredAt };
    }

    private static StringContent JsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string UtcTimestamp(TimeSpan offset)
    {
        return TestApiFactory.FixedNow
            .Add(offset)
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private static string OffsetTimestamp(TimeSpan offset, TimeSpan zoneOffset)
    {
        return TestApiFactory.FixedNow
            .Add(offset)
            .ToOffset(zoneOffset)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
    }

    private static string NoOffsetTimestamp(TimeSpan offset)
    {
        return TestApiFactory.FixedNow
            .Add(offset)
            .UtcDateTime
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    private static async Task<SyncResponse> ReadSyncResponseAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<SyncResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private async Task<List<Transaction>> GetTransactionsAsync(Guid businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.BusinessId == businessId)
            .ToListAsync();
    }

    private sealed record SyncResponse(
        int ReceivedCount,
        int AcceptedCount,
        int DuplicateCount,
        int RejectedCount,
        List<SyncResult> Results);

    private sealed record SyncResult(
        int Index,
        Guid? ClientId,
        string Status,
        Guid? TransactionId,
        List<ItemError> Errors);

    private sealed record ItemError(
        string Code,
        string? Field,
        string Message);
}

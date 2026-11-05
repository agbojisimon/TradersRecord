using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    public async Task IdenticalSyncRetry_StoresOneRecord_AndLeavesTotalsUnchanged()
    {
        var (client, auth) = await CreateTraderAsync();
        var clientId = Guid.NewGuid();
        var item = Item(
            clientId,
            "Sale",
            4_500_000,
            "Sold rice",
            UtcTimestamp(TimeSpan.FromDays(-1)));

        var firstResponse = await PostBatchAsync(client, [item]);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstBody = await ReadSyncResponseAsync(firstResponse);
        Assert.Equal(1, firstBody.AcceptedCount);
        Assert.Equal(0, firstBody.DuplicateCount);
        Assert.Equal(0, firstBody.RejectedCount);

        var originalTransactionId = firstBody.Results[0].TransactionId;
        var rowsBeforeRetry = await GetTransactionsAsync(auth.BusinessId!.Value);
        Assert.Single(rowsBeforeRetry);
        Assert.Equal(4_500_000, rowsBeforeRetry.Sum(transaction => transaction.AmountMinor));

        // The same client ID with the same canonical fields is a successful retry,
        // not a second sale. It must return the original transaction ID.
        var retryResponse = await PostBatchAsync(client, [item]);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        var retryBody = await ReadSyncResponseAsync(retryResponse);
        Assert.Equal(0, retryBody.AcceptedCount);
        Assert.Equal(1, retryBody.DuplicateCount);
        Assert.Equal(0, retryBody.RejectedCount);
        Assert.Equal("Duplicate", retryBody.Results[0].Status);
        Assert.Equal(originalTransactionId, retryBody.Results[0].TransactionId);

        var rowsAfterRetry = await GetTransactionsAsync(auth.BusinessId!.Value);
        Assert.Single(rowsAfterRetry);
        Assert.Equal(4_500_000, rowsAfterRetry.Sum(transaction => transaction.AmountMinor));
    }

    [Fact]
    public async Task ChangedPayloadForExistingClientId_RejectsItem_AndPreservesOriginalTransaction()
    {
        var (client, auth) = await CreateTraderAsync();
        var clientId = Guid.NewGuid();
        var occurredAt = UtcTimestamp(TimeSpan.FromDays(-1));

        var firstResponse = await PostBatchAsync(
            client,
            [Item(clientId, "Sale", 4_500_000, "Sold rice", occurredAt)]);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstBody = await ReadSyncResponseAsync(firstResponse);
        Assert.Equal(1, firstBody.AcceptedCount);

        // Reusing the idempotency key with a different amount is an item-level
        // conflict, never an update and never a request-wide 409.
        var conflictResponse = await PostBatchAsync(
            client,
            [Item(clientId, "Sale", 5_000_000, "Sold rice", occurredAt)]);
        Assert.Equal(HttpStatusCode.OK, conflictResponse.StatusCode);

        var conflictBody = await ReadSyncResponseAsync(conflictResponse);
        Assert.Equal(0, conflictBody.AcceptedCount);
        Assert.Equal(0, conflictBody.DuplicateCount);
        Assert.Equal(1, conflictBody.RejectedCount);
        Assert.Equal("Rejected", conflictBody.Results[0].Status);
        Assert.Null(conflictBody.Results[0].TransactionId);
        Assert.Equal(
            "CLIENT_ID_CONFLICT",
            Assert.Single(conflictBody.Results[0].Errors).Code);

        var rows = await GetTransactionsAsync(auth.BusinessId!.Value);
        Assert.Single(rows);
        Assert.Equal(4_500_000, rows[0].AmountMinor);
    }

    [Fact]
    public async Task PartialSyncBatch_SavesValidItems_AndReturnsClearItemLevelErrors()
    {
        var (client, auth) = await CreateTraderAsync();
        var validClientId = Guid.NewGuid();

        // One valid entry plus four independently readable invalid entries. Invalid
        // items must not roll back the valid entry or prevent later outcomes.
        var items = new object?[]
        {
            Item(validClientId, "Sale", 1_000, "valid", UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(Guid.NewGuid(), "Sale", 0, null, UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(Guid.NewGuid(), "Transfer", 1_000, null, UtcTimestamp(TimeSpan.FromDays(-1))),
            Item(Guid.NewGuid(), "Sale", 1_000, null, null),
            null
        };

        var response = await PostBatchAsync(client, items);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadSyncResponseAsync(response);
        Assert.Equal(5, body.ReceivedCount);
        Assert.Equal(1, body.AcceptedCount);
        Assert.Equal(0, body.DuplicateCount);
        Assert.Equal(4, body.RejectedCount);
        Assert.Equal([0, 1, 2, 3, 4], body.Results.Select(result => result.Index));

        Assert.Equal("Accepted", body.Results[0].Status);
        Assert.Equal("INVALID_AMOUNT", Assert.Single(body.Results[1].Errors).Code);
        Assert.Equal("INVALID_TYPE", Assert.Single(body.Results[2].Errors).Code);
        Assert.Equal("INVALID_OCCURRED_AT", Assert.Single(body.Results[3].Errors).Code);
        Assert.Equal("INVALID_ITEM", Assert.Single(body.Results[4].Errors).Code);

        var rows = await GetTransactionsAsync(auth.BusinessId!.Value);
        Assert.Single(rows);
        Assert.Equal(validClientId, rows[0].ClientId);
        Assert.Equal(1_000, rows[0].AmountMinor);
    }

    [Fact]
    public async Task ConcurrentIdenticalSyncRequests_StoreOneRecord()
    {
        var (_, auth) = await CreateTraderAsync();
        var clientId = Guid.NewGuid();
        var items = new object?[]
        {
            Item(
                clientId,
                "Sale",
                1_234,
                "race",
                UtcTimestamp(TimeSpan.FromDays(-1)))
        };

        // Two independent HTTP clients model simultaneous retries. The unique index
        // must leave one row and classify the loser as Duplicate, not a raw 500.
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
    public async Task SyncRequests_RequireTraderRoleAndOwnedBusiness()
    {
        var items = new object?[]
        {
            Item(
                Guid.NewGuid(),
                "Sale",
                1_000,
                null,
                UtcTimestamp(TimeSpan.FromDays(-1)))
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
        using var traderWithoutBusinessClient = CreateClient(
            traderWithoutBusinessToken);
        var missingBusinessResponse = await PostBatchAsync(
            traderWithoutBusinessClient,
            items);
        Assert.Equal(HttpStatusCode.NotFound, missingBusinessResponse.StatusCode);
        Assert.Equal(
            "BUSINESS_NOT_FOUND",
            await ReadProblemCodeAsync(missingBusinessResponse));
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

    private static string UtcTimestamp(TimeSpan offset)
    {
        return TestApiFactory.FixedNow
            .Add(offset)
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
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

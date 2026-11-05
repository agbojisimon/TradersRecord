using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TraderRecords.Api.Application.DTOs.Auth;
using TraderRecords.Api.Application.DTOs.Consent;
using TraderRecords.Api.Application.DTOs.Summary;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;
using Xunit;

namespace TraderRecords.Tests;

public sealed class ConsentTests : IAsyncLifetime
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
    public async Task LenderSummary_RequiresActiveConsent_BeforeAndAfterRevoke()
    {
        var (traderClient, traderAuth) = await CreateTraderAsync();
        var lender = await CreateUserAsync(UserRole.Lender);
        var businessId = traderAuth.BusinessId!.Value;
        var nowUtc = TestApiFactory.FixedNow.UtcDateTime;

        await AddTransactionsAsync(
            businessId,
            (nowUtc.AddDays(-1), TransactionType.Sale, 1_000_000));

        using var lenderClient = CreateClient(lender.Token);

        // No consent row exists yet, so a valid Lender is still denied.
        var beforeGrant = await lenderClient.GetAsync(
            $"/api/lender/businesses/{businessId}/summary");
        Assert.Equal(HttpStatusCode.Forbidden, beforeGrant.StatusCode);
        Assert.Equal("CONSENT_REQUIRED", await ReadProblemCodeAsync(beforeGrant));

        var grantResponse = await traderClient.PutAsync(
            $"/api/businesses/me/consents/{lender.UserId}",
            content: null);
        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);

        var consent = await ReadConsentAsync(grantResponse);
        Assert.True(consent.IsActive);
        Assert.Equal(businessId, consent.BusinessId);
        Assert.Equal(lender.UserId, consent.LenderId);

        var whileActive = await lenderClient.GetAsync(
            $"/api/lender/businesses/{businessId}/summary");
        Assert.Equal(HttpStatusCode.OK, whileActive.StatusCode);

        var summary = await ReadSummaryAsync(whileActive);
        Assert.Equal(businessId, summary.BusinessId);
        Assert.Equal(1_000_000, summary.TotalSalesMinor);
        Assert.Equal(1, summary.TransactionCount);

        var revokeResponse = await traderClient.DeleteAsync(
            $"/api/businesses/me/consents/{lender.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        // The next request must see the persisted revocation and deny access.
        var afterRevoke = await lenderClient.GetAsync(
            $"/api/lender/businesses/{businessId}/summary");
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.StatusCode);
        Assert.Equal("CONSENT_REQUIRED", await ReadProblemCodeAsync(afterRevoke));
    }

    [Fact]
    public async Task GrantConsent_WhenAlreadyActive_LeavesTimestampsUnchanged()
    {
        var (traderClient, traderAuth) = await CreateTraderAsync();
        var lender = await CreateUserAsync(UserRole.Lender);
        var businessId = traderAuth.BusinessId!.Value;
        var originalGrant = TestApiFactory.FixedNow.UtcDateTime.AddDays(-10);

        await SeedConsentAsync(businessId, lender.UserId, originalGrant, revokedAtUtc: null);

        var response = await traderClient.PutAsync(
            $"/api/businesses/me/consents/{lender.UserId}",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var consent = await ReadConsentAsync(response);
        Assert.True(consent.IsActive);
        Assert.Equal(originalGrant, consent.GrantedAtUtc);
        Assert.Null(consent.RevokedAtUtc);

        var stored = await GetConsentAsync(businessId, lender.UserId);
        Assert.NotNull(stored);
        Assert.Equal(originalGrant, stored!.GrantedAtUtc);
        Assert.Null(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task RevokeConsent_IsIdempotent_AndAbsentConsentSucceeds()
    {
        var (traderClient, traderAuth) = await CreateTraderAsync();
        var absentLender = await CreateUserAsync(UserRole.Lender);
        var revokedLender = await CreateUserAsync(UserRole.Lender);
        var businessId = traderAuth.BusinessId!.Value;
        var originalRevocation = TestApiFactory.FixedNow.UtcDateTime.AddDays(-2);

        var absentResponse = await traderClient.DeleteAsync(
            $"/api/businesses/me/consents/{absentLender.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, absentResponse.StatusCode);
        Assert.Null(await GetConsentAsync(businessId, absentLender.UserId));

        await SeedConsentAsync(
            businessId,
            revokedLender.UserId,
            originalRevocation.AddDays(-1),
            originalRevocation);

        var repeatedResponse = await traderClient.DeleteAsync(
            $"/api/businesses/me/consents/{revokedLender.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, repeatedResponse.StatusCode);

        var stored = await GetConsentAsync(businessId, revokedLender.UserId);
        Assert.NotNull(stored);
        Assert.Equal(originalRevocation, stored!.RevokedAtUtc);
    }

    [Fact]
    public async Task RegrantAfterRevocation_ReactivatesRowAndClearsRevocation()
    {
        var (traderClient, traderAuth) = await CreateTraderAsync();
        var lender = await CreateUserAsync(UserRole.Lender);
        var businessId = traderAuth.BusinessId!.Value;
        var oldGrant = TestApiFactory.FixedNow.UtcDateTime.AddDays(-20);
        var oldRevocation = TestApiFactory.FixedNow.UtcDateTime.AddDays(-10);

        await SeedConsentAsync(businessId, lender.UserId, oldGrant, oldRevocation);

        var response = await traderClient.PutAsync(
            $"/api/businesses/me/consents/{lender.UserId}",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var consent = await ReadConsentAsync(response);
        Assert.True(consent.IsActive);
        Assert.Equal(TestApiFactory.FixedNow.UtcDateTime, consent.GrantedAtUtc);
        Assert.Null(consent.RevokedAtUtc);

        var stored = await GetConsentAsync(businessId, lender.UserId);
        Assert.NotNull(stored);
        Assert.Equal(TestApiFactory.FixedNow.UtcDateTime, stored!.GrantedAtUtc);
        Assert.Null(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task WrongLender_CannotUseAnotherLendersConsent()
    {
        var (traderClient, traderAuth) = await CreateTraderAsync();
        var allowedLender = await CreateUserAsync(UserRole.Lender);
        var otherLender = await CreateUserAsync(UserRole.Lender);
        var businessId = traderAuth.BusinessId!.Value;
        var nowUtc = TestApiFactory.FixedNow.UtcDateTime;

        await AddTransactionsAsync(
            businessId,
            (nowUtc.AddDays(-1), TransactionType.Sale, 2_000_000));

        var grantResponse = await traderClient.PutAsync(
            $"/api/businesses/me/consents/{allowedLender.UserId}",
            content: null);
        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);

        using var allowedClient = CreateClient(allowedLender.Token);
        var allowedResponse = await allowedClient.GetAsync(
            $"/api/lender/businesses/{businessId}/summary");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);

        using var otherClient = CreateClient(otherLender.Token);
        var deniedResponse = await otherClient.GetAsync(
            $"/api/lender/businesses/{businessId}/summary");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        Assert.Equal("CONSENT_REQUIRED", await ReadProblemCodeAsync(deniedResponse));
    }

    [Fact]
    public async Task ConsentRoutes_RequireTraderRoleAndValidLender()
    {
        var (traderClient, _) = await CreateTraderAsync();
        var lender = await CreateUserAsync(UserRole.Lender);
        var otherTrader = await CreateUserAsync(UserRole.Trader);

        using var anonymousClient = _factory.CreateClient();
        var anonymousResponse = await anonymousClient.PutAsync(
            $"/api/businesses/me/consents/{lender.UserId}",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ReadProblemCodeAsync(anonymousResponse));

        using var lenderClient = CreateClient(lender.Token);
        var wrongRoleResponse = await lenderClient.PutAsync(
            $"/api/businesses/me/consents/{lender.UserId}",
            content: null);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRoleResponse.StatusCode);
        Assert.Equal("FORBIDDEN", await ReadProblemCodeAsync(wrongRoleResponse));

        var unknownLenderResponse = await traderClient.PutAsync(
            $"/api/businesses/me/consents/{Guid.NewGuid()}",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, unknownLenderResponse.StatusCode);
        Assert.Equal("LENDER_NOT_FOUND", await ReadProblemCodeAsync(unknownLenderResponse));

        var wrongTargetResponse = await traderClient.PutAsync(
            $"/api/businesses/me/consents/{otherTrader.UserId}",
            content: null);
        Assert.Equal(HttpStatusCode.Conflict, wrongTargetResponse.StatusCode);
        Assert.Equal("TARGET_NOT_LENDER", await ReadProblemCodeAsync(wrongTargetResponse));

        var emptyLenderResponse = await traderClient.PutAsync(
            $"/api/businesses/me/consents/{Guid.Empty}",
            content: null);
        Assert.Equal(HttpStatusCode.BadRequest, emptyLenderResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ReadProblemCodeAsync(emptyLenderResponse));

        var emptyRevokeResponse = await traderClient.DeleteAsync(
            $"/api/businesses/me/consents/{Guid.Empty}");
        Assert.Equal(HttpStatusCode.BadRequest, emptyRevokeResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ReadProblemCodeAsync(emptyRevokeResponse));
    }

    [Fact]
    public async Task LenderSummary_RequiresLenderRoleAndKnownBusiness()
    {
        var (traderClient, traderAuth) = await CreateTraderAsync();
        var lender = await CreateUserAsync(UserRole.Lender);
        var businessId = traderAuth.BusinessId!.Value;

        using var anonymousClient = _factory.CreateClient();
        var anonymousResponse = await anonymousClient.GetAsync(
            $"/api/lender/businesses/{businessId}/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ReadProblemCodeAsync(anonymousResponse));

        var wrongRoleResponse = await traderClient.GetAsync(
            $"/api/lender/businesses/{businessId}/summary");
        Assert.Equal(HttpStatusCode.Forbidden, wrongRoleResponse.StatusCode);
        Assert.Equal("FORBIDDEN", await ReadProblemCodeAsync(wrongRoleResponse));

        using var lenderClient = CreateClient(lender.Token);
        var unknownBusinessResponse = await lenderClient.GetAsync(
            $"/api/lender/businesses/{Guid.NewGuid()}/summary");
        Assert.Equal(HttpStatusCode.NotFound, unknownBusinessResponse.StatusCode);
        Assert.Equal(
            "BUSINESS_NOT_FOUND",
            await ReadProblemCodeAsync(unknownBusinessResponse));

        var emptyBusinessResponse = await lenderClient.GetAsync(
            $"/api/lender/businesses/{Guid.Empty}/summary");
        Assert.Equal(HttpStatusCode.BadRequest, emptyBusinessResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ReadProblemCodeAsync(emptyBusinessResponse));
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

    private async Task<(Guid UserId, string Token)> CreateUserAsync(UserRole role)
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

        if (role == UserRole.Trader)
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

    private async Task SeedConsentAsync(
        Guid businessId,
        Guid lenderId,
        DateTime grantedAtUtc,
        DateTime? revokedAtUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Consents.Add(new Consent
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            LenderUserId = lenderId,
            GrantedAtUtc = grantedAtUtc,
            RevokedAtUtc = revokedAtUtc
        });

        await db.SaveChangesAsync();
    }

    private async Task<Consent?> GetConsentAsync(Guid businessId, Guid lenderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Consents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                consent => consent.BusinessId == businessId
                    && consent.LenderUserId == lenderId);
    }

    private static async Task<ConsentResponse> ReadConsentAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ConsentResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    private static async Task<BusinessSummaryResponse> ReadSummaryAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<BusinessSummaryResponse>(
            JsonOptions);
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
}

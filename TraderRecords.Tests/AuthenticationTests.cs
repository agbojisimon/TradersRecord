using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TraderRecords.Api.Application.DTOs.Auth;
using Xunit;

namespace TraderRecords.Tests;

public sealed class AuthenticationTests : IAsyncLifetime
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
    public async Task Login_WithValidCredentials_ReturnsBearerToken()
    {
        var client = _factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@example.test";
        const string password = "Trader1!Demo2026";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                fullName = "Login Trader",
                email,
                password,
                businessName = "Login Business"
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await registerResponse.Content
            .ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(registered);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Equal(
            "application/json",
            loginResponse.Content.Headers.ContentType?.MediaType);

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);
        Assert.Equal(registered!.UserId, auth!.UserId);
        Assert.Equal(registered.BusinessId, auth.BusinessId);
        Assert.Equal("Trader", auth.Role);
        Assert.Equal("Bearer", auth.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.True(auth.ExpiresAtUtc > TestApiFactory.FixedNow.UtcDateTime);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorizedProblemDetails(
        bool useKnownEmail)
    {
        var client = _factory.CreateClient();
        var email = $"invalid-{Guid.NewGuid():N}@example.test";
        const string password = "Trader1!Demo2026";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                fullName = "Invalid Login Trader",
                email,
                password,
                businessName = "Invalid Login Business"
            },
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email = useKnownEmail
                    ? email
                    : $"unknown-{Guid.NewGuid():N}@example.test",
                password = "WrongPassword1!"
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            loginResponse.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(
            await loginResponse.Content.ReadAsStringAsync());
        Assert.Equal(401, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "INVALID_CREDENTIALS",
            body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("stackTrace", out _));
        Assert.False(body.RootElement.TryGetProperty("exception", out _));
    }
}

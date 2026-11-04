namespace TraderRecords.Api.Application.DTOs.Auth;

/// <summary>
/// Authentication response contract. BusinessId is null for Lenders because only
/// Traders own a business.
/// </summary>
public sealed class AuthResponse
{
    public Guid UserId { get; init; }

    public Guid? BusinessId { get; init; }

    public string Role { get; init; } = string.Empty;

    public string AccessToken { get; init; } = string.Empty;

    public string TokenType { get; init; } = "Bearer";

    public DateTime ExpiresAtUtc { get; init; }
}

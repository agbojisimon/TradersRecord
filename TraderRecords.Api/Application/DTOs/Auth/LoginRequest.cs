namespace TraderRecords.Api.Application.DTOs.Auth;

/// <summary>
/// Login payload. The password is intentionally not trimmed before verification.
/// </summary>
public sealed class LoginRequest
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

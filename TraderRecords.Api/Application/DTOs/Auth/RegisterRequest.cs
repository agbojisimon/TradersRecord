namespace TraderRecords.Api.Application.DTOs.Auth;

/// <summary>
/// Public trader-registration payload. Role and owner identifiers are deliberately
/// absent so callers cannot elevate themselves or select another owner.
/// </summary>
public sealed class RegisterRequest
{
    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string BusinessName { get; init; } = string.Empty;
}

using TraderRecords.Api.Domain.Enums;

namespace TraderRecords.Api.Domain.Entities;

/// <summary>
/// A Trader owns at most one Business. A Lender owns none, so Business is optional.
/// PasswordHash is persisted but must never be returned or logged.
/// </summary>
public sealed class User
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Business? Business { get; set; }
}

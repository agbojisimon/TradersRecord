namespace TraderRecords.Api.Domain.Entities;

/// <summary>
/// Consent is a current-state row rather than a full audit history. A null
/// RevokedAtUtc means the lender currently has summary access.
/// </summary>
public sealed class Consent
{
    public Guid Id { get; set; }

    public Guid BusinessId { get; set; }

    public Guid LenderUserId { get; set; }

    public DateTime GrantedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Business Business { get; set; } = null!;

    public User Lender { get; set; } = null!;
}

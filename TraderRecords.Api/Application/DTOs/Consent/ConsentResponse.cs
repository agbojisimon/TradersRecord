namespace TraderRecords.Api.Application.DTOs.Consent;

/// <summary>
/// Current-state consent for one business/lender pair. A null RevokedAtUtc means
/// the lender currently has summary access.
/// </summary>
public sealed class ConsentResponse
{
    public Guid BusinessId { get; init; }

    public Guid LenderId { get; init; }

    public bool IsActive { get; init; }

    public DateTime GrantedAtUtc { get; init; }

    public DateTime? RevokedAtUtc { get; init; }
}

namespace TraderRecords.Api.Domain.Entities;

/// <summary>
/// A Business is owned by exactly one Trader and is the ownership boundary for
/// transactions and lender consents. Currency is fixed to NGN in v1.
/// </summary>
public sealed class Business
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Currency { get; set; } = "NGN";

    public DateTime CreatedAtUtc { get; set; }

    public User Owner { get; set; } = null!;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public ICollection<Consent> Consents { get; set; } = new List<Consent>();
}

namespace TraderRecords.Api.Domain.Enums;

/// <summary>
/// Explicit numeric values keep the contract stable even if the enum is ever
/// reordered. EF persists the name, not the number, so the database remains readable.
/// </summary>
public enum UserRole
{
    Trader = 1,
    Lender = 2
}

namespace TraderRecords.Api.Domain.Enums;

/// <summary>
/// Sale increases cash flow; Expense decreases it. The numeric values are part of
/// the stated contract, while EF stores the names as strings for readability.
/// </summary>
public enum TransactionType
{
    Sale = 1,
    Expense = 2
}

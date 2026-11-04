namespace TraderRecords.Api.Application.Common;

/// <summary>
/// Classifies expected failures without leaking HTTP status codes into the
/// application layer. Controllers map these values to the public API contract.
/// </summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

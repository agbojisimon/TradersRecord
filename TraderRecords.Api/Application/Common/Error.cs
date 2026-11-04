namespace TraderRecords.Api.Application.Common;

/// <summary>
/// Represents an expected application failure. Field errors are optional and
/// allow validation failures to be translated into ProblemDetails extensions.
/// </summary>
public sealed class Error
{
    public Error(
        string code,
        string message,
        ErrorType type,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        Code = code;
        Message = message;
        Type = type;
        FieldErrors = fieldErrors;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorType Type { get; }

    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    public static Error Validation(
        string message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new("VALIDATION_FAILED", message, ErrorType.Validation, fieldErrors);

    public static Error Unauthorized(string code, string message) =>
        new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) =>
        new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) =>
        new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) =>
        new(code, message, ErrorType.Conflict);

    public static Error Unavailable(string code, string message) =>
        new(code, message, ErrorType.Unavailable);
}

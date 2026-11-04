namespace TraderRecords.Api.Infrastructure.Authentication;

/// <summary>
/// Stable claim names used by both token creation and validation. Keeping these
/// explicit avoids the framework's default inbound claim-name mapping.
/// </summary>
public static class JwtClaimNames
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string Jti = "jti";
    public const string BusinessId = "businessId";
}

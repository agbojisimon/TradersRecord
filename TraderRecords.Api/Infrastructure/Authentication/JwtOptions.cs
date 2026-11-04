namespace TraderRecords.Api.Infrastructure.Authentication;

/// <summary>
/// Validated JWT settings. The signing key is supplied outside source control
/// through user-secrets or the Jwt__SigningKey environment variable.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string DefaultIssuer = "TraderRecords.Api";
    public const string DefaultAudience = "TraderRecords.Demo";
    public const int DefaultExpiryMinutes = 60;

    public string Issuer { get; set; } = DefaultIssuer;

    public string Audience { get; set; } = DefaultAudience;

    public int ExpiryMinutes { get; set; } = DefaultExpiryMinutes;

    public string SigningKey { get; set; } = string.Empty;
}

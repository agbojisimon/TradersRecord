using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TraderRecords.Api.Application.DTOs.Auth;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;

namespace TraderRecords.Api.Infrastructure.Authentication;

/// <summary>
/// The only component that knows how access tokens are signed. Ownership checks
/// still use the subject claim and database records, never the businessId claim.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public AuthResponse Create(User user, Guid? businessId)
    {
        var issuedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = issuedAtUtc.AddMinutes(_options.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, user.Id.ToString()),
            new(JwtClaimNames.Role, user.Role.ToString()),
            new(JwtClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (businessId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.BusinessId, businessId.Value.ToString()));
        }

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAtUtc,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new AuthResponse
        {
            UserId = user.Id,
            BusinessId = businessId,
            Role = user.Role.ToString(),
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            TokenType = "Bearer",
            ExpiresAtUtc = expiresAtUtc
        };
    }
}

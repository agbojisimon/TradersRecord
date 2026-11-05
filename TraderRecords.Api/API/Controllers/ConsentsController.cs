using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TraderRecords.Api.API.ErrorHandling;
using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Consent;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Infrastructure.Authentication;

namespace TraderRecords.Api.API.Controllers;

/// <summary>
/// Trader-owned lender consent management. The trader identity comes from the
/// validated subject claim and the business is resolved by ownership.
/// </summary>
[ApiController]
[Authorize(Roles = "Trader")]
[Route("api/businesses/me/consents")]
public sealed class ConsentsController : ControllerBase
{
    private readonly IConsentService _consentService;

    public ConsentsController(IConsentService consentService)
    {
        _consentService = consentService;
    }

    [HttpPut("{lenderId:guid}")]
    [ProducesResponseType(typeof(ConsentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Grant(Guid lenderId, CancellationToken ct)
    {
        var subject = User.FindFirstValue(JwtClaimNames.Subject);

        if (!Guid.TryParse(subject, out var traderUserId))
        {
            return this.ToActionResult(
                Error.Unauthorized("UNAUTHORIZED", "Authentication is required."));
        }

        var result = await _consentService.GrantAsync(traderUserId, lenderId, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error);
    }

    [HttpDelete("{lenderId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(Guid lenderId, CancellationToken ct)
    {
        var subject = User.FindFirstValue(JwtClaimNames.Subject);

        if (!Guid.TryParse(subject, out var traderUserId))
        {
            return this.ToActionResult(
                Error.Unauthorized("UNAUTHORIZED", "Authentication is required."));
        }

        var result = await _consentService.RevokeAsync(traderUserId, lenderId, ct);

        return result.IsSuccess
            ? NoContent()
            : this.ToActionResult(result.Error);
    }
}

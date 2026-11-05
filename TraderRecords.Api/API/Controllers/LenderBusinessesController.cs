using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TraderRecords.Api.API.ErrorHandling;
using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Statistics;
using TraderRecords.Api.Application.DTOs.Summary;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Infrastructure.Authentication;

namespace TraderRecords.Api.API.Controllers;

/// <summary>
/// Lender-facing business reads. Every request rechecks the current persisted
/// consent row, so revoking access denies later summary requests immediately.
/// </summary>
[ApiController]
[Authorize(Roles = "Lender")]
[Route("api/lender/businesses")]
public sealed class LenderBusinessesController : ControllerBase
{
    private readonly ISummaryService _summaryService;

    public LenderBusinessesController(ISummaryService summaryService)
    {
        _summaryService = summaryService;
    }

    [HttpGet("{businessId:guid}/summary")]
    [ProducesResponseType(typeof(BusinessSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(
        Guid businessId,
        CancellationToken ct)
    {
        var subject = User.FindFirstValue(JwtClaimNames.Subject);

        if (!Guid.TryParse(subject, out var lenderUserId))
        {
            return this.ToActionResult(
                Error.Unauthorized("UNAUTHORIZED", "Authentication is required."));
        }

        var result = await _summaryService.GetForLenderAsync(
            lenderUserId,
            businessId,
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error);
    }

    /// <summary>
    /// Returns consent-gated cash-flow statistics for an inclusive Lagos date
    /// range, grouped by day, week, or month.
    /// </summary>
    [HttpGet("{businessId:guid}/statistics")]
    [ProducesResponseType(typeof(PeriodStatisticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatistics(
        Guid businessId,
        [FromQuery(Name = "from")] DateOnly? fromLocalDate,
        [FromQuery(Name = "to")] DateOnly? toLocalDate,
        [FromQuery(Name = "groupBy")] string? groupBy,
        CancellationToken ct)
    {
        var subject = User.FindFirstValue(JwtClaimNames.Subject);

        if (!Guid.TryParse(subject, out var lenderUserId))
        {
            return this.ToActionResult(
                Error.Unauthorized("UNAUTHORIZED", "Authentication is required."));
        }

        var result = await _summaryService.GetStatisticsForLenderAsync(
            lenderUserId,
            businessId,
            fromLocalDate,
            toLocalDate,
            groupBy,
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error);
    }
}

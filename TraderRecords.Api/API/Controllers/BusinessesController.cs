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
/// Trader-owned business reads. Ownership comes from the validated subject claim,
/// so the route never accepts another trader's business ID.
/// </summary>
[ApiController]
[Authorize(Roles = "Trader")]
[Route("api/businesses")]
public sealed class BusinessesController : ControllerBase
{
    private readonly ISummaryService _summaryService;

    public BusinessesController(ISummaryService summaryService)
    {
        _summaryService = summaryService;
    }

    /// <summary>
    /// Returns the authenticated trader's fixed eight-week cash-flow summary.
    /// </summary>
    [HttpGet("me/summary")]
    [ProducesResponseType(typeof(BusinessSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMySummary(CancellationToken ct)
    {
        var subject = User.FindFirstValue(JwtClaimNames.Subject);

        if (!Guid.TryParse(subject, out var traderUserId))
        {
            return this.ToActionResult(
                Error.Unauthorized("UNAUTHORIZED", "Authentication is required."));
        }

        var result = await _summaryService.GetForTraderAsync(traderUserId, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error);
    }

    /// <summary>
    /// Returns the authenticated trader's cash-flow statistics for an inclusive
    /// Lagos date range, grouped by day, week, or month.
    /// </summary>
    [HttpGet("me/statistics")]
    [ProducesResponseType(typeof(PeriodStatisticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyStatistics(
        [FromQuery(Name = "from")] DateOnly? fromLocalDate,
        [FromQuery(Name = "to")] DateOnly? toLocalDate,
        [FromQuery(Name = "groupBy")] string? groupBy,
        CancellationToken ct)
    {
        var subject = User.FindFirstValue(JwtClaimNames.Subject);

        if (!Guid.TryParse(subject, out var traderUserId))
        {
            return this.ToActionResult(
                Error.Unauthorized("UNAUTHORIZED", "Authentication is required."));
        }

        var result = await _summaryService.GetStatisticsForTraderAsync(
            traderUserId,
            fromLocalDate,
            toLocalDate,
            groupBy,
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error);
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TraderRecords.Api.API.ErrorHandling;
using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Sync;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Infrastructure.Authentication;

namespace TraderRecords.Api.API.Controllers;

/// <summary>
/// Trader-only transaction synchronisation. Ownership comes from the validated
/// subject claim, never from a business ID in the request body.
/// </summary>
[ApiController]
[Authorize(Roles = "Trader")]
[Route("api/transactions")]
public sealed class TransactionsController : ControllerBase
{
    public const int MaximumRequestBodyBytes = 256 * 1024;

    private readonly ITransactionSyncService _syncService;

    public TransactionsController(ITransactionSyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>
    /// Accepts a 1-100 item batch. Trader role required. Returns 200 even when every
    /// readable item is rejected; only envelope, ownership and transport failures
    /// use non-200 status codes.
    /// </summary>
    [HttpPost("sync")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [ProducesResponseType(typeof(SyncBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Sync(
        [FromBody] SyncBatchRequest request,
        CancellationToken ct)
    {
        var subject = User.FindFirstValue(JwtClaimNames.Subject);

        if (!Guid.TryParse(subject, out var traderUserId))
        {
            return this.ToActionResult(
                Error.Unauthorized("UNAUTHORIZED", "Authentication is required."));
        }

        var result = await _syncService.SyncAsync(traderUserId, request, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error);
    }
}

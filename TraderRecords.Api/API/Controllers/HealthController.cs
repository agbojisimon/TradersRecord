using Microsoft.AspNetCore.Mvc;

namespace TraderRecords.Api.API.Controllers;

/// <summary>
/// Gate 1 liveness endpoint used to prove the controller pipeline and OpenAPI
/// document are working. It is intentionally not a business endpoint.
/// </summary>
[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new { status = "ok" });
    }
}

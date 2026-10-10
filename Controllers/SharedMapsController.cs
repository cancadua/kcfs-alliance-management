using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AllianceRewards.Api.Controllers;

/// <summary>
/// Shared Uncharted Waters map state, without login. Anyone with the code can read and change it;
/// it expires (and is deleted) a fixed time after creation.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/shared-maps")]
public class SharedMapsController(SharedMapService maps) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("shared-map-create")]
    public async Task<ActionResult<SharedMapResponse>> Create(CreateSharedMapRequest? req)
    {
        var (map, code, error) = await maps.CreateAsync(req?.State);
        if (error is not null) return BadRequest(new { error });

        SetETag(map!.Version);
        return CreatedAtAction(nameof(Get), new { code }, SharedMapService.ToResponse(map, code!));
    }

    /// <summary>Supports If-None-Match with the ETag from a previous response (304 when unchanged), for cheap polling.</summary>
    [HttpGet("{code}")]
    [EnableRateLimiting("shared-map")]
    public async Task<ActionResult<SharedMapResponse>> Get(string code)
    {
        var map = await maps.FindAsync(code);
        if (map is null) return NotFound(new { error = "Map not found or expired." });

        var etag = SetETag(map.Version);
        if (Request.Headers.IfNoneMatch.Contains(etag)) return StatusCode(StatusCodes.Status304NotModified);
        return SharedMapService.ToResponse(map, code);
    }

    [HttpPatch("{code}")]
    [EnableRateLimiting("shared-map")]
    public async Task<ActionResult<SharedMapResponse>> Patch(string code, PatchSharedMapRequest req)
    {
        var (map, error) = await maps.PatchAsync(code, req);
        if (error is not null) return BadRequest(new { error });
        if (map is null) return NotFound(new { error = "Map not found or expired." });

        SetETag(map.Version);
        return SharedMapService.ToResponse(map, code);
    }

    private string SetETag(long version)
    {
        var etag = $"\"{version}\"";
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "no-cache";
        return etag;
    }
}

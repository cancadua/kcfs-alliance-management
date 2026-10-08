using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/recommendations")]
public class RecommendationsController(AllianceAccessService access, RecommendationService recommendations) : ControllerBase
{
    /// <summary>Ranked MVP candidates across the user's alliances (or one, via ?allianceId=).</summary>
    [HttpGet("mvp")]
    public async Task<ActionResult<List<MvpRecommendation>>> Mvp([FromQuery] Guid? allianceId, [FromQuery] int? top)
    {
        List<Guid> ids;
        if (allianceId is not null)
        {
            if (!await access.IsMemberAsync(allianceId.Value)) return NotFound();
            ids = [allianceId.Value];
        }
        else
        {
            ids = await access.MyAllianceIds().ToListAsync();
        }

        var result = await recommendations.GetMvpAsync(ids);
        return top is > 0 ? result.Take(top.Value).ToList() : result;
    }
}

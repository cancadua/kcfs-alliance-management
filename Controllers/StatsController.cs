using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Models;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/stats")]
public class StatsController(AppDbContext db, AllianceAccessService access) : ControllerBase
{
    /// <summary>MVP counts per player and per tier for one alliance.</summary>
    [HttpGet("{allianceId:guid}")]
    public async Task<ActionResult<AllianceStats>> ForAlliance(Guid allianceId)
    {
        if (!await access.IsManagerAsync(allianceId)) return NotFound();

        var players = await db.Players
            .Where(p => p.AllianceId == allianceId)
            .OrderBy(p => p.Name)
            .Select(p => new PlayerStats(
                p.Id,
                p.Name,
                p.IsActive,
                p.Rewards.Count(r => r.Type == RewardType.Normal),
                p.Rewards.Count(r => r.Type == RewardType.Earl),
                p.Rewards.Count(r => r.Type == RewardType.Duke),
                p.Rewards.Count,
                p.Rewards.Max(r => (DateTime?)r.AwardedAt)))
            .ToListAsync();

        var totals = new RewardTotals(
            players.Sum(p => p.Normal),
            players.Sum(p => p.Earl),
            players.Sum(p => p.Duke),
            players.Sum(p => p.Total));

        return new AllianceStats(allianceId, totals, players);
    }
}

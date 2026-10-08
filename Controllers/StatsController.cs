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
    /// <summary>Reward counts per player and per type for one alliance.</summary>
    [HttpGet("{allianceId:guid}")]
    public async Task<ActionResult<AllianceStats>> ForAlliance(Guid allianceId)
    {
        if (!await access.IsMemberAsync(allianceId)) return NotFound();

        var players = await db.Players
            .Where(p => p.AllianceId == allianceId)
            .OrderBy(p => p.Name)
            .Select(p => new PlayerStats(
                p.Id,
                p.Name,
                p.IsActive,
                p.Rewards.Count(r => r.Type == RewardType.Normal),
                p.Rewards.Count(r => r.Type == RewardType.Blue),
                p.Rewards.Count(r => r.Type == RewardType.Purple),
                p.Rewards.Count(r => r.Type == RewardType.Mvp),
                p.Rewards.Count,
                p.Rewards.Max(r => (DateTime?)r.AwardedAt)))
            .ToListAsync();

        var totals = new RewardTotals(
            players.Sum(p => p.Normal),
            players.Sum(p => p.Blue),
            players.Sum(p => p.Purple),
            players.Sum(p => p.Mvp),
            players.Sum(p => p.Total));

        return new AllianceStats(allianceId, totals, players);
    }
}

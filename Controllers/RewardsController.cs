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
[Route("api/rewards")]
public class RewardsController(AppDbContext db, AllianceAccessService access) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RewardResponse>>> List([FromQuery] Guid? allianceId)
    {
        var q = db.Rewards.Where(r => access.MyAllianceIds().Contains(r.Player!.AllianceId));
        if (allianceId is not null) q = q.Where(r => r.Player!.AllianceId == allianceId);

        return await q.OrderByDescending(r => r.AwardedAt)
            .Select(r => new RewardResponse(r.Id, r.PlayerId, r.Player!.Name, r.EventId, r.Type, r.AwardedAt))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<RewardResponse>> Create(CreateRewardRequest req)
    {
        var player = await db.Players.FirstOrDefaultAsync(p =>
            p.Id == req.PlayerId && access.MyAllianceIds().Contains(p.AllianceId));
        if (player is null) return NotFound(new { error = "Player not found." });

        if (req.EventId is not null &&
            !await db.Events.AnyAsync(e => e.Id == req.EventId && e.AllianceId == player.AllianceId))
            return BadRequest(new { error = "Event does not belong to the player's alliance." });

        var reward = new Reward
        {
            PlayerId = player.Id,
            EventId = req.EventId,
            Type = req.Type,
            AwardedAt = (req.AwardedAt ?? DateTime.UtcNow).ToUniversalTime(),
        };
        db.Rewards.Add(reward);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(ForPlayer), new { playerId = player.Id },
            new RewardResponse(reward.Id, player.Id, player.Name, reward.EventId, reward.Type, reward.AwardedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var reward = await db.Rewards.FirstOrDefaultAsync(r =>
            r.Id == id && access.MyAllianceIds().Contains(r.Player!.AllianceId));
        if (reward is null) return NotFound();

        db.Rewards.Remove(reward);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("player/{playerId:guid}")]
    public async Task<ActionResult<List<RewardResponse>>> ForPlayer(Guid playerId)
    {
        if (!await db.Players.AnyAsync(p => p.Id == playerId && access.MyAllianceIds().Contains(p.AllianceId)))
            return NotFound();

        return await db.Rewards
            .Where(r => r.PlayerId == playerId)
            .OrderByDescending(r => r.AwardedAt)
            .Select(r => new RewardResponse(r.Id, r.PlayerId, r.Player!.Name, r.EventId, r.Type, r.AwardedAt))
            .ToListAsync();
    }
}

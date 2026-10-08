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
[Route("api/players")]
public class PlayersController(AppDbContext db, AllianceAccessService access) : ControllerBase
{
    private static PlayerResponse ToDto(Player p) =>
        new(p.Id, p.AllianceId, p.Name, p.Activity, p.IsActive, p.CreatedAt);

    [HttpGet]
    public async Task<ActionResult<List<PlayerResponse>>> List([FromQuery] Guid? allianceId)
    {
        var q = db.Players.Where(p => access.MyAllianceIds().Contains(p.AllianceId));
        if (allianceId is not null) q = q.Where(p => p.AllianceId == allianceId);

        return await q.OrderBy(p => p.Name)
            .Select(p => new PlayerResponse(p.Id, p.AllianceId, p.Name, p.Activity, p.IsActive, p.CreatedAt))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<PlayerResponse>> Create(CreatePlayerRequest req)
    {
        if (!await access.IsMemberAsync(req.AllianceId)) return NotFound(new { error = "Alliance not found." });

        var name = req.Name.Trim();
        if (await db.Players.AnyAsync(p => p.AllianceId == req.AllianceId && p.Name == name))
            return Conflict(new { error = "A player with that name already exists in this alliance." });

        var player = new Player { AllianceId = req.AllianceId, Name = name, Activity = req.Activity };
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), new { allianceId = player.AllianceId }, ToDto(player));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<PlayerResponse>> Update(Guid id, UpdatePlayerRequest req)
    {
        var player = await FindOwnedAsync(id);
        if (player is null) return NotFound();

        if (req.Name is not null)
        {
            var name = req.Name.Trim();
            if (await db.Players.AnyAsync(p => p.AllianceId == player.AllianceId && p.Name == name && p.Id != id))
                return Conflict(new { error = "A player with that name already exists in this alliance." });
            player.Name = name;
        }
        if (req.Activity is not null) player.Activity = req.Activity.Value;
        if (req.IsActive is not null) player.IsActive = req.IsActive.Value;

        await db.SaveChangesAsync();
        return ToDto(player);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var player = await FindOwnedAsync(id);
        if (player is null) return NotFound();

        db.Players.Remove(player);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<Player?> FindOwnedAsync(Guid id) =>
        db.Players.FirstOrDefaultAsync(p => p.Id == id && access.MyAllianceIds().Contains(p.AllianceId));
}

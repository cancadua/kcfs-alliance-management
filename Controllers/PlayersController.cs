using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Models;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/players")]
public class PlayersController(AppDbContext db, AllianceAccessService access, PlayerLinkService links) : ControllerBase
{
    private static PlayerResponse ToDto(Player p) =>
        new(p.Id, p.AllianceId, p.Name, p.Activity, p.IsActive, p.CreatedAt, p.UserId, p.User?.Username);

    /// <summary>Owner/Leader see every player of their alliances; plain Members see only their own player.</summary>
    [HttpGet]
    public async Task<ActionResult<List<PlayerResponse>>> List([FromQuery] Guid? allianceId)
    {
        var q = db.Players.Where(p => access.VisiblePlayerIds().Contains(p.Id));
        if (allianceId is not null) q = q.Where(p => p.AllianceId == allianceId);

        return await q.OrderBy(p => p.Name)
            .Select(p => new PlayerResponse(
                p.Id, p.AllianceId, p.Name, p.Activity, p.IsActive, p.CreatedAt, p.UserId, p.User!.Username))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<PlayerResponse>> Create(CreatePlayerRequest req)
    {
        if (!await access.IsManagerAsync(req.AllianceId)) return NotFound(new { error = "Alliance not found." });

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
        var player = await FindManagedAsync(id);
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

    /// <summary>
    /// Deleting a player linked to an account requires ?confirm=true; the account then also leaves the alliance
    /// (unless it is the Owner).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool confirm = false)
    {
        var player = await FindManagedAsync(id);
        if (player is null) return NotFound();

        if (player.UserId is not null && !confirm)
            return Conflict(new
            {
                error = "This player is linked to an account, which will also be removed from the alliance. " +
                        "Repeat with ?confirm=true to proceed.",
            });

        await links.DetachForDeletionAsync(player, access.UserId);
        db.Players.Remove(player);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Generates a one-time link code for an unlinked player; any previous code stops working.</summary>
    [HttpPost("{id:guid}/link-code")]
    public async Task<ActionResult<LinkCodeResponse>> CreateLinkCode(Guid id)
    {
        var player = await FindManagedAsync(id);
        if (player is null) return NotFound();
        if (player.UserId is not null) return Conflict(new { error = "This player is already linked to an account." });

        var (code, expiresAt) = await links.CreateCodeAsync(player, access.UserId);
        return new LinkCodeResponse(code, expiresAt);
    }

    /// <summary>Links the current account to the player the code was issued for.</summary>
    [HttpPost("claim")]
    [EnableRateLimiting("link")]
    public async Task<ActionResult<PlayerResponse>> Claim(ClaimPlayerRequest req)
    {
        var player = await links.FindPlayerByCodeAsync(req.Code);
        if (player is null) return BadRequest(new { error = "The code is invalid or has expired." });

        var error = await links.LinkAsync(player, access.UserId, PlayerLinkMethod.Code, access.UserId);
        if (error is not null) return Conflict(new { error });

        await db.Entry(player).Reference(p => p.User).LoadAsync();
        return ToDto(player);
    }

    /// <summary>Removes the account link; the player, its history and the account's membership stay.</summary>
    [HttpDelete("{id:guid}/link")]
    public async Task<IActionResult> Unlink(Guid id)
    {
        var player = await FindManagedAsync(id);
        if (player is null) return NotFound();
        if (player.UserId is null) return Conflict(new { error = "This player is not linked to an account." });

        links.Unlink(player, PlayerLinkMethod.Unlink, access.UserId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<Player?> FindManagedAsync(Guid id) =>
        db.Players.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == id && access.ManagedAllianceIds().Contains(p.AllianceId));
}

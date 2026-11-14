using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Infrastructure;
using AllianceRewards.Api.Models;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Controllers;

/// <summary>Invites addressed to the current user: an account is only linked once its owner accepts.</summary>
[ApiController]
[Authorize]
[Route("api/invites")]
public class InvitesController(AppDbContext db, AllianceAccessService access, PlayerLinkService links) : ControllerBase
{
    /// <summary>The current user's invites, newest first.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<AllianceInviteResponse>>> Mine()
    {
        var uid = access.UserId;
        return this.ListResult(await links.ProjectInvites(db.AllianceInvites
                .Where(i => i.UserId == uid)
                .OrderByDescending(i => i.CreatedAt))
            .ToListAsync());
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id)
    {
        var invite = await FindPendingOwnAsync(id);
        if (invite is null) return NotFound();

        var player = invite.PlayerId is { } playerId ? await db.Players.FirstOrDefaultAsync(p => p.Id == playerId) : null;
        if (player is null) return Conflict(new { error = "The player no longer exists." });

        // Only the Owner may grant Leader; fall back to Member if the inviter is no longer the Owner.
        var role = invite.Role != AllianceRole.Member &&
                   !await db.Alliances.AnyAsync(a => a.Id == invite.AllianceId && a.OwnerId == invite.InvitedById)
            ? AllianceRole.Member
            : invite.Role;

        var error = await links.LinkAsync(player, invite.UserId, PlayerLinkMethod.Invite, invite.InvitedById, role);
        if (error is not null) return Conflict(new { error });
        return NoContent();
    }

    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id)
    {
        var invite = await FindPendingOwnAsync(id);
        if (invite is null) return NotFound();

        invite.Status = AllianceInviteStatus.Declined;
        invite.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<AllianceInvite?> FindPendingOwnAsync(Guid id)
    {
        var uid = access.UserId;
        return db.AllianceInvites.FirstOrDefaultAsync(i =>
            i.Id == id && i.UserId == uid && i.Status == AllianceInviteStatus.Pending);
    }
}

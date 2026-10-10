using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Infrastructure;
using AllianceRewards.Api.Models;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Controllers;

/// <summary>Requests from users to be linked to a player ("this is me"), resolved by Owner/Leader.</summary>
[ApiController]
[Authorize]
[Route("api/link-requests")]
public class LinkRequestsController(AppDbContext db, AllianceAccessService access, PlayerLinkService links) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("link")]
    public async Task<ActionResult<LinkRequestResponse>> Create(CreateLinkRequestRequest req)
    {
        var uid = access.UserId;

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == req.PlayerId && p.AllianceId == req.AllianceId);
        if (player is null) return NotFound(new { error = "Player not found." });
        if (player.UserId is not null) return Conflict(new { error = "This player is already linked to an account." });

        if (await db.Players.AnyAsync(p => p.AllianceId == req.AllianceId && p.UserId == uid))
            return Conflict(new { error = "You are already linked to a player in this alliance." });
        if (await db.LinkRequests.AnyAsync(r =>
                r.AllianceId == req.AllianceId && r.UserId == uid && r.Status == LinkRequestStatus.Pending))
            return Conflict(new { error = "You already have a pending request in this alliance." });

        var message = string.IsNullOrWhiteSpace(req.Message) ? null : req.Message.Trim();
        var request = new LinkRequest
        {
            AllianceId = req.AllianceId,
            PlayerId = player.Id,
            PlayerName = player.Name,
            UserId = uid,
            Message = message,
        };
        db.LinkRequests.Add(request);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Mine), null, await Project(db.LinkRequests.Where(r => r.Id == request.Id)).FirstAsync());
    }

    /// <summary>The current user's requests, newest first.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<LinkRequestResponse>>> Mine()
    {
        var uid = access.UserId;
        return this.ListResult(await Project(db.LinkRequests.Where(r => r.UserId == uid).OrderByDescending(r => r.CreatedAt)).ToListAsync());
    }

    /// <summary>Requests for an alliance (Owner/Leader). Pending only unless ?status= is given.</summary>
    [HttpGet]
    public async Task<ActionResult<List<LinkRequestResponse>>> ForAlliance(
        [FromQuery] Guid allianceId, [FromQuery] LinkRequestStatus status = LinkRequestStatus.Pending)
    {
        if (!await access.IsManagerAsync(allianceId)) return NotFound();

        return this.ListResult(await Project(db.LinkRequests
                .Where(r => r.AllianceId == allianceId && r.Status == status)
                .OrderBy(r => r.CreatedAt))
            .ToListAsync());
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id)
    {
        var request = await FindPendingManagedAsync(id);
        if (request is null) return NotFound();

        // A deleted player rejects its pending requests, so the player is normally still there.
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == request.PlayerId);
        if (player is null) return Conflict(new { error = "The player no longer exists." });

        var error = await links.LinkAsync(player, request.UserId, PlayerLinkMethod.Request, access.UserId);
        if (error is not null) return Conflict(new { error });
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id)
    {
        var request = await FindPendingManagedAsync(id);
        if (request is null) return NotFound();

        Resolve(request, LinkRequestStatus.Rejected);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Withdraws the current user's own pending request.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var uid = access.UserId;
        var request = await db.LinkRequests.FirstOrDefaultAsync(r =>
            r.Id == id && r.UserId == uid && r.Status == LinkRequestStatus.Pending);
        if (request is null) return NotFound();

        Resolve(request, LinkRequestStatus.Cancelled);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private void Resolve(LinkRequest request, LinkRequestStatus status)
    {
        request.Status = status;
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedById = access.UserId;
    }

    private Task<LinkRequest?> FindPendingManagedAsync(Guid id) =>
        db.LinkRequests.FirstOrDefaultAsync(r =>
            r.Id == id && r.Status == LinkRequestStatus.Pending && access.ManagedAllianceIds().Contains(r.AllianceId));

    private static IQueryable<LinkRequestResponse> Project(IQueryable<LinkRequest> q) =>
        q.Select(r => new LinkRequestResponse(
            r.Id, r.AllianceId, r.Alliance!.Name, r.PlayerId, r.PlayerName, r.UserId, r.User!.Username,
            r.Message, r.Status, r.CreatedAt, r.ResolvedAt));
}

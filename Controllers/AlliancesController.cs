using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Infrastructure;
using AllianceRewards.Api.Models;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AllianceRewards.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/alliances")]
public class AlliancesController(AppDbContext db, AllianceAccessService access, PlayerLinkService links) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MyAllianceResponse>>> List()
    {
        var uid = access.UserId;
        return this.ListResult(await db.AllianceMembers
            .Where(m => m.UserId == uid)
            .OrderBy(m => m.Alliance!.Name)
            .Select(m => new MyAllianceResponse(
                m.AllianceId, m.Alliance!.Name, m.Alliance.OwnerId, m.Role, m.Alliance.CreatedAt, m.Alliance.Members.Count,
                m.Alliance.Players.Where(p => p.UserId == uid).Select(p => (Guid?)p.Id).FirstOrDefault(),
                m.Alliance.Players.Where(p => p.UserId == uid).Select(p => p.Name).FirstOrDefault()))
            .ToListAsync());
    }

    [HttpPost]
    public async Task<ActionResult<AllianceResponse>> Create(CreateAllianceRequest req)
    {
        var alliance = new Alliance { Name = req.Name.Trim(), OwnerId = access.UserId };
        alliance.Members.Add(new AllianceMember { UserId = access.UserId, Role = AllianceRole.Owner });
        db.Alliances.Add(alliance);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = alliance.Id },
            new AllianceResponse(alliance.Id, alliance.Name, alliance.OwnerId, alliance.CreatedAt, 1));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AllianceResponse>> Get(Guid id)
    {
        if (!await access.IsInAllianceAsync(id)) return NotFound();

        var a = await db.Alliances
            .Where(x => x.Id == id)
            .Select(x => new AllianceResponse(x.Id, x.Name, x.OwnerId, x.CreatedAt, x.Members.Count))
            .FirstAsync();
        return a;
    }

    /// <summary>Finds alliances by name so a user can request a link. Exposes only id and name.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<AllianceSearchResult>>> Search([FromQuery] string name)
    {
        var term = name?.Trim() ?? "";
        if (term.Length < 2) return BadRequest(new { error = "Search term must have at least 2 characters." });

        var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return this.ListResult(await db.Alliances
            .Where(a => EF.Functions.ILike(a.Name, pattern))
            .OrderBy(a => a.Name)
            .Take(20)
            .Select(a => new AllianceSearchResult(a.Id, a.Name))
            .ToListAsync());
    }

    /// <summary>Names of players without an account, for building a link request. Open to any signed-in user.</summary>
    [HttpGet("{id:guid}/unlinked-players")]
    public async Task<ActionResult<List<UnlinkedPlayerResponse>>> UnlinkedPlayers(Guid id)
    {
        if (!await db.Alliances.AnyAsync(a => a.Id == id)) return NotFound();

        return this.ListResult(await db.Players
            .Where(p => p.AllianceId == id && p.UserId == null)
            .OrderBy(p => p.Name)
            .Select(p => new UnlinkedPlayerResponse(p.Id, p.Name))
            .ToListAsync());
    }

    /// <summary>
    /// Invites a registered account (email or username) to be linked to a player of the alliance; nothing changes
    /// until the account accepts (201 with the invite). Inviting your own account links it at once (204).
    /// Owner may grant Leader or Member, a Leader only Member. An account already in the alliance keeps its role.
    /// </summary>
    [HttpPost("{id:guid}/invite")]
    [EnableRateLimiting("link")]
    public async Task<IActionResult> Invite(Guid id, InviteRequest req)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();

        if (req.Role == AllianceRole.Owner) return BadRequest(new { error = "Cannot invite another Owner." });
        if (req.Role != AllianceRole.Member && !await access.IsOwnerAsync(id)) return Forbid();

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == req.PlayerId && p.AllianceId == id);
        if (player is null) return NotFound(new { error = "Player not found." });

        var login = req.User.Trim();
        var email = login.ToLowerInvariant();
        var user = login.Contains('@')
            ? await db.Users.FirstOrDefaultAsync(u => u.Email == email)
            : await db.Users.FirstOrDefaultAsync(u => u.Username == login);
        if (user is null) return NotFound(new { error = "No registered user with that email or username." });

        // Your own account needs no consent.
        if (user.Id == access.UserId)
        {
            var error = await links.LinkAsync(player, user.Id, PlayerLinkMethod.Invite, access.UserId, req.Role);
            if (error is not null) return Conflict(new { error });
            return NoContent();
        }

        if (player.UserId is not null) return Conflict(new { error = "This player is already linked to an account." });
        if (await db.Players.AnyAsync(p => p.AllianceId == id && p.UserId == user.Id))
            return Conflict(new { error = "This account is already linked to another player in this alliance." });
        if (await db.AllianceInvites.AnyAsync(i => i.Status == AllianceInviteStatus.Pending &&
                                                   (i.PlayerId == player.Id || (i.AllianceId == id && i.UserId == user.Id))))
            return Conflict(new { error = "There is already a pending invite for this player or account." });

        var invite = new AllianceInvite
        {
            AllianceId = id,
            PlayerId = player.Id,
            PlayerName = player.Name,
            UserId = user.Id,
            Role = req.Role,
            InvitedById = access.UserId,
        };
        db.AllianceInvites.Add(invite);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "There is already a pending invite for this player or account." });
        }

        var response = await links.ProjectInvites(db.AllianceInvites.Where(i => i.Id == invite.Id)).FirstAsync();
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Invites sent by the alliance (Owner/Leader). Pending only unless ?status= is given.</summary>
    [HttpGet("{id:guid}/invites")]
    public async Task<ActionResult<List<AllianceInviteResponse>>> Invites(
        Guid id, [FromQuery] AllianceInviteStatus status = AllianceInviteStatus.Pending)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();

        return this.ListResult(await links.ProjectInvites(db.AllianceInvites
                .Where(i => i.AllianceId == id && i.Status == status)
                .OrderBy(i => i.CreatedAt))
            .ToListAsync());
    }

    /// <summary>Withdraws a pending invite (Owner/Leader).</summary>
    [HttpDelete("{id:guid}/invites/{inviteId:guid}")]
    public async Task<IActionResult> CancelInvite(Guid id, Guid inviteId)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();

        var invite = await db.AllianceInvites.FirstOrDefaultAsync(i =>
            i.Id == inviteId && i.AllianceId == id && i.Status == AllianceInviteStatus.Pending);
        if (invite is null) return NotFound();

        invite.Status = AllianceInviteStatus.Cancelled;
        invite.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<MemberResponse>>> Members(Guid id)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();

        return this.ListResult(await db.AllianceMembers
            .Where(m => m.AllianceId == id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new MemberResponse(
                m.UserId, m.User!.Username, m.User.Email, m.Role, m.JoinedAt,
                m.Alliance!.Players.Where(p => p.UserId == m.UserId).Select(p => (Guid?)p.Id).FirstOrDefault(),
                m.Alliance.Players.Where(p => p.UserId == m.UserId).Select(p => p.Name).FirstOrDefault()))
            .ToListAsync());
    }

    [HttpPatch("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> UpdateMemberRole(Guid id, Guid userId, UpdateMemberRoleRequest req)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();
        if (!await access.IsOwnerAsync(id)) return Forbid();

        if (req.Role == AllianceRole.Owner) return BadRequest(new { error = "Cannot assign the Owner role." });

        var member = await db.AllianceMembers.FirstOrDefaultAsync(m => m.AllianceId == id && m.UserId == userId);
        if (member is null) return NotFound();
        if (member.Role == AllianceRole.Owner) return BadRequest(new { error = "Cannot change the Owner's role." });

        member.Role = req.Role;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Renames the member within the alliance, i.e. the player their account is linked to. Leader or higher.</summary>
    [HttpPatch("{id:guid}/members/{userId:guid}/name")]
    public async Task<ActionResult<MemberResponse>> RenameMember(Guid id, Guid userId, RenameMemberRequest req)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();

        var member = await db.AllianceMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.AllianceId == id && m.UserId == userId);
        if (member is null) return NotFound();

        var player = await db.Players.FirstOrDefaultAsync(p => p.AllianceId == id && p.UserId == userId);
        if (player is null) return Conflict(new { error = "This member is not linked to a player." });

        var name = req.Name.Trim();
        if (await db.Players.AnyAsync(p => p.AllianceId == id && p.Name == name && p.Id != player.Id))
            return Conflict(new { error = "A player with that name already exists in this alliance." });

        player.Name = name;
        await db.SaveChangesAsync();
        return new MemberResponse(
            member.UserId, member.User!.Username, member.User.Email, member.Role, member.JoinedAt, player.Id, player.Name);
    }

    /// <summary>Removes the account from the alliance and unlinks it from its player (the player stays).</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();
        if (!await access.IsOwnerAsync(id)) return Forbid();

        var member = await db.AllianceMembers.FirstOrDefaultAsync(m => m.AllianceId == id && m.UserId == userId);
        if (member is null) return NotFound();
        if (member.Role == AllianceRole.Owner) return BadRequest(new { error = "Cannot remove the Owner." });

        var player = await db.Players.FirstOrDefaultAsync(p => p.AllianceId == id && p.UserId == userId);
        if (player is not null) links.Unlink(player, PlayerLinkMethod.MemberRemoved, access.UserId);

        db.AllianceMembers.Remove(member);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Audit trail of account links and unlinks, newest first.</summary>
    [HttpGet("{id:guid}/link-log")]
    public async Task<ActionResult<List<PlayerLinkLogResponse>>> LinkLog(Guid id)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();

        return this.ListResult(await db.PlayerLinkLogs
            .Where(l => l.AllianceId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new PlayerLinkLogResponse(
                l.Id, l.PlayerId, l.PlayerName,
                l.UserId, db.Users.Where(u => u.Id == l.UserId).Select(u => u.Username).FirstOrDefault(),
                l.Action, l.Method,
                l.ActorId, db.Users.Where(u => u.Id == l.ActorId).Select(u => u.Username).FirstOrDefault(),
                l.CreatedAt))
            .ToListAsync());
    }
}

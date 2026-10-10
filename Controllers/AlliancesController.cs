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
[Route("api/alliances")]
public class AlliancesController(AppDbContext db, AllianceAccessService access, PlayerLinkService links) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MyAllianceResponse>>> List()
    {
        var uid = access.UserId;
        return await db.AllianceMembers
            .Where(m => m.UserId == uid)
            .OrderBy(m => m.Alliance!.Name)
            .Select(m => new MyAllianceResponse(
                m.AllianceId, m.Alliance!.Name, m.Alliance.OwnerId, m.Role, m.Alliance.CreatedAt, m.Alliance.Members.Count,
                m.Alliance.Players.Where(p => p.UserId == uid).Select(p => (Guid?)p.Id).FirstOrDefault(),
                m.Alliance.Players.Where(p => p.UserId == uid).Select(p => p.Name).FirstOrDefault()))
            .ToListAsync();
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
        return await db.Alliances
            .Where(a => EF.Functions.ILike(a.Name, pattern))
            .OrderBy(a => a.Name)
            .Take(20)
            .Select(a => new AllianceSearchResult(a.Id, a.Name))
            .ToListAsync();
    }

    /// <summary>Names of players without an account, for building a link request. Open to any signed-in user.</summary>
    [HttpGet("{id:guid}/unlinked-players")]
    public async Task<ActionResult<List<UnlinkedPlayerResponse>>> UnlinkedPlayers(Guid id)
    {
        if (!await db.Alliances.AnyAsync(a => a.Id == id)) return NotFound();

        return await db.Players
            .Where(p => p.AllianceId == id && p.UserId == null)
            .OrderBy(p => p.Name)
            .Select(p => new UnlinkedPlayerResponse(p.Id, p.Name))
            .ToListAsync();
    }

    /// <summary>
    /// Links a registered account (email or username) to a player of the alliance. Owner may grant Leader or Member,
    /// a Leader only Member. An account already in the alliance keeps its role.
    /// </summary>
    [HttpPost("{id:guid}/invite")]
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

        var error = await links.LinkAsync(player, user.Id, PlayerLinkMethod.Invite, access.UserId, req.Role);
        if (error is not null) return Conflict(new { error });
        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<MemberResponse>>> Members(Guid id)
    {
        if (!await access.IsManagerAsync(id)) return NotFound();

        return await db.AllianceMembers
            .Where(m => m.AllianceId == id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new MemberResponse(
                m.UserId, m.User!.Username, m.User.Email, m.Role, m.JoinedAt,
                m.Alliance!.Players.Where(p => p.UserId == m.UserId).Select(p => (Guid?)p.Id).FirstOrDefault(),
                m.Alliance.Players.Where(p => p.UserId == m.UserId).Select(p => p.Name).FirstOrDefault()))
            .ToListAsync();
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

        return await db.PlayerLinkLogs
            .Where(l => l.AllianceId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new PlayerLinkLogResponse(
                l.Id, l.PlayerId, l.PlayerName,
                l.UserId, db.Users.Where(u => u.Id == l.UserId).Select(u => u.Username).FirstOrDefault(),
                l.Action, l.Method,
                l.ActorId, db.Users.Where(u => u.Id == l.ActorId).Select(u => u.Username).FirstOrDefault(),
                l.CreatedAt))
            .ToListAsync();
    }
}

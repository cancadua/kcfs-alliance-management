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
public class AlliancesController(AppDbContext db, AllianceAccessService access) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MyAllianceResponse>>> List()
    {
        var uid = access.UserId;
        return await db.AllianceMembers
            .Where(m => m.UserId == uid)
            .OrderBy(m => m.Alliance!.Name)
            .Select(m => new MyAllianceResponse(
                m.AllianceId, m.Alliance!.Name, m.Alliance.OwnerId, m.Role, m.Alliance.CreatedAt, m.Alliance.Members.Count))
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
        if (!await access.IsMemberAsync(id)) return NotFound();

        var a = await db.Alliances
            .Where(x => x.Id == id)
            .Select(x => new AllianceResponse(x.Id, x.Name, x.OwnerId, x.CreatedAt, x.Members.Count))
            .FirstAsync();
        return a;
    }

    [HttpPost("{id:guid}/invite")]
    public async Task<IActionResult> Invite(Guid id, InviteRequest req)
    {
        if (!await access.IsMemberAsync(id)) return NotFound();
        if (!await access.IsOwnerAsync(id)) return Forbid();

        if (req.Role == AllianceRole.Owner) return BadRequest(new { error = "Cannot invite another Owner." });

        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null) return NotFound(new { error = "No registered user with that email." });

        if (await db.AllianceMembers.AnyAsync(m => m.AllianceId == id && m.UserId == user.Id))
            return Conflict(new { error = "User is already a member." });

        db.AllianceMembers.Add(new AllianceMember { AllianceId = id, UserId = user.Id, Role = req.Role });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<MemberResponse>>> Members(Guid id)
    {
        if (!await access.IsMemberAsync(id)) return NotFound();

        return await db.AllianceMembers
            .Where(m => m.AllianceId == id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new MemberResponse(m.UserId, m.User!.Username, m.User.Email, m.Role, m.JoinedAt))
            .ToListAsync();
    }

    [HttpPatch("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> UpdateMemberRole(Guid id, Guid userId, UpdateMemberRoleRequest req)
    {
        if (!await access.IsMemberAsync(id)) return NotFound();
        if (!await access.IsOwnerAsync(id)) return Forbid();

        if (req.Role == AllianceRole.Owner) return BadRequest(new { error = "Cannot assign the Owner role." });

        var member = await db.AllianceMembers.FirstOrDefaultAsync(m => m.AllianceId == id && m.UserId == userId);
        if (member is null) return NotFound();
        if (member.Role == AllianceRole.Owner) return BadRequest(new { error = "Cannot change the Owner's role." });

        member.Role = req.Role;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
    {
        if (!await access.IsMemberAsync(id)) return NotFound();
        if (!await access.IsOwnerAsync(id)) return Forbid();

        var member = await db.AllianceMembers.FirstOrDefaultAsync(m => m.AllianceId == id && m.UserId == userId);
        if (member is null) return NotFound();
        if (member.Role == AllianceRole.Owner) return BadRequest(new { error = "Cannot remove the Owner." });

        db.AllianceMembers.Remove(member);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

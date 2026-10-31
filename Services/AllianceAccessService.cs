using System.Security.Claims;
using AllianceRewards.Api.Data;
using AllianceRewards.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Services;

/// <summary>
/// Resolves what the current user may access. Checks are per feature: Owner/Leader manage the alliance,
/// plain Members only see what is explicitly granted to them (currently: their own player and its rewards).
/// </summary>
public class AllianceAccessService(AppDbContext db, IHttpContextAccessor http)
{
    public Guid UserId =>
        Guid.Parse(http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? throw new UnauthorizedAccessException());

    /// <summary>True for Owner/Leader: full access to alliance data and management.</summary>
    public Task<bool> IsManagerAsync(Guid allianceId)
    {
        var uid = UserId;
        return db.AllianceMembers.AnyAsync(m =>
            m.AllianceId == allianceId && m.UserId == uid && m.Role != AllianceRole.Member);
    }

    /// <summary>True for any role, including plain Members.</summary>
    public Task<bool> IsInAllianceAsync(Guid allianceId)
    {
        var uid = UserId;
        return db.AllianceMembers.AnyAsync(m => m.AllianceId == allianceId && m.UserId == uid);
    }

    public Task<bool> IsOwnerAsync(Guid allianceId) =>
        db.Alliances.AnyAsync(a => a.Id == allianceId && a.OwnerId == UserId);

    /// <summary>Alliances where the user is Owner/Leader.</summary>
    public IQueryable<Guid> ManagedAllianceIds()
    {
        var uid = UserId;
        return db.AllianceMembers.Where(m => m.UserId == uid && m.Role != AllianceRole.Member).Select(m => m.AllianceId);
    }

    /// <summary>Players whose data (profile, rewards) the user may read: all players of managed alliances plus their own.</summary>
    public IQueryable<Guid> VisiblePlayerIds()
    {
        var uid = UserId;
        var managed = ManagedAllianceIds();
        return db.Players.Where(p => managed.Contains(p.AllianceId) || p.UserId == uid).Select(p => p.Id);
    }
}

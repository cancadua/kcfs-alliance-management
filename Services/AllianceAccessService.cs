using System.Security.Claims;
using AllianceRewards.Api.Data;
using AllianceRewards.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Services;

/// <summary>Resolves which alliances the current user belongs to.</summary>
public class AllianceAccessService(AppDbContext db, IHttpContextAccessor http)
{
    public Guid UserId =>
        Guid.Parse(http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? throw new UnauthorizedAccessException());

    /// <summary>True only for Owner/Leader; plain Members have no access to alliance data.</summary>
    public Task<bool> IsMemberAsync(Guid allianceId)
    {
        var uid = UserId;
        return db.AllianceMembers.AnyAsync(m =>
            m.AllianceId == allianceId && m.UserId == uid && m.Role != AllianceRole.Member);
    }

    public Task<bool> IsOwnerAsync(Guid allianceId) =>
        db.Alliances.AnyAsync(a => a.Id == allianceId && a.OwnerId == UserId);

    public IQueryable<Guid> MyAllianceIds()
    {
        var uid = UserId;
        return db.AllianceMembers.Where(m => m.UserId == uid && m.Role != AllianceRole.Member).Select(m => m.AllianceId);
    }
}

using System.Security.Claims;
using AllianceRewards.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Services;

/// <summary>Resolves which alliances the current user belongs to.</summary>
public class AllianceAccessService(AppDbContext db, IHttpContextAccessor http)
{
    public Guid UserId =>
        Guid.Parse(http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? throw new UnauthorizedAccessException());

    public Task<bool> IsMemberAsync(Guid allianceId) =>
        db.AllianceMembers.AnyAsync(m => m.AllianceId == allianceId && m.UserId == UserId);

    public Task<bool> IsOwnerAsync(Guid allianceId) =>
        db.Alliances.AnyAsync(a => a.Id == allianceId && a.OwnerId == UserId);

    public IQueryable<Guid> MyAllianceIds()
    {
        var uid = UserId;
        return db.AllianceMembers.Where(m => m.UserId == uid).Select(m => m.AllianceId);
    }
}

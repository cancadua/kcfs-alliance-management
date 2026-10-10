using AllianceRewards.Api.Data;
using AllianceRewards.Api.Infrastructure;
using AllianceRewards.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AllianceRewards.Api.Services;

/// <summary>Links registered accounts to alliance players, whichever way the link is made (code, invite, request).</summary>
public class PlayerLinkService(AppDbContext db, IConfiguration config)
{
    private TimeSpan CodeLifetime => TimeSpan.FromDays(config.GetValue("LinkCodes:ValidDays", 7));

    /// <summary>Creates a new one-time code for the player, replacing any previous one.</summary>
    public async Task<(string Code, DateTime ExpiresAt)> CreateCodeAsync(Player player, Guid actorId)
    {
        var code = AccessCode.Generate();

        await db.PlayerLinkCodes.Where(c => c.PlayerId == player.Id).ExecuteDeleteAsync();

        var entry = new PlayerLinkCode
        {
            PlayerId = player.Id,
            CodeHash = AccessCode.Hash(code),
            CreatedById = actorId,
            ExpiresAt = DateTime.UtcNow + CodeLifetime,
        };
        db.PlayerLinkCodes.Add(entry);
        await db.SaveChangesAsync();
        return (code, entry.ExpiresAt);
    }

    /// <summary>Returns the player a valid (unexpired) code points to, or null.</summary>
    public async Task<Player?> FindPlayerByCodeAsync(string code)
    {
        var hash = AccessCode.Hash(code);
        var now = DateTime.UtcNow;
        return await db.PlayerLinkCodes
            .Where(c => c.CodeHash == hash && c.ExpiresAt > now)
            .Select(c => c.Player)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Links the account to the player and saves. Creates the alliance membership (with <paramref name="roleIfNew"/>)
    /// when the account is not in the alliance yet; an existing membership keeps its role.
    /// Returns an error message when the link is not allowed.
    /// </summary>
    public async Task<string?> LinkAsync(
        Player player, Guid userId, PlayerLinkMethod method, Guid actorId, AllianceRole roleIfNew = AllianceRole.Member)
    {
        if (player.UserId is not null) return "This player is already linked to an account.";
        if (await db.Players.AnyAsync(p => p.AllianceId == player.AllianceId && p.UserId == userId))
            return "This account is already linked to another player in this alliance.";

        player.UserId = userId;

        if (!await db.AllianceMembers.AnyAsync(m => m.AllianceId == player.AllianceId && m.UserId == userId))
            db.AllianceMembers.Add(new AllianceMember { AllianceId = player.AllianceId, UserId = userId, Role = roleIfNew });

        db.PlayerLinkCodes.RemoveRange(await db.PlayerLinkCodes.Where(c => c.PlayerId == player.Id).ToListAsync());

        // Settle pending requests made obsolete by this link: the matching one is accepted, the rest rejected.
        var now = DateTime.UtcNow;
        var pending = await db.LinkRequests
            .Where(r => r.Status == LinkRequestStatus.Pending &&
                        (r.PlayerId == player.Id || (r.AllianceId == player.AllianceId && r.UserId == userId)))
            .ToListAsync();
        foreach (var r in pending)
        {
            r.Status = r.PlayerId == player.Id && r.UserId == userId ? LinkRequestStatus.Accepted : LinkRequestStatus.Rejected;
            r.ResolvedAt = now;
            r.ResolvedById = actorId;
        }

        Log(player, userId, PlayerLinkAction.Linked, method, actorId);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request linked the same player or account first.
            return "This player or account was linked in the meantime.";
        }
        return null;
    }

    /// <summary>Removes the player's account link (keeping the player and its history). Does not save.</summary>
    public void Unlink(Player player, PlayerLinkMethod method, Guid actorId)
    {
        if (player.UserId is not { } userId) return;
        player.UserId = null;
        player.User = null;
        Log(player, userId, PlayerLinkAction.Unlinked, method, actorId);
    }

    /// <summary>
    /// Prepares a player for deletion: unlinks its account, removes that account's membership (unless Owner)
    /// and rejects pending requests for the player. Does not save.
    /// </summary>
    public async Task DetachForDeletionAsync(Player player, Guid actorId)
    {
        if (player.UserId is { } userId)
        {
            var member = await db.AllianceMembers.FirstOrDefaultAsync(m =>
                m.AllianceId == player.AllianceId && m.UserId == userId && m.Role != AllianceRole.Owner);
            if (member is not null) db.AllianceMembers.Remove(member);
            Unlink(player, PlayerLinkMethod.PlayerDeleted, actorId);
        }

        var now = DateTime.UtcNow;
        var pending = await db.LinkRequests
            .Where(r => r.PlayerId == player.Id && r.Status == LinkRequestStatus.Pending)
            .ToListAsync();
        foreach (var r in pending)
        {
            r.Status = LinkRequestStatus.Rejected;
            r.ResolvedAt = now;
            r.ResolvedById = actorId;
        }
    }

    private void Log(Player player, Guid userId, PlayerLinkAction action, PlayerLinkMethod method, Guid actorId) =>
        db.PlayerLinkLogs.Add(new PlayerLinkLog
        {
            AllianceId = player.AllianceId,
            PlayerId = player.Id,
            PlayerName = player.Name,
            UserId = userId,
            Action = action,
            Method = method,
            ActorId = actorId,
        });
}

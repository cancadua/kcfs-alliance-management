namespace AllianceRewards.Api.Models;

public enum AllianceRole { Member = 0, Owner = 1, Leader = 2 }

public enum RewardType { Normal = 0, Blue = 1, Purple = 2, Mvp = 3 }

public enum LinkRequestStatus { Pending = 0, Accepted = 1, Rejected = 2, Cancelled = 3 }

public enum PlayerLinkAction { Linked = 0, Unlinked = 1 }

/// <summary>How a link was created or why it was removed.</summary>
public enum PlayerLinkMethod { Code = 0, Invite = 1, Request = 2, Unlink = 3, MemberRemoved = 4, PlayerDeleted = 5 }

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<AllianceMember> Memberships { get; set; } = [];
}

public class Alliance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? Owner { get; set; }
    public List<AllianceMember> Members { get; set; } = [];
    public List<Player> Players { get; set; } = [];
    public List<Event> Events { get; set; } = [];
}

public class AllianceMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllianceId { get; set; }
    public Guid UserId { get; set; }
    public AllianceRole Role { get; set; } = AllianceRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public Alliance? Alliance { get; set; }
    public User? User { get; set; }
}

public class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllianceId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Activity level, 0-100.</summary>
    public int Activity { get; set; } = 50;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Registered account linked to this player, if any.</summary>
    public Guid? UserId { get; set; }

    public Alliance? Alliance { get; set; }
    public User? User { get; set; }
    public List<Reward> Rewards { get; set; } = [];
}

/// <summary>One-time code a user redeems to link their account to a player. At most one per player.</summary>
public class PlayerLinkCode
{
    public Guid PlayerId { get; set; }
    /// <summary>SHA-256 of the normalized code; the plain code is never stored.</summary>
    public string CodeHash { get; set; } = "";
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    public Player? Player { get; set; }
}

/// <summary>A user's request to be linked to a player, resolved by an Owner/Leader.</summary>
public class LinkRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllianceId { get; set; }
    /// <summary>Null once the player has been deleted.</summary>
    public Guid? PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public Guid UserId { get; set; }
    public string? Message { get; set; }
    public LinkRequestStatus Status { get; set; } = LinkRequestStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedById { get; set; }

    public Alliance? Alliance { get; set; }
    public Player? Player { get; set; }
    public User? User { get; set; }
}

/// <summary>Audit entry for linking/unlinking. Ids are plain values so entries outlive the rows they refer to.</summary>
public class PlayerLinkLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllianceId { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public Guid UserId { get; set; }
    public PlayerLinkAction Action { get; set; }
    public PlayerLinkMethod Method { get; set; }
    public Guid ActorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllianceId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;

    public Alliance? Alliance { get; set; }
    public List<Reward> Rewards { get; set; } = [];
}

public class Reward
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }
    public Guid? EventId { get; set; }
    public RewardType Type { get; set; }
    public DateTime AwardedAt { get; set; } = DateTime.UtcNow;

    public Player? Player { get; set; }
    public Event? Event { get; set; }
}

namespace AllianceRewards.Api.Models;

public enum AllianceRole { Member = 0, Owner = 1 }

public enum RewardType { Normal = 0, Blue = 1, Purple = 2, Mvp = 3 }

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

    public Alliance? Alliance { get; set; }
    public List<Reward> Rewards { get; set; } = [];
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

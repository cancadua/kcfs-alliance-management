using System.ComponentModel.DataAnnotations;
using AllianceRewards.Api.Models;

namespace AllianceRewards.Api.DTOs;

// Auth
public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, StringLength(64, MinimumLength = 3)] string Username,
    [Required, MinLength(6)] string Password);

public record LoginRequest([Required] string Email, [Required] string Password);

public record AuthResponse(string Token);

// Alliances
public record CreateAllianceRequest([Required, StringLength(100, MinimumLength = 2)] string Name);

public record InviteRequest([Required, EmailAddress] string Email, AllianceRole Role = AllianceRole.Leader);

public record AllianceResponse(Guid Id, string Name, Guid OwnerId, DateTime CreatedAt, int MemberCount);

public record MemberResponse(Guid UserId, string Username, string Email, AllianceRole Role, DateTime JoinedAt);

// Players
public record CreatePlayerRequest(
    [Required] Guid AllianceId,
    [Required, StringLength(64, MinimumLength = 1)] string Name,
    [Range(0, 100)] int Activity = 50);

public record UpdatePlayerRequest(
    [StringLength(64, MinimumLength = 1)] string? Name,
    [Range(0, 100)] int? Activity,
    bool? IsActive);

public record PlayerResponse(Guid Id, Guid AllianceId, string Name, int Activity, bool IsActive, DateTime CreatedAt);

// Events
public record CreateEventRequest(
    [Required] Guid AllianceId,
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    string? Description,
    DateTime? Date);

public record EventResponse(Guid Id, Guid AllianceId, string Name, string? Description, DateTime Date);

// Rewards
public record CreateRewardRequest(
    [Required] Guid PlayerId,
    [Required] RewardType Type,
    Guid? EventId,
    DateTime? AwardedAt);

public record RewardResponse(Guid Id, Guid PlayerId, string PlayerName, Guid? EventId, RewardType Type, DateTime AwardedAt);

// Recommendations
public record MvpRecommendation(
    Guid PlayerId,
    string Player,
    int Score,
    DateTime? LastReward,
    DateTime? LastMvp,
    int NormalRewards,
    int BlueRewards,
    int PurpleRewards);

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

public record MeResponse(Guid Id, string Email, string Username, DateTime CreatedAt);

// Alliances
public record CreateAllianceRequest([Required, StringLength(100, MinimumLength = 2)] string Name);

/// <summary>Invites a registered account (by email or username) and links it to the given player.</summary>
public record InviteRequest(
    [Required] string User,
    [Required] Guid PlayerId,
    AllianceRole Role = AllianceRole.Member);

public record AllianceResponse(Guid Id, string Name, Guid OwnerId, DateTime CreatedAt, int MemberCount);

public record MyAllianceResponse(
    Guid Id, string Name, Guid OwnerId, AllianceRole MyRole, DateTime CreatedAt, int MemberCount, Guid? MyPlayerId, string? MyPlayerName);

public record UpdateMemberRoleRequest([Required] AllianceRole Role);

public record MemberResponse(
    Guid UserId, string Username, string Email, AllianceRole Role, DateTime JoinedAt, Guid? PlayerId, string? PlayerName);

public record AllianceSearchResult(Guid Id, string Name);

public record UnlinkedPlayerResponse(Guid Id, string Name);

public record PlayerLinkLogResponse(
    Guid Id, Guid PlayerId, string PlayerName, Guid UserId, string? Username,
    PlayerLinkAction Action, PlayerLinkMethod Method, Guid ActorId, string? ActorUsername, DateTime CreatedAt);

// Players
public record CreatePlayerRequest(
    [Required] Guid AllianceId,
    [Required, StringLength(64, MinimumLength = 1)] string Name,
    [Range(0, 100)] int Activity = 50);

public record UpdatePlayerRequest(
    [StringLength(64, MinimumLength = 1)] string? Name,
    [Range(0, 100)] int? Activity,
    bool? IsActive);

public record PlayerResponse(
    Guid Id, Guid AllianceId, string Name, int Activity, bool IsActive, DateTime CreatedAt, Guid? UserId, string? Username);

public record LinkCodeResponse(string Code, DateTime ExpiresAt);

public record ClaimPlayerRequest([Required, StringLength(16)] string Code);

// Link requests
public record CreateLinkRequestRequest(
    [Required] Guid AllianceId,
    [Required] Guid PlayerId,
    [StringLength(500)] string? Message);

public record LinkRequestResponse(
    Guid Id, Guid AllianceId, string AllianceName, Guid? PlayerId, string PlayerName, Guid UserId, string Username,
    string? Message, LinkRequestStatus Status, DateTime CreatedAt, DateTime? ResolvedAt);

// Events
public record CreateEventRequest(
    [Required] Guid AllianceId,
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    string? Description,
    DateTime? Date);

public record UpdateEventRequest(
    [StringLength(100, MinimumLength = 1)] string? Name,
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

// Stats
public record PlayerStats(
    Guid PlayerId,
    string Player,
    bool IsActive,
    int Normal,
    int Blue,
    int Purple,
    int Mvp,
    int Total,
    DateTime? LastReward);

public record RewardTotals(int Normal, int Blue, int Purple, int Mvp, int Total);

public record AllianceStats(Guid AllianceId, RewardTotals Totals, List<PlayerStats> Players);

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

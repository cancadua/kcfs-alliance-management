using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Services;

/// <summary>
/// Scores players 0-100 for MVP candidacy. Weights (sum = 100):
/// activity 30, time since last MVP 25, time since last reward 20,
/// few normal rewards 10, few BLUE 8, few PURPLE 7.
/// </summary>
public class RecommendationService(AppDbContext db)
{
    private const int DaysCap = 60;   // after this many days the "time since" components are maxed
    private const int CountCap = 5;   // this many rewards of a type zeroes its component

    public async Task<List<MvpRecommendation>> GetMvpAsync(IReadOnlyCollection<Guid> allianceIds, DateTime? now = null)
    {
        var today = now ?? DateTime.UtcNow;

        var players = await db.Players
            .Where(p => allianceIds.Contains(p.AllianceId) && p.IsActive)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Activity,
                LastReward = p.Rewards.Max(r => (DateTime?)r.AwardedAt),
                LastMvp = p.Rewards.Where(r => r.Type == RewardType.Mvp).Max(r => (DateTime?)r.AwardedAt),
                Normal = p.Rewards.Count(r => r.Type == RewardType.Normal),
                Blue = p.Rewards.Count(r => r.Type == RewardType.Blue),
                Purple = p.Rewards.Count(r => r.Type == RewardType.Purple),
            })
            .ToListAsync();

        return players
            .Select(p =>
            {
                var activity = Math.Clamp(p.Activity, 0, 100) / 100.0;
                var sinceMvp = DaysFraction(p.LastMvp, today);
                var sinceReward = DaysFraction(p.LastReward, today);
                var score =
                    30 * activity +
                    25 * sinceMvp +
                    20 * sinceReward +
                    10 * CountFraction(p.Normal) +
                    8 * CountFraction(p.Blue) +
                    7 * CountFraction(p.Purple);

                return new MvpRecommendation(
                    p.Id, p.Name, (int)Math.Round(score), p.LastReward, p.LastMvp, p.Normal, p.Blue, p.Purple);
            })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Player)
            .ToList();
    }

    private static double DaysFraction(DateTime? date, DateTime now) =>
        date is null ? 1.0 : Math.Clamp((now - date.Value).TotalDays / DaysCap, 0, 1);

    private static double CountFraction(int count) => Math.Max(0, 1 - (double)count / CountCap);
}

using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Services;

/// <summary>
/// Scores players 0-100 for MVP candidacy. Every reward is an MVP; weights (sum = 100):
/// activity 30, time since last MVP 45, few Normal 10, few Earl 8, few Duke 7.
/// </summary>
public class RecommendationService(AppDbContext db)
{
    private const int DaysCap = 60;   // after this many days the "time since" component is maxed
    private const int CountCap = 5;   // this many rewards of a tier zeroes its component

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
                LastMvp = p.Rewards.Max(r => (DateTime?)r.AwardedAt),
                Normal = p.Rewards.Count(r => r.Type == RewardType.Normal),
                Earl = p.Rewards.Count(r => r.Type == RewardType.Earl),
                Duke = p.Rewards.Count(r => r.Type == RewardType.Duke),
            })
            .ToListAsync();

        return players
            .Select(p =>
            {
                var activity = Math.Clamp(p.Activity, 0, 100) / 100.0;
                var sinceMvp = DaysFraction(p.LastMvp, today);
                var score =
                    30 * activity +
                    45 * sinceMvp +
                    10 * CountFraction(p.Normal) +
                    8 * CountFraction(p.Earl) +
                    7 * CountFraction(p.Duke);

                return new MvpRecommendation(
                    p.Id, p.Name, (int)Math.Round(score), p.LastMvp, p.Normal, p.Earl, p.Duke);
            })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Player)
            .ToList();
    }

    private static double DaysFraction(DateTime? date, DateTime now) =>
        date is null ? 1.0 : Math.Clamp((now - date.Value).TotalDays / DaysCap, 0, 1);

    private static double CountFraction(int count) => Math.Max(0, 1 - (double)count / CountCap);
}

using AllianceRewards.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Services;

/// <summary>
/// Hard-deletes expired shared maps. Sleeps until the next known expiry (or until a map is created) instead of
/// polling, so an idle app does not keep waking the database.
/// </summary>
public class SharedMapCleanupService(IServiceScopeFactory scopes, ILogger<SharedMapCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _wake = new(0);

    /// <summary>Signals that a map was created, so the next expiry is recalculated.</summary>
    public void Wake() => _wake.Release();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;
                var deleted = await db.SharedMaps.Where(m => m.ExpiresAt <= now).ExecuteDeleteAsync(ct);
                if (deleted > 0) logger.LogInformation("Deleted {Count} expired shared map(s)", deleted);

                var next = await db.SharedMaps.MinAsync(m => (DateTime?)m.ExpiresAt, ct);
                delay = next is null
                    ? Timeout.InfiniteTimeSpan
                    : (next.Value - DateTime.UtcNow > TimeSpan.Zero ? next.Value - DateTime.UtcNow : TimeSpan.Zero) + Slack;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Shared map cleanup failed");
                delay = RetryAfterError;
            }

            try
            {
                await _wake.WaitAsync(delay, ct);
                while (_wake.CurrentCount > 0) await _wake.WaitAsync(0, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

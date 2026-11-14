using Microsoft.Extensions.Caching.Memory;

namespace AllianceRewards.Api.Services;

/// <summary>
/// Temporarily blocks login for an email after repeated failures, against password guessing spread over many IPs.
/// Counts unknown emails the same way, so a block does not reveal whether an account exists.
/// In memory: the count resets on restart, which is acceptable for a single instance.
/// </summary>
public class LoginThrottle(IConfiguration config)
{
    private sealed class Counter
    {
        public int Failures;
    }

    // Private cache with a size limit, so a flood of random emails cannot grow memory without bound.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    private int MaxFailures => config.GetValue("RateLimit:LoginAccount:MaxFailures", 10);
    private TimeSpan Window => TimeSpan.FromMinutes(config.GetValue("RateLimit:LoginAccount:WindowMinutes", 15));

    public bool IsLocked(string email) =>
        _cache.TryGetValue(email, out Counter? counter) && Volatile.Read(ref counter!.Failures) >= MaxFailures;

    public void RecordFailure(string email)
    {
        var counter = _cache.GetOrCreate(email, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            entry.Size = 1;
            return new Counter();
        })!;
        Interlocked.Increment(ref counter.Failures);
    }

    public void Reset(string email) => _cache.Remove(email);
}

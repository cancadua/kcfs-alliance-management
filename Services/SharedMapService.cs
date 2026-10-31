using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Infrastructure;
using AllianceRewards.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Services;

/// <summary>Anonymous shared map states: created with a code, edited by anyone who has it, deleted after expiry.</summary>
public class SharedMapService(AppDbContext db, IConfiguration config, SharedMapCleanupService cleanup)
{
    private const int MaxKeysPerPatch = 500;
    private const int MaxKeyLength = 200;
    private const int MaxAttempts = 5;

    private TimeSpan Lifetime => TimeSpan.FromHours(config.GetValue("SharedMaps:LifetimeHours", 4.0));
    private int MaxStateBytes => config.GetValue("SharedMaps:MaxStateBytes", 65536);

    public async Task<(SharedMap? Map, string? Code, string? Error)> CreateAsync(JsonElement? initialState)
    {
        var state = new JsonObject();
        if (initialState is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } s)
        {
            if (s.ValueKind != JsonValueKind.Object) return (null, null, "State must be a JSON object.");
            state = JsonNode.Parse(s.GetRawText())!.AsObject();
        }

        var json = state.ToJsonString();
        if (Encoding.UTF8.GetByteCount(json) > MaxStateBytes) return (null, null, $"State exceeds {MaxStateBytes} bytes.");

        var code = AccessCode.Generate();
        var now = DateTime.UtcNow;
        var map = new SharedMap
        {
            CodeHash = AccessCode.Hash(code),
            State = json,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        db.SharedMaps.Add(map);
        await db.SaveChangesAsync();

        cleanup.Wake();
        return (map, code, null);
    }

    /// <summary>The map for a code, or null when it does not exist or has expired.</summary>
    public Task<SharedMap?> FindAsync(string code)
    {
        var hash = AccessCode.Hash(code);
        var now = DateTime.UtcNow;
        return db.SharedMaps.FirstOrDefaultAsync(m => m.CodeHash == hash && m.ExpiresAt > now);
    }

    /// <summary>
    /// Sets/removes top-level keys. Concurrent patches are merged: on a version conflict the latest state is
    /// re-read and the patch re-applied. Returns (null, null) when the map does not exist.
    /// </summary>
    public async Task<(SharedMap? Map, string? Error)> PatchAsync(string code, PatchSharedMapRequest req)
    {
        var set = req.Set ?? [];
        var remove = req.Remove ?? [];
        if (set.Count + remove.Count == 0) return (null, "Nothing to change: provide 'set' and/or 'remove'.");
        if (set.Count + remove.Count > MaxKeysPerPatch) return (null, $"At most {MaxKeysPerPatch} keys per change.");
        if (set.Keys.Concat(remove).Any(k => string.IsNullOrEmpty(k) || k.Length > MaxKeyLength))
            return (null, $"Keys must be 1-{MaxKeyLength} characters long.");

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var map = await FindAsync(code);
            if (map is null) return (null, null);

            var state = JsonNode.Parse(map.State)!.AsObject();
            foreach (var key in remove) state.Remove(key);
            foreach (var (key, value) in set) state[key] = JsonNode.Parse(value.GetRawText());

            var json = state.ToJsonString();
            if (Encoding.UTF8.GetByteCount(json) > MaxStateBytes) return (null, $"State would exceed {MaxStateBytes} bytes.");

            map.State = json;
            map.Version++;
            map.UpdatedAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync();
                return (map, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone else changed the map meanwhile: retry on top of their version.
                db.ChangeTracker.Clear();
            }
        }
        return (null, "The map is being changed by many people at once; try again.");
    }

    public static SharedMapResponse ToResponse(SharedMap map, string code)
    {
        var normalized = AccessCode.Normalize(code);
        var display = normalized.Length == 8 ? $"{normalized[..4]}-{normalized[4..]}" : normalized;
        using var doc = JsonDocument.Parse(map.State);
        return new SharedMapResponse(display, map.Version, map.ExpiresAt, map.UpdatedAt, doc.RootElement.Clone());
    }
}

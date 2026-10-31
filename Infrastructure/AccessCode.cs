using System.Security.Cryptography;
using System.Text;

namespace AllianceRewards.Api.Infrastructure;

/// <summary>Random human-friendly codes (e.g. <c>K7QX-92PM</c>) and the hash under which they are stored.</summary>
public static class AccessCode
{
    // 32 symbols without look-alikes (0/O, 1/I): 8 characters = 40 bits of entropy.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 8;

    public static string Generate()
    {
        var raw = RandomNumberGenerator.GetString(Alphabet, Length);
        return $"{raw[..4]}-{raw[4..]}";
    }

    /// <summary>Uppercase without dashes/spaces, so <c>k7qx92pm</c> and <c>K7QX-92PM</c> are the same code.</summary>
    public static string Normalize(string code) =>
        code.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

    /// <summary>SHA-256 (hex) of the normalized code; only this is stored.</summary>
    public static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));
}

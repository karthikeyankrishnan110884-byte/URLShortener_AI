using System.Security.Cryptography;

namespace UrlShortener.Api.Services;

/// <summary>
/// Cryptographically random Base62 codes. Random (not sequential) codes are not enumerable;
/// 62^7 ≈ 3.5 trillion combinations keeps collision probability low, and collisions are retried anyway.
/// </summary>
public sealed class RandomShortCodeGenerator : IShortCodeGenerator
{
    internal const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public string Generate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        return RandomNumberGenerator.GetString(Alphabet, length);
    }
}

using System.Security.Cryptography;

namespace LifeCounterAssist.Api.Services;

/// <summary>Generates short human-friendly codes.</summary>
public static class Codes
{
    // No 0/O/1/I/L to avoid confusion when codes are read aloud.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Creates a random code.</summary>
    /// <param name="length">Code length.</param>
    /// <returns>The code.</returns>
    public static string New(int length) => RandomNumberGenerator.GetString(Alphabet, length);

    /// <summary>Normalizes user input before lookup.</summary>
    /// <param name="code">Raw input.</param>
    /// <returns>Upper-case trimmed code.</returns>
    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();
}

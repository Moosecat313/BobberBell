using System.Security.Cryptography;

namespace BobberBell.Services;

/// <summary>Random timing jitter (reaction delays, cast waits, key holds) from the OS CSPRNG.</summary>
internal static class Jitter
{
    /// <summary>Returns a uniformly distributed value in [0, 1).</summary>
    /// <returns>A random double.</returns>
    internal static double NextDouble() => RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;

    /// <summary>Returns a value in [0, <paramref name="maxExclusive"/>).</summary>
    /// <param name="maxExclusive">Exclusive upper bound.</param>
    /// <returns>A random integer.</returns>
    internal static int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}

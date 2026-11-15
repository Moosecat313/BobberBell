using System;

namespace BobberBell.Core.Detection;

/// <summary>
/// The two gates a sound must pass to count as a bite: a loudness gate (cheap, applied per audio chunk
/// before fingerprinting) and a fingerprint-similarity gate.
/// </summary>
/// <param name="MinSimilarity">Minimum fingerprint confidence (0..1).</param>
/// <param name="MinVolumeDb">Minimum chunk peak in dBFS.</param>
public readonly record struct DetectionThresholds(float MinSimilarity, float MinVolumeDb)
{
    /// <summary>dBFS reported for digital silence.</summary>
    public const float SilenceDb = -120f;

    /// <summary>Returns the peak level of a float sample buffer in dBFS (-120 for silence).</summary>
    /// <param name="samples">32-bit float samples in -1..1.</param>
    /// <returns>Peak level in dBFS.</returns>
    public static float PeakDb(ReadOnlySpan<float> samples)
    {
        float peak = 0f;
        foreach (float s in samples)
        {
            float v = Math.Abs(s);
            if (v > peak) peak = v;
        }

        return peak <= 1e-8f ? SilenceDb : (float)(20.0 * Math.Log10(peak));
    }

    /// <summary>Returns whether a chunk is loud enough to fingerprint.</summary>
    /// <param name="db">Chunk peak in dBFS.</param>
    /// <returns><c>true</c> if <paramref name="db"/> is at or above the volume gate.</returns>
    public bool PassesVolume(float db) => db >= MinVolumeDb;

    /// <summary>Returns whether a fingerprint confidence counts as a bite.</summary>
    /// <param name="confidence">Match confidence (0..1).</param>
    /// <returns><c>true</c> if <paramref name="confidence"/> is at or above the similarity gate.</returns>
    public bool IsMatch(double confidence) => confidence >= MinSimilarity;

    /// <summary>Maps a level in dBFS to a 0–100 meter value (-80 dBFS = 0, 0 dBFS = 100).</summary>
    /// <param name="db">Level in dBFS.</param>
    /// <returns>Meter percentage.</returns>
    public static int VolumeToPercent(float db) => (int)Math.Max(0, Math.Min(100, (db + 80f) / 80f * 100f));

    /// <summary>Maps a similarity (0..1) to a 0–100 meter value.</summary>
    /// <param name="similarity">Similarity.</param>
    /// <returns>Meter percentage.</returns>
    public static int SimilarityToPercent(float similarity) => (int)Math.Max(0, Math.Min(100, similarity * 100));
}

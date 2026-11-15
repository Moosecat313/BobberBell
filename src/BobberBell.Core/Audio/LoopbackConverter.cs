using System;

namespace BobberBell.Core.Audio;

/// <summary>
/// Converts a capture stream in its native format (e.g. 48 kHz, 32-bit float, stereo from WASAPI loopback) into the
/// 5512 Hz mono float stream SoundFingerprinting requires. Keeps resampler state across chunks. One instance per stream.
/// </summary>
public sealed class LoopbackConverter
{
    /// <summary>Sample rate SoundFingerprinting fingerprints and queries at.</summary>
    public const int FingerprintSampleRate = 5512;

    private readonly StreamingResampler resampler;

    /// <summary>Creates a converter for the given capture format.</summary>
    /// <param name="format">Capture format.</param>
    /// <exception cref="NotSupportedException">The format cannot be decoded.</exception>
    public LoopbackConverter(AudioSampleFormat format)
    {
        format.Validate();
        Format = format;
        resampler = new StreamingResampler(format.SampleRate, FingerprintSampleRate);
    }

    /// <summary>Gets the capture format.</summary>
    public AudioSampleFormat Format { get; }

    /// <summary>Converts the next chunk of captured bytes.</summary>
    /// <param name="data">Interleaved capture bytes.</param>
    /// <param name="peakDb">Peak level of the original (pre-mix) audio in dBFS.</param>
    /// <returns>5512 Hz mono samples for this chunk (may be empty).</returns>
    public float[] Convert(ReadOnlySpan<byte> data, out float peakDb)
    {
        float[] mono = SampleDecoder.DownmixToMono(data, Format, out float peak);
        peakDb = SampleDecoder.PeakToDb(peak);
        return resampler.Process(mono);
    }
}

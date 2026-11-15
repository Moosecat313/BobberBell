using System;

namespace BobberBell.Core.Audio;

/// <summary>How the samples in a captured buffer are encoded.</summary>
public enum SampleEncoding
{
    /// <summary>Signed integer PCM (8-bit is unsigned, as in WAV).</summary>
    Pcm,

    /// <summary>IEEE floating point (32- or 64-bit).</summary>
    IeeeFloat,
}

/// <summary>
/// Describes interleaved audio as delivered by a capture device (a platform-neutral mirror of a WAVEFORMATEX).
/// </summary>
/// <param name="Encoding">Sample encoding.</param>
/// <param name="SampleRate">Frames per second.</param>
/// <param name="Channels">Interleaved channel count.</param>
/// <param name="BitsPerSample">Container bits per sample.</param>
/// <param name="BlockAlign">Bytes per frame (all channels).</param>
public readonly record struct AudioSampleFormat(SampleEncoding Encoding, int SampleRate, int Channels, int BitsPerSample, int BlockAlign)
{
    /// <summary>WAVE_FORMAT_PCM.</summary>
    public const int FormatTagPcm = 0x0001;

    /// <summary>WAVE_FORMAT_IEEE_FLOAT.</summary>
    public const int FormatTagIeeeFloat = 0x0003;

    /// <summary>WAVE_FORMAT_EXTENSIBLE.</summary>
    public const int FormatTagExtensible = 0xFFFE;

    /// <summary>KSDATAFORMAT_SUBTYPE_PCM.</summary>
    public static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT.</summary>
    public static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Bytes per sample of one channel within a frame.</summary>
    public int BytesPerSample => Channels > 0 ? BlockAlign / Channels : 0;

    /// <summary>Builds a format from WAVEFORMATEX(-TENSIBLE) fields.</summary>
    /// <param name="formatTag">wFormatTag.</param>
    /// <param name="subFormat">SubFormat for WAVE_FORMAT_EXTENSIBLE (ignored otherwise).</param>
    /// <param name="sampleRate">nSamplesPerSec.</param>
    /// <param name="channels">nChannels.</param>
    /// <param name="bitsPerSample">wBitsPerSample (container size).</param>
    /// <param name="blockAlign">nBlockAlign.</param>
    /// <returns>The format.</returns>
    /// <exception cref="NotSupportedException">The encoding or layout is not one this app can decode.</exception>
    public static AudioSampleFormat FromWaveFormat(int formatTag, Guid subFormat, int sampleRate, int channels, int bitsPerSample, int blockAlign)
    {
        SampleEncoding encoding = formatTag switch
        {
            FormatTagPcm => SampleEncoding.Pcm,
            FormatTagIeeeFloat => SampleEncoding.IeeeFloat,
            FormatTagExtensible when subFormat == SubtypePcm => SampleEncoding.Pcm,
            FormatTagExtensible when subFormat == SubtypeIeeeFloat => SampleEncoding.IeeeFloat,
            _ => throw new NotSupportedException($"Unsupported audio format tag 0x{formatTag:X4}."),
        };
        var format = new AudioSampleFormat(encoding, sampleRate, channels, bitsPerSample, blockAlign);
        format.Validate();
        return format;
    }

    /// <summary>Throws if this format cannot be decoded.</summary>
    /// <exception cref="NotSupportedException">The format is invalid or unsupported.</exception>
    public void Validate()
    {
        if (SampleRate is < 1000 or > 768_000) throw new NotSupportedException($"Unsupported sample rate {SampleRate} Hz.");
        if (Channels is < 1 or > 32) throw new NotSupportedException($"Unsupported channel count {Channels}.");
        if (BlockAlign <= 0 || BlockAlign % Channels != 0) throw new NotSupportedException($"Invalid block align {BlockAlign}.");
        int bytes = BytesPerSample;
        bool ok = Encoding == SampleEncoding.IeeeFloat ? bytes is 4 or 8 : bytes is 1 or 2 or 3 or 4;
        if (!ok) throw new NotSupportedException($"Unsupported {Encoding} sample size {bytes * 8} bits.");
    }
}

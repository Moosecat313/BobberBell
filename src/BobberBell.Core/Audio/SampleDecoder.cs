using System;
using System.Buffers.Binary;

namespace BobberBell.Core.Audio;

/// <summary>Decodes interleaved PCM / float bytes to mono float samples in -1..1.</summary>
public static class SampleDecoder
{
    /// <summary>
    /// Averages all channels of every complete frame in <paramref name="data"/> into one mono sample, and reports the
    /// peak absolute value over every channel sample (the loudness meter uses the original audio, not the mix).
    /// </summary>
    /// <param name="data">Interleaved little-endian frames; a trailing partial frame is ignored.</param>
    /// <param name="format">Layout of <paramref name="data"/>.</param>
    /// <param name="peak">Largest absolute channel sample (0..1+).</param>
    /// <returns>Mono samples, one per frame.</returns>
    public static float[] DownmixToMono(ReadOnlySpan<byte> data, AudioSampleFormat format, out float peak)
    {
        format.Validate();
        int channels = format.Channels;
        int width = format.BytesPerSample;
        int frames = data.Length / format.BlockAlign;
        var mono = new float[frames];
        float max = 0f;
        float scale = 1f / channels;
        int offset = 0;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                float v = Read(data.Slice(offset, width), format.Encoding);
                offset += width;
                float a = Math.Abs(v);
                if (a > max && !float.IsNaN(a)) max = a;
                sum += v;
            }

            float m = sum * scale;
            mono[f] = float.IsFinite(m) ? m : 0f;
        }

        peak = max;
        return mono;
    }

    /// <summary>Converts a peak amplitude to dBFS (-120 for silence), matching <see cref="Detection.DetectionThresholds.PeakDb"/>.</summary>
    /// <param name="peak">Peak absolute amplitude.</param>
    /// <returns>Level in dBFS.</returns>
    public static float PeakToDb(float peak) =>
        peak <= 1e-8f || !float.IsFinite(peak) ? Detection.DetectionThresholds.SilenceDb : (float)(20.0 * Math.Log10(peak));

    private static float Read(ReadOnlySpan<byte> s, SampleEncoding encoding)
    {
        if (encoding == SampleEncoding.IeeeFloat)
        {
            return s.Length == 4
                ? BinaryPrimitives.ReadSingleLittleEndian(s)
                : (float)BinaryPrimitives.ReadDoubleLittleEndian(s);
        }

        return s.Length switch
        {
            1 => (s[0] - 128) / 128f,
            2 => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
            3 => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
            _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
        };
    }
}

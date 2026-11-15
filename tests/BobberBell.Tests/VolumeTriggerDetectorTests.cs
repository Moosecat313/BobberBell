using System;
using System.Collections.Generic;
using System.Linq;
using BobberBell.Core.Audio;
using BobberBell.Core.Detection;

namespace BobberBell.Tests;

public class VolumeTriggerDetectorTests
{
    private const int Rate = 48000;
    private static readonly AudioSampleFormat Float48Stereo = new(SampleEncoding.IeeeFloat, Rate, 2, 32, 8);
    private static readonly VolumeTriggerOptions Defaults = new();

    // Band-limited noise: a sum of random-phase sines between lo and hi Hz, scaled to the given peak (dBFS).
    private static float[] BandNoise(double lo, double hi, double seconds, double peakDb, int seed, int rate = Rate)
    {
        var rng = new Lcg(seed);
        var s = new double[(int)(rate * seconds)];
        for (int k = 0; k < 60; k++)
        {
            double f = lo + (rng.NextDouble() * (hi - lo)), ph = rng.NextDouble() * 2 * Math.PI;
            for (int i = 0; i < s.Length; i++) s[i] += Math.Sin((2 * Math.PI * f * i / rate) + ph);
        }

        double max = s.Max(Math.Abs), gain = Math.Pow(10, peakDb / 20) / max;
        return s.Select(v => (float)(v * gain)).ToArray();
    }

    private static float[] Silence(double seconds, int rate = Rate) => new float[(int)(rate * seconds)];

    private static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    // Feeds mono audio as 48 kHz float stereo in 10 ms chunks, like WASAPI loopback.
    private static VolumeTriggerDetector Run(float[] mono, VolumeTriggerOptions? options = null)
    {
        var d = new VolumeTriggerDetector(Float48Stereo, options ?? Defaults);
        byte[] bytes = Signals.FloatBytes(Signals.Interleave(mono, 2));
        for (int i = 0; i < bytes.Length && d.Bite == null; i += 3840)
            d.Push(bytes.AsSpan(i, Math.Min(3840, bytes.Length - i)), out _);
        d.Complete();
        return d;
    }

    [Fact]
    public void Defaults_MatchTheDocumentedValues()
    {
        Assert.Equal(-38f, Defaults.LevelDb);
        Assert.Equal(0.6, Defaults.MinSeconds);
        Assert.Equal(2.5, Defaults.IgnoreSeconds);
        Assert.True(Defaults.ToneFilter);
        Assert.Equal((300.0, 3200.0), (Defaults.MinToneHz, Defaults.MaxToneHz));
    }

    [Fact]
    public void LowPitchedSustainedSound_AfterIgnoreTime_IsABite()
    {
        var d = Run(Concat(Silence(5), BandNoise(400, 2000, 1.5, -20, 1), Silence(2)));
        Assert.NotNull(d.Bite);
        Assert.InRange(d.Bite!.StartSeconds, 4.95, 5.05);
        Assert.InRange(d.Bite.CentroidHz, 400, 2000);
        Assert.InRange(d.Bite.PeakDb, -23, -19);
    }

    [Fact]
    public void Sound_InsideIgnoreTime_IsIgnored()
    {
        var d = Run(Concat(Silence(0.5), BandNoise(400, 2000, 1.5, -20, 2), Silence(5)));
        Assert.Null(d.Bite);
    }

    [Fact]
    public void ShortSound_IsRejectedAsTooShort()
    {
        var d = Run(Concat(Silence(4), BandNoise(400, 2000, 0.4, -20, 3), Silence(3)));
        Assert.Null(d.Bite);
        var r = Assert.Single(d.Rejected);
        Assert.Equal("too short", r.Reason);
    }

    [Fact]
    public void BrightSound_IsRejectedByToneFilter_AndAcceptedWithoutIt()
    {
        float[] bright = Concat(Silence(4), BandNoise(5000, 8000, 1.5, -20, 4), Silence(2));
        var filtered = Run(bright);
        Assert.Null(filtered.Bite);
        var r = Assert.Single(filtered.Rejected);
        Assert.StartsWith("tone ", r.Reason, StringComparison.Ordinal);
        Assert.InRange(r.CentroidHz, 5000, 8000);

        Assert.NotNull(Run(bright, Defaults with { ToneFilter = false }).Bite);
    }

    [Fact]
    public void QuietSound_BelowLevel_IsIgnored()
    {
        var d = Run(Concat(Silence(4), BandNoise(400, 2000, 1.5, -45, 5), Silence(2)));
        Assert.Null(d.Bite);
        Assert.Empty(d.Rejected);
    }

    [Fact]
    public void FlickeringSound_WithShortGaps_IsBridged()
    {
        var parts = new List<float[]> { Silence(4) };
        for (int i = 0; i < 10; i++)
        {
            parts.Add(BandNoise(400, 2000, 0.12, -20, 10 + i));
            parts.Add(Silence(0.06));
        }

        Assert.NotNull(Run(Concat(parts.ToArray())).Bite);
    }

    [Fact]
    public void LongGaps_SplitTheSound_SoNoPartIsLongEnough()
    {
        var parts = new List<float[]> { Silence(4) };
        for (int i = 0; i < 6; i++)
        {
            parts.Add(BandNoise(400, 2000, 0.3, -20, 30 + i));
            parts.Add(Silence(0.3));
        }

        var d = Run(Concat(parts.ToArray()));
        Assert.Null(d.Bite);
        Assert.Equal(6, d.Rejected.Count);
    }

    [Fact]
    public void Pcm16At44100_ArbitraryChunking_StillDetects()
    {
        const int rate = 44100;
        var fmt = new AudioSampleFormat(SampleEncoding.Pcm, rate, 2, 16, 4);
        float[] mono = Concat(Silence(3, rate), BandNoise(500, 1500, 1.0, -15, 7, rate), Silence(1, rate));
        var bytes = new byte[mono.Length * 4];
        for (int i = 0; i < mono.Length; i++)
        {
            short v = (short)(mono[i] * 32767);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), v);
            BitConverter.TryWriteBytes(bytes.AsSpan((i * 4) + 2), v);
        }

        var d = new VolumeTriggerDetector(fmt, Defaults);
        var rng = new Lcg(9);
        for (int i = 0; i < bytes.Length && d.Bite == null;)
        {
            int n = Math.Min(bytes.Length - i, 4 * (1 + (int)(rng.NextDouble() * 899)));
            d.Push(bytes.AsSpan(i, n), out _);
            i += n;
        }

        Assert.NotNull(d.Bite);
        Assert.InRange(d.Bite!.StartSeconds, 2.95, 3.05);
    }

    [Fact]
    public void Push_ReportsChunkPeakForTheMeter()
    {
        var d = new VolumeTriggerDetector(Float48Stereo, Defaults);
        d.Push(Signals.FloatBytes(Signals.Interleave(Signals.Sine(1000, Rate, 0.01, 0.5), 2)), out float db);
        Assert.InRange(db, -6.1, -5.9);
    }

    [Theory]
    [InlineData(250.0)]
    [InlineData(1000.0)]
    [InlineData(5000.0)]
    public void Fft_PutsASineInTheRightBin(double hz)
    {
        const int n = 1024;
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++) re[i] = Math.Sin(2 * Math.PI * hz * i / Rate);
        VolumeTriggerDetector.Fft(re, im);
        int best = Enumerable.Range(0, n / 2).MaxBy(k => (re[k] * re[k]) + (im[k] * im[k]));
        Assert.InRange(best * (double)Rate / n, hz - 50, hz + 50);
    }

    [Fact]
    public void Constructor_RejectsUnsupportedFormats()
    {
        Assert.ThrowsAny<NotSupportedException>(() => new VolumeTriggerDetector(new AudioSampleFormat(SampleEncoding.Pcm, Rate, 2, 12, 3), Defaults));
    }

    // Deterministic test-signal generator (not for security).
    private sealed class Lcg(int seed)
    {
        private ulong state = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL;

        public double NextDouble()
        {
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            return (state >> 11) * (1.0 / (1UL << 53));
        }
    }
}

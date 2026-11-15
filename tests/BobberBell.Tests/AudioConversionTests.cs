using System;
using System.Collections.Generic;
using BobberBell.Core.Audio;

namespace BobberBell.Tests;

public class AudioConversionTests
{
    private static readonly AudioSampleFormat Float48Stereo = new(SampleEncoding.IeeeFloat, 48000, 2, 32, 8);

    private static float[] ResampleInChunks(StreamingResampler r, float[] input, int chunk)
    {
        var output = new List<float>();
        for (int i = 0; i < input.Length; i += chunk)
            output.AddRange(r.Process(input.AsSpan(i, Math.Min(chunk, input.Length - i))));
        return output.ToArray();
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(96000)]
    [InlineData(16000)]
    public void Resampler_OutputLength_MatchesRateRatio(int inputRate)
    {
        var r = new StreamingResampler(inputRate, 5512);
        float[] input = Signals.Sine(440, inputRate, 10);
        float[] output = ResampleInChunks(r, input, inputRate / 100); // 10 ms chunks, like WASAPI
        double expected = (input.Length - r.LatencyInputSamples) * 5512.0 / inputRate;
        Assert.InRange(output.Length, expected - 2, expected + 2);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(1000)]
    [InlineData(1800)]
    public void Resampler_PreservesToneFrequencyAndLevel(double hz)
    {
        var r = new StreamingResampler(48000, 5512);
        float[] output = ResampleInChunks(r, Signals.Sine(hz, 48000, 2, 0.5), 480);
        var steady = output.AsSpan(200); // skip filter warm-up
        Assert.InRange(Signals.Rms(steady), 0.5 / Math.Sqrt(2) * 0.97, 0.5 / Math.Sqrt(2) * 1.03);
        double atTone = Signals.PowerAt(steady, hz, 5512);
        Assert.True(atTone > 1000 * Signals.PowerAt(steady, hz * 1.3, 5512), "tone energy should sit at its own frequency");
        Assert.True(atTone > 1000 * Signals.PowerAt(steady, hz * 0.7, 5512), "tone energy should sit at its own frequency");
    }

    [Theory]
    [InlineData(4000)] // would alias to 1512 Hz without the low-pass
    [InlineData(6000)]
    [InlineData(10000)]
    public void Resampler_RejectsContentAboveNewNyquist(double hz)
    {
        var r = new StreamingResampler(48000, 5512);
        float[] output = ResampleInChunks(r, Signals.Sine(hz, 48000, 2, 0.5), 480);
        Assert.True(Signals.Rms(output.AsSpan(200)) < 0.005, $"alias of {hz} Hz leaked through");
    }

    [Fact]
    public void Resampler_IsChunkSizeInvariant()
    {
        float[] input = Signals.Sine(700, 48000, 1);
        float[] whole = ResampleInChunks(new StreamingResampler(48000, 5512), input, input.Length);
        float[] odd = ResampleInChunks(new StreamingResampler(48000, 5512), input, 37);
        Assert.Equal(whole.Length, odd.Length);
        for (int i = 0; i < whole.Length; i++) Assert.Equal(whole[i], odd[i], 1e-6f);
    }

    [Fact]
    public void Resampler_ReplacesNonFiniteInput()
    {
        var r = new StreamingResampler(48000, 5512);
        var input = new float[4800];
        input[100] = float.NaN;
        input[200] = float.PositiveInfinity;
        Assert.All(r.Process(input), v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void Downmix_AveragesChannels_AndPeakUsesOriginalChannels()
    {
        float[] interleaved = [1f, 0f, 0.5f, -0.5f, -0.25f, -0.75f];
        float[] mono = SampleDecoder.DownmixToMono(Signals.FloatBytes(interleaved), Float48Stereo, out float peak);
        Assert.Equal([0.5f, 0f, -0.5f], mono);
        Assert.Equal(1f, peak);
    }

    [Fact]
    public void Downmix_Pcm16_Normalises()
    {
        var fmt = new AudioSampleFormat(SampleEncoding.Pcm, 48000, 2, 16, 4);
        byte[] data = new byte[8];
        BitConverter.TryWriteBytes(data.AsSpan(0), (short)16384);
        BitConverter.TryWriteBytes(data.AsSpan(2), (short)16384);
        BitConverter.TryWriteBytes(data.AsSpan(4), short.MinValue);
        BitConverter.TryWriteBytes(data.AsSpan(6), (short)0);
        float[] mono = SampleDecoder.DownmixToMono(data, fmt, out float peak);
        Assert.Equal([0.5f, -0.5f], mono);
        Assert.Equal(1f, peak);
    }

    [Fact]
    public void Downmix_Pcm24And8_AndSixChannels()
    {
        var pcm24 = new AudioSampleFormat(SampleEncoding.Pcm, 48000, 1, 24, 3);
        Assert.Equal([-0.5f], SampleDecoder.DownmixToMono(new byte[] { 0x00, 0x00, 0xC0 }, pcm24, out _));
        var pcm8 = new AudioSampleFormat(SampleEncoding.Pcm, 8000, 1, 8, 1);
        Assert.Equal([0f, 0.5f], SampleDecoder.DownmixToMono(new byte[] { 128, 192 }, pcm8, out _));
        var six = new AudioSampleFormat(SampleEncoding.IeeeFloat, 48000, 6, 32, 24);
        Assert.Equal([0.5f], SampleDecoder.DownmixToMono(Signals.FloatBytes([3f / 6, 3f / 6, 3f / 6, 3f / 6, 3f / 6, 3f / 6]), six, out _));
    }

    [Fact]
    public void Downmix_IgnoresTrailingPartialFrame()
    {
        byte[] data = new byte[8 + 5];
        Assert.Single(SampleDecoder.DownmixToMono(data, Float48Stereo, out _));
    }

    [Fact]
    public void FromWaveFormat_MapsExtensibleSubtypes()
    {
        Assert.Equal(SampleEncoding.IeeeFloat, AudioSampleFormat.FromWaveFormat(0xFFFE, AudioSampleFormat.SubtypeIeeeFloat, 48000, 2, 32, 8).Encoding);
        Assert.Equal(SampleEncoding.Pcm, AudioSampleFormat.FromWaveFormat(0xFFFE, AudioSampleFormat.SubtypePcm, 48000, 2, 16, 4).Encoding);
        Assert.Equal(SampleEncoding.Pcm, AudioSampleFormat.FromWaveFormat(1, Guid.Empty, 44100, 2, 16, 4).Encoding);
        Assert.Equal(SampleEncoding.IeeeFloat, AudioSampleFormat.FromWaveFormat(3, Guid.Empty, 48000, 2, 32, 8).Encoding);
    }

    [Theory]
    [InlineData(0x0055, 48000, 2, 16, 4)]   // MP3 tag
    [InlineData(1, 48000, 0, 16, 4)]        // no channels
    [InlineData(1, 48000, 2, 16, 3)]        // block align not a multiple of channels
    [InlineData(3, 48000, 2, 16, 4)]        // 16-bit float
    [InlineData(1, 100, 2, 16, 4)]          // absurd rate
    public void FromWaveFormat_RejectsUnsupported(int tag, int rate, int ch, int bits, int align)
        => Assert.Throws<NotSupportedException>(() => AudioSampleFormat.FromWaveFormat(tag, Guid.Empty, rate, ch, bits, align));

    [Fact]
    public void LoopbackConverter_48kStereo_Gives5512Mono_WithOriginalPeak()
    {
        var conv = new LoopbackConverter(Float48Stereo);
        // Left: 1 kHz at 0.8, right: silent. The mono mix is 0.4, but the meter must show the original 0.8 peak.
        float[] left = Signals.Sine(1000, 48000, 1, 0.8);
        var stereo = new float[left.Length * 2];
        for (int i = 0; i < left.Length; i++) stereo[i * 2] = left[i];
        var output = new List<float>();
        float lastDb = 0;
        for (int i = 0; i < left.Length; i += 480)
        {
            output.AddRange(conv.Convert(Signals.FloatBytes(stereo[(i * 2)..((i + 480) * 2)]), out float db));
            lastDb = db;
        }

        Assert.InRange(lastDb, 20 * Math.Log10(0.8) - 0.1, 20 * Math.Log10(0.8) + 0.1);
        Assert.InRange(output.Count, 5512 - 25, 5512);
        var steady = output.ToArray().AsSpan(200);
        Assert.InRange(Signals.Rms(steady), 0.4 / Math.Sqrt(2) * 0.97, 0.4 / Math.Sqrt(2) * 1.03);
        Assert.True(Signals.PowerAt(steady, 1000, 5512) > 1000 * Signals.PowerAt(steady, 1300, 5512));
    }
}

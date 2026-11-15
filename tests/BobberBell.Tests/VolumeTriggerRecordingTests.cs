using System;
using System.IO;
using BobberBell.Core.Audio;
using BobberBell.Core.Detection;

namespace BobberBell.Tests;

/// <summary>
/// Regression test against a 90 s loopback capture from a private server where fingerprinting never matched
/// (48 kHz float stereo). The capture is game audio and is not committed; set BOBBERBELL_LOOP_WAV to its path to run.
/// Recording time 0 = 17:54:20. Casts were logged at 17:54:13, 17:54:54 and 17:55:34 (-7 s, 34 s, 74 s);
/// splashes start at 16.95 s and 46.60 s, cast sounds at 34.15 s and 74.65 s.
/// </summary>
public class VolumeTriggerRecordingTests
{
    // The app starts listening 0.4-0.7 s after the cast key press.
    private const double CastSettle = 0.55;

    private static (byte[] Data, AudioSampleFormat Format) Load()
    {
        string? path = Environment.GetEnvironmentVariable("BOBBERBELL_LOOP_WAV");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            Assert.Skip("BOBBERBELL_LOOP_WAV not set (private game-audio capture, not in the repo)");
        byte[] all = File.ReadAllBytes(path!);
        int pos = 12;
        while (pos + 8 <= all.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(all, pos, 4);
            int size = BitConverter.ToInt32(all, pos + 4);
            if (id == "data") return (all.AsSpan(pos + 8, Math.Min(size, all.Length - pos - 8)).ToArray(), new AudioSampleFormat(SampleEncoding.IeeeFloat, 48000, 2, 32, 8));
            pos += 8 + size;
        }

        throw new InvalidDataException("no data chunk");
    }

    // Replays one listen window: from the listen start after a cast at castTime (recording seconds) for up to 40 s,
    // in 10 ms chunks. Time before the recording started is fed as silence.
    private static VolumeTriggerDetector Window(byte[] data, AudioSampleFormat fmt, double castTime, VolumeTriggerOptions options)
    {
        var d = new VolumeTriggerDetector(fmt, options);
        double start = castTime + CastSettle, end = Math.Min(start + 40, data.Length / (double)(fmt.BlockAlign * fmt.SampleRate));
        const int chunk = 480 * 8;
        if (start < 0) d.PushMono(new float[(int)(-start * fmt.SampleRate)]);
        long from = (long)(Math.Max(0, start) * fmt.SampleRate) * fmt.BlockAlign, to = (long)(end * fmt.SampleRate) * fmt.BlockAlign;
        for (long i = from; i < to && d.Bite == null; i += chunk)
            d.Push(data.AsSpan((int)i, (int)Math.Min(chunk, to - i)), out _);
        d.Complete();
        return d;
    }

    [Fact]
    public void Defaults_DetectBothBites_AtTheRightTime()
    {
        var (data, fmt) = Load();
        var first = Window(data, fmt, -7, new VolumeTriggerOptions());
        Assert.NotNull(first.Bite);
        Assert.InRange(-7 + CastSettle + first.Bite!.StartSeconds, 16.8, 17.2);

        var second = Window(data, fmt, 34, new VolumeTriggerOptions());
        Assert.NotNull(second.Bite);
        Assert.InRange(34 + CastSettle + second.Bite!.StartSeconds, 46.5, 46.8);
    }

    [Fact]
    public void Defaults_IgnoreTheLastCast()
    {
        var (data, fmt) = Load();
        Assert.Null(Window(data, fmt, 74, new VolumeTriggerOptions()).Bite);
    }

    // Margin: with no ignore time at all, the cast sounds are still rejected (too short and too bright) while both bites
    // are still found.
    [Fact]
    public void WithoutIgnoreTime_CastsAreStillRejected_AndBitesFound()
    {
        var (data, fmt) = Load();
        var noIgnore = new VolumeTriggerOptions(IgnoreSeconds: 0);
        foreach (double cast in new[] { 34.0, 74.0 })
        {
            var d = new VolumeTriggerDetector(fmt, noIgnore);
            long from = (long)(cast * fmt.SampleRate) * fmt.BlockAlign, to = (long)((cast + 2.5) * fmt.SampleRate) * fmt.BlockAlign;
            d.Push(data.AsSpan((int)from, (int)(to - from)), out _);
            d.Complete();
            Assert.Null(d.Bite);
        }

        Assert.NotNull(Window(data, fmt, -7, noIgnore).Bite);
        Assert.NotNull(Window(data, fmt, 34, noIgnore).Bite);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BobberBell.Core.Audio;
using BobberBell.Core.Detection;
using BobberBell.Core.Settings;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.InMemory;

namespace BobberBell.Tests;

// Simulates WASAPI loopback (48 kHz float stereo, 10 ms chunks) carrying a splash and checks the live pipeline detects it.
[Collection(SoundFilesCollection.Name)]
public class LiveDetectionPipelineTests
{
    private static readonly string SoundsDir = Path.Combine(AppContext.BaseDirectory, "Sounds");
    private static readonly AudioSampleFormat Loopback = new(SampleEncoding.IeeeFloat, 48000, 2, 32, 8);
    private static readonly DetectionThresholds Defaults = new((float)SettingsLimits.DefaultSimilarity, (float)SettingsLimits.DefaultMinVolume);

    private static async Task<SplashFingerprintEngine> EngineAsync(ListLogger log)
    {
        var engine = new SplashFingerprintEngine(log);
        var refs = Enumerable.Range(1, 3).Select(i => (Path.Combine(SoundsDir, $"bobbersplash-{i}.wav"), $"BobberSplash{i}")).ToList();
        await engine.LoadReferencesAsync(new SoundFingerprintingAudioService(), refs, TestContext.Current.CancellationToken);
        return engine;
    }

    // Upsamples a mono clip to 48 kHz stereo and frames it with background silence, like the game's output mix.
    private static float[] AsLoopback(float[] mono, int rate, double gain = 1.0)
    {
        float[] up = new StreamingResampler(rate, 48000).Process(mono.Concat(new float[rate]).ToArray());
        var padded = new float[48000 + up.Length + 96000];
        for (int i = 0; i < up.Length; i++) padded[48000 + i] = (float)(up[i] * gain);
        return Signals.Interleave(padded, 2);
    }

    private static async Task<(string? Label, ListenSession Session)> PlayAsync(SplashFingerprintEngine engine, float[] stereo)
    {
        var session = engine.StartSession(Loopback, Defaults, onLevel: null, TestContext.Current.CancellationToken);
        for (int i = 0; i < stereo.Length; i += 960)
            session.Push(Signals.FloatBytes(stereo[i..Math.Min(i + 960, stereo.Length)]));
        session.Complete(); // end of stream: the query drains what was pushed and ends
        var done = await Task.WhenAny(session.Bite, session.Completion, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        if (done != session.Bite) await Task.Delay(100, TestContext.Current.CancellationToken);
        done = session.Bite.IsCompleted ? session.Bite : done;
        return (done == session.Bite ? await session.Bite : null, session);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Splash_ThroughSimulatedLoopback_IsDetected(int n)
    {
        var log = new ListLogger();
        var engine = await EngineAsync(log);
        var (wav, rate) = Signals.ReadWav16(Path.Combine(SoundsDir, $"bobbersplash-{n}.wav"));
        var (label, session) = await PlayAsync(engine, AsLoopback(wav, rate));
        await using (session)
        {
            Assert.Equal($"BobberSplash{n}", label);
            Assert.True(session.PeakSimilarity >= Defaults.MinSimilarity, $"peak similarity {session.PeakSimilarity:0.00}");
            Assert.Equal(0, session.QueryFailures);
        }

        Assert.DoesNotContain(log.Lines, l => l.StartsWith('E'));
    }

    [Fact]
    public async Task UnrelatedTone_IsNotDetected()
    {
        var log = new ListLogger();
        var engine = await EngineAsync(log);
        float[] tone = Signals.Sine(880, 48000, 3, 0.5).Select((v, i) => v * (float)(0.5 + 0.5 * Math.Sin(i / 4800.0))).ToArray();
        var (label, session) = await PlayAsync(engine, AsLoopback(tone, 48000));
        await using (session)
        {
            Assert.Null(label);
            Assert.True(session.SamplesQueued > 0, "the tone passed the volume gate, so it was fingerprinted");
            Assert.True(session.PeakSimilarity < Defaults.MinSimilarity);
        }

        Assert.DoesNotContain(log.Lines, l => l.StartsWith('E'));
    }

    [Fact]
    public async Task SplashBelowVolumeGate_IsNotDetected()
    {
        var log = new ListLogger();
        var engine = await EngineAsync(log);
        var (wav, rate) = Signals.ReadWav16(Path.Combine(SoundsDir, "bobbersplash-1.wav"));
        var (label, session) = await PlayAsync(engine, AsLoopback(wav, rate, gain: 0.003)); // peak about -50 dBFS < -35 gate
        await using (session)
        {
            Assert.Null(label);
            Assert.Equal(0, session.SamplesQueued);
        }
    }

    [Fact]
    public async Task QueryFailure_IsLoggedOnce_AndQueryRestarts()
    {
        var log = new ListLogger();
        var engine = new SplashFingerprintEngine(log);
        int calls = 0;
        var restartedThreeTimes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task FailingQuery(CancellationToken ct)
        {
            if (Interlocked.Increment(ref calls) <= 3) throw new ArgumentException("Provided samples are not sampled in the required sample rate 5512");
            restartedThreeTimes.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }

        var session = engine.StartSession(Loopback, Defaults, FailingQuery, TestContext.Current.CancellationToken);
        await using (session)
        {
            await restartedThreeTimes.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(3, session.QueryFailures);
            Assert.False(session.Completion.IsCompleted, "the query must keep running after failures");
        }

        Assert.True(session.Completion.IsCompletedSuccessfully);
        Assert.Single(log.Lines, l => l.StartsWith('E') && l.Contains("restarting", StringComparison.Ordinal));
    }

    // Root cause of the v1.0.0 bug: raw 48 kHz loopback samples are rejected by the realtime query.
    [Fact]
    public async Task RawLoopbackRate_IsRejectedBySoundFingerprinting()
    {
        var model = new InMemoryModelService();
        var raw = new AudioSamples(Signals.Sine(1000, 48000, 1), "loopback", 48000);
        async IAsyncEnumerable<AudioSamples> One()
        {
            yield return raw;
            await Task.Yield();
        }

        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(() => QueryCommandBuilder.Instance
            .BuildRealtimeQueryCommand()
            .From(One())
            .WithRealtimeQueryConfig(c => c)
            .UsingServices(model)
            .Query(CancellationToken.None));
        Assert.Contains("5512", ex.Message, StringComparison.Ordinal);
    }
}

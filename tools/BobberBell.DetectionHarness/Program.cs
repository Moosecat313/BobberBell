using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BobberBell.Core.Audio;
using BobberBell.Core.Detection;
using BobberBell.Core.Logging;
using BobberBell.Core.Settings;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SoundFingerprinting.Audio.NAudio;

namespace BobberBell.DetectionHarness;

/// <summary>
/// Live end-to-end detection check: for each clip, captures loopback on the chosen render device with the app's own
/// <see cref="SplashFingerprintEngine"/> while playing the clip through that device, then reports the result.
/// Sends no input to any window.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string deviceHint = args.Length > 0 ? args[0] : "Alienware AW520H";
        string soundsDir = Path.Combine(AppContext.BaseDirectory, "Sounds");
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(deviceHint, StringComparison.OrdinalIgnoreCase));
        if (device == null)
        {
            Console.WriteLine($"No active render device matching '{deviceHint}'.");
            return 2;
        }

        Console.WriteLine($"Device: {device.FriendlyName}");
        var logger = new ConsoleLogger();
        var engine = new SplashFingerprintEngine(logger);
        var refs = Enumerable.Range(1, 3).Select(i => (Path.Combine(soundsDir, $"bobbersplash-{i}.wav"), $"BobberSplash{i}")).ToList();
        await engine.LoadReferencesAsync(new NAudioService(), refs).ConfigureAwait(false);
        var thresholds = new DetectionThresholds((float)SettingsLimits.DefaultSimilarity, (float)SettingsLimits.DefaultMinVolume);
        Console.WriteLine($"Thresholds: similarity >= {thresholds.MinSimilarity:0.00}, volume >= {thresholds.MinVolumeDb:0} dBFS");

        var clips = new List<(string Name, Func<ISampleProvider> Make, bool Expect)>();
        var readers = new List<WaveFileReader>();
        ISampleProvider Open(string path)
        {
            var reader = new WaveFileReader(path);
            readers.Add(reader);
            return reader.ToSampleProvider();
        }

        foreach (var (path, label) in refs)
            clips.Add((label, () => Open(path), true));
        // Controls are written to temporary WAVs so they take exactly the same playback path as the splashes.
        string tempDir = Path.Combine(Path.GetTempPath(), "bb-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string tone = WriteWav(Path.Combine(tempDir, "tone.wav"), Warble(44100, 3.0), 44100);
        string noise = WriteWav(Path.Combine(tempDir, "noise.wav"), Noise(44100, 2.0), 44100);
        clips.Add(("Tone 880 Hz warble (control)", () => Open(tone), false));
        clips.Add(("White noise burst (control)", () => Open(noise), false));

        int failures = 0;
        foreach (var clip in clips)
        {
            var r = await RunClipAsync(device, engine, thresholds, clip.Make()).ConfigureAwait(false);
            readers.ForEach(x => x.Dispose());
            readers.Clear();
            bool ok = clip.Expect ? r.Label == clip.Name : r.Label == null;
            if (!ok) failures++;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{(ok ? "PASS" : "FAIL")}  {clip.Name,-30} detected={r.Label ?? "none",-14} peakSimilarity={r.PeakSimilarity:0.00} " +
                $"lastSimilarity={r.SimilarityAtBite:0.00} detectedAfter={(r.Latency.HasValue ? r.Latency.Value.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "-")} " +
                $"peakLevel={r.PeakDb:0.0} dBFS queryFailures={r.QueryFailures} format={r.Format}"));
        }

        try { Directory.Delete(tempDir, true); }
        catch (IOException) { }

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<ClipResult> RunClipAsync(MMDevice device, SplashFingerprintEngine engine, DetectionThresholds thresholds, ISampleProvider clip)
    {
        float peakDb = DetectionThresholds.SilenceDb;
        float lastSimilarity = 0;
        void OnSim(float s) => lastSimilarity = s;
        engine.SimilarityMeasured += OnSim;
        using var cts = new CancellationTokenSource();
        using var capture = new WasapiLoopbackCapture(device);
        var w = capture.WaveFormat;
        var format = AudioSampleFormat.FromWaveFormat((int)w.Encoding, w is WaveFormatExtensible ext ? ext.SubFormat : Guid.Empty, w.SampleRate, w.Channels, w.BitsPerSample, w.BlockAlign);
        var session = engine.StartSession(format, thresholds, db => { if (db > peakDb) peakDb = db; }, cts.Token);
        capture.DataAvailable += (s, a) => { if (a.BytesRecorded > 0) session.Push(a.Buffer.AsSpan(0, a.BytesRecorded)); };
        capture.StartRecording();
        await Task.Delay(1000).ConfigureAwait(false); // background before the clip

        var sw = Stopwatch.StartNew();
        TimeSpan? latency = null;
        var biteTimed = session.Bite.ContinueWith(_ => latency = sw.Elapsed, TaskScheduler.Default);
        Trace("play");
        using (var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 50))
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (s, e) =>
            {
                if (e.Exception != null) Console.WriteLine("  playback error: " + e.Exception.Message);
                finished.TrySetResult();
            };
            output.Init(clip);
            output.Play();
            await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Trace($"played ({sw.Elapsed.TotalSeconds:0.00} s)");
            output.Stop();
        }

        await Task.WhenAny(session.Bite, Task.Delay(3000)).ConfigureAwait(false); // allow for detection latency
        Trace("stop capture");
        capture.StopRecording();
        string? label = session.Bite.IsCompleted ? await session.Bite.ConfigureAwait(false) : null;
        float atBite = lastSimilarity;
        await cts.CancelAsync().ConfigureAwait(false);
        Trace("dispose session");
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        Trace("disposed");
        engine.SimilarityMeasured -= OnSim;
        if (session.Bite.IsCompleted) await biteTimed.ConfigureAwait(false);
        string fmt = $"{format.Encoding} {format.SampleRate} Hz {format.Channels} ch {format.BitsPerSample}-bit";
        return new ClipResult(label, session.PeakSimilarity, atBite, latency, peakDb, session.QueryFailures, fmt);
    }

    private static void Trace(string stage)
    {
        if (Environment.GetEnvironmentVariable("BB_HARNESS_TRACE") == "1") Console.WriteLine("  . " + stage);
    }

    private static float[] Warble(int rate, double seconds)
    {
        var s = new float[(int)(rate * seconds)];
        for (int i = 0; i < s.Length; i++)
            s[i] = (float)(0.3 * Math.Sin(2 * Math.PI * (880 + (60 * Math.Sin(2 * Math.PI * 5 * i / rate))) * i / rate));
        return s;
    }

    private static float[] Noise(int rate, double seconds)
    {
        var s = new float[(int)(rate * seconds)];
        var rng = new Random(1234);
#pragma warning disable CA5394 // test signal, not security
        for (int i = 0; i < s.Length; i++) s[i] = (float)((rng.NextDouble() - 0.5) * 0.4);
#pragma warning restore CA5394
        return s;
    }

    private static string WriteWav(string path, float[] samples, int rate)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(rate, 16, 1));
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    private sealed record ClipResult(string? Label, float PeakSimilarity, float SimilarityAtBite, TimeSpan? Latency, float PeakDb, int QueryFailures, string Format);

    private sealed class ConsoleLogger : IAppLogger
    {
        public event EventHandler<LogEntry>? EntryWritten;

        public void Info(string message) => Console.WriteLine("[info] " + message);

        public void Warn(string message) => Console.WriteLine("[warn] " + message);

        public void Error(string message, Exception? exception = null)
        {
            Console.WriteLine("[error] " + message + (exception != null ? ": " + exception.Message : string.Empty));
            EntryWritten?.Invoke(this, new LogEntry(DateTime.Now, LogLevel.Error, message));
        }
    }
}

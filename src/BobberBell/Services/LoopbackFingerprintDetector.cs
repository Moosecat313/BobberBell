using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BobberBell.Core.Detection;
using BobberBell.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Audio.NAudio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;

namespace BobberBell.Services;

/// <summary>
/// WASAPI loopback capture → per-chunk loudness gate → SoundFingerprinting realtime query against an in-memory
/// index of the reference sounds. All audio stays in memory; nothing is recorded to disk or sent anywhere.
/// </summary>
internal sealed class LoopbackFingerprintDetector : IAudioDetector
{
    private readonly InMemoryModelService modelService = new();
    private readonly IAudioService audioService = new NAudioService();
    private readonly IAppLogger logger;
    private readonly object captureGate = new();

    // trackId -> label. Replaced as a whole (never mutated after publish), so query callbacks see a consistent set.
    private volatile IReadOnlyDictionary<string, string> activeTracks = new Dictionary<string, string>();
    private WasapiLoopbackCapture? loopback;
    private BlockingCollection<AudioSamples>? samples;
    private Task? queryTask;
    private CancellationTokenSource? listenCts;
    private volatile bool capturing;

    /// <summary>Creates the detector.</summary>
    /// <param name="logger">Application logger.</param>
    public LoopbackFingerprintDetector(IAppLogger logger) => this.logger = logger;

    /// <inheritdoc />
    public event Action<float>? LevelMeasured;

    /// <inheritdoc />
    public event Action<float>? SimilarityMeasured;

    /// <inheritdoc />
    public bool HasReferences => activeTracks.Count > 0;

    /// <inheritdoc />
    public async Task LoadReferencesAsync(IReadOnlyList<(string Path, string Label)> sounds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        var old = activeTracks;
        activeTracks = new Dictionary<string, string>();
        foreach (var id in old.Keys)
        {
            try { await modelService.DeleteTrackAsync(id).ConfigureAwait(false); }
            catch (InvalidOperationException) { }
        }

        var loaded = new Dictionary<string, string>();
        foreach (var (filePath, label) in sounds)
        {
            ct.ThrowIfCancellationRequested();
            string id = Guid.NewGuid().ToString();
            var track = new TrackInfo(id, label, "library");
            var hashes = await FingerprintCommandBuilder.Instance
                .BuildFingerprintCommand()
                .From(filePath)
                .UsingServices(audioService)
                .Hash()
                .ConfigureAwait(false);
            await modelService.InsertAsync(track, hashes).ConfigureAwait(false);
            loaded[id] = label;
        }

        activeTracks = loaded;
    }

    /// <inheritdoc />
    public async Task<string?> ListenAsync(MMDevice? device, DetectionThresholds thresholds, TimeSpan window, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var bite = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        StartCapture(device, thresholds, bite, cts);
        try
        {
            var timeout = Task.Delay(window, cts.Token);
            var done = await Task.WhenAny(bite.Task, timeout).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            return done == bite.Task ? await bite.Task.ConfigureAwait(true) : null;
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(true);
            await StopCaptureAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        try { listenCts?.Cancel(); }
        catch (ObjectDisposedException) { }
        StopCaptureDevice();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Stop();
        await StopCaptureAsync().ConfigureAwait(false);
    }

    private void StartCapture(MMDevice? device, DetectionThresholds thresholds, TaskCompletionSource<string> bite, CancellationTokenSource cts)
    {
        StopCaptureDevice();
        var queue = new BlockingCollection<AudioSamples>();
        var capture = device != null ? new WasapiLoopbackCapture(device) : new WasapiLoopbackCapture();
        int sampleRate = capture.WaveFormat.SampleRate;

        capture.DataAvailable += (s, a) =>
        {
            if (!capturing || queue.IsAddingCompleted) return;

            var chunk = new float[a.BytesRecorded / 4];
            Buffer.BlockCopy(a.Buffer, 0, chunk, 0, a.BytesRecorded);
            float db = DetectionThresholds.PeakDb(chunk);
            LevelMeasured?.Invoke(db);
            if (!thresholds.PassesVolume(db))
                return; // too quiet: skip fingerprinting this chunk

            try { queue.Add(new AudioSamples(chunk, "loopback", sampleRate)); }
            catch (InvalidOperationException) { } // completed between the check and the add
        };

        lock (captureGate)
        {
            loopback = capture;
            samples = queue;
            listenCts = cts;
            capturing = true;
        }

        capture.StartRecording();
        var token = cts.Token;
        queryTask = Task.Run(() => RunRealtimeQueryAsync(queue, thresholds, bite, token), CancellationToken.None);
    }

    private void StopCaptureDevice()
    {
        WasapiLoopbackCapture? capture;
        BlockingCollection<AudioSamples>? queue;
        lock (captureGate)
        {
            capturing = false;
            capture = loopback;
            queue = samples;
            loopback = null;
        }

        if (capture != null)
        {
            try { capture.StopRecording(); }
            catch (InvalidOperationException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            capture.Dispose();
        }

        if (queue != null && !queue.IsAddingCompleted) queue.CompleteAdding();
    }

    // Stops the device and waits for the fingerprint query to finish, so no work outlives the listen window.
    private async Task StopCaptureAsync()
    {
        StopCaptureDevice();
        var task = queryTask;
        queryTask = null;
        if (task != null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.Error("Detection query failed", ex); } // A failed query must not crash the session; it is logged instead.
        }

        BlockingCollection<AudioSamples>? queue;
        lock (captureGate)
        {
            queue = samples;
            samples = null;
            listenCts = null;
        }

        queue?.Dispose();
    }

    private static async IAsyncEnumerable<AudioSamples> ConsumeAsync(BlockingCollection<AudioSamples> queue, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var sample in queue.GetConsumingEnumerable(token))
        {
            yield return sample;
            await Task.Yield();
        }
    }

    private async Task RunRealtimeQueryAsync(BlockingCollection<AudioSamples> queue, DetectionThresholds thresholds, TaskCompletionSource<string> bite, CancellationToken token)
    {
        try
        {
            await QueryCommandBuilder.Instance
                .BuildRealtimeQueryCommand()
                .From(ConsumeAsync(queue, token))
                .WithRealtimeQueryConfig(config =>
                {
                    config.SuccessCallback = result =>
                    {
                        if (!capturing || token.IsCancellationRequested)
                            return Task.CompletedTask;

                        var tracks = activeTracks;
                        var best = result.ResultEntries?
                            .Where(x => x.Audio != null && tracks.ContainsKey(x.TrackId))
                            .OrderByDescending(x => x.Audio!.Confidence)
                            .FirstOrDefault();
                        if (best?.Audio != null)
                        {
                            SimilarityMeasured?.Invoke((float)best.Audio.Confidence);
                            if (thresholds.IsMatch(best.Audio.Confidence))
                                bite.TrySetResult(tracks.TryGetValue(best.TrackId, out var lbl) ? lbl : "target");
                        }

                        return Task.CompletedTask;
                    };
                    config.DidNotPassFilterCallback = result =>
                    {
                        var best = result.ResultEntries?.FirstOrDefault();
                        if (best?.Audio != null && activeTracks.ContainsKey(best.TrackId))
                            SimilarityMeasured?.Invoke((float)best.Audio.Confidence);
                        return Task.CompletedTask;
                    };
                    return config;
                })
                .UsingServices(modelService)
                .Query(token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

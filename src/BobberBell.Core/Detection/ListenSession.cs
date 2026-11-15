using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BobberBell.Core.Audio;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Command;

namespace BobberBell.Core.Detection;

/// <summary>
/// One listen window: converts pushed capture chunks to 5512 Hz mono, gates them on loudness and feeds a realtime
/// fingerprint query. If the query throws, the error is logged once and the query restarts for the rest of the
/// session, so one bad chunk cannot silently end detection.
/// </summary>
/// <remarks>
/// Chunks below the volume gate are replaced by silence of the same length rather than dropped, so the query's
/// timeline keeps moving (quiet audio still cannot match, but matches are not held back waiting for more audio).
/// A match is reported as soon as <see cref="MinMatchSeconds"/> of a reference has been heard, instead of waiting
/// for the whole reference to finish (the library default needs 5 s and waits, which a 3 s splash never passes).
/// </remarks>
public sealed class ListenSession : IAsyncDisposable
{
    /// <summary>Seconds of a reference that must be matched before the similarity gate is checked.</summary>
    public const double MinMatchSeconds = 1.0;

    private static readonly TimeSpan RestartDelay = TimeSpan.FromMilliseconds(250);

    private readonly SplashFingerprintEngine engine;
    private readonly LoopbackConverter converter;
    private readonly DetectionThresholds thresholds;
    private readonly Action<float>? onLevel;
    private readonly BlockingCollection<AudioSamples> queue = new();
    private readonly CancellationTokenSource cts;
    private readonly TaskCompletionSource<string> bite = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object pushGate = new();
    private readonly Task queryLoop;
    private readonly Func<CancellationToken, Task> query;
    private int queryFailures;
    private int disposed;

    internal ListenSession(SplashFingerprintEngine engine, LoopbackConverter converter, DetectionThresholds thresholds, Action<float>? onLevel, CancellationToken ct, Func<CancellationToken, Task>? queryOverride = null)
    {
        query = queryOverride ?? QueryAsync;
        this.engine = engine;
        this.converter = converter;
        this.thresholds = thresholds;
        this.onLevel = onLevel;
        cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = cts.Token;
        queryLoop = Task.Run(() => RunQueryLoopAsync(token), CancellationToken.None);
    }

    /// <summary>Gets a task that completes with the label of the first reference heard.</summary>
    public Task<string> Bite => bite.Task;

    /// <summary>Gets a task that completes when the query has ended (after <see cref="Complete"/> or cancellation).</summary>
    public Task Completion => queryLoop;

    /// <summary>Gets how many times the fingerprint query failed and was restarted.</summary>
    public int QueryFailures => Volatile.Read(ref queryFailures);

    /// <summary>Gets the number of 5512 Hz samples that passed the volume gate so far.</summary>
    public long SamplesQueued { get; private set; }

    /// <summary>Highest similarity seen against any reference during this session.</summary>
    public float PeakSimilarity { get; private set; }

    /// <summary>Pushes the next chunk of capture bytes (call from the capture thread, in order).</summary>
    /// <param name="data">Interleaved bytes in the session's capture format.</param>
    public void Push(ReadOnlySpan<byte> data)
    {
        lock (pushGate)
        {
            if (queue.IsAddingCompleted) return;
            float[] mono = converter.Convert(data, out float db);
            onLevel?.Invoke(db);
            if (mono.Length == 0) return;
            if (!thresholds.PassesVolume(db))
                Array.Clear(mono); // too quiet: fingerprint silence instead, so this chunk cannot match but time advances

            try
            {
                queue.Add(new AudioSamples(mono, "loopback", LoopbackConverter.FingerprintSampleRate));
                if (thresholds.PassesVolume(db)) SamplesQueued += mono.Length;
            }
            catch (InvalidOperationException) { } // completed between the check and the add
        }
    }

    /// <summary>Stops accepting audio; the query drains what is queued and ends.</summary>
    public void Complete()
    {
        lock (pushGate)
        {
            if (!queue.IsAddingCompleted) queue.CompleteAdding();
        }
    }

    /// <summary>Stops the session and waits for the query to finish.</summary>
    /// <returns>A task that completes when the session is fully stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Complete();
        await cts.CancelAsync().ConfigureAwait(false);
        try { await queryLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        cts.Dispose();
        queue.Dispose();
    }

    private async Task RunQueryLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !queue.IsCompleted)
        {
            try
            {
                await query(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (Interlocked.Increment(ref queryFailures) == 1)
                    engine.Logger.Error("Detection query failed; restarting it for the rest of this listen window", ex);
                try { await Task.Delay(RestartDelay, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task QueryAsync(CancellationToken token)
    {
        await QueryCommandBuilder.Instance
            .BuildRealtimeQueryCommand()
            .From(ConsumeAsync(queue, token))
            .WithRealtimeQueryConfig(config =>
            {
                config.ResultEntryFilter = new TrackCoverageLengthEntryFilter(MinMatchSeconds, waitTillCompletion: false);
                config.SuccessCallback = result =>
                {
                    if (token.IsCancellationRequested) return Task.CompletedTask;
                    var tracks = engine.ActiveTracks;
                    var best = result.ResultEntries?
                        .Where(x => x.Audio != null && tracks.ContainsKey(x.TrackId))
                        .OrderByDescending(x => x.Audio!.Confidence)
                        .FirstOrDefault();
                    if (best?.Audio != null)
                    {
                        Note((float)best.Audio.Confidence);
                        if (thresholds.IsMatch(best.Audio.Confidence))
                            bite.TrySetResult(tracks.TryGetValue(best.TrackId, out var lbl) ? lbl : "target");
                    }

                    return Task.CompletedTask;
                };
                config.DidNotPassFilterCallback = result =>
                {
                    var best = result.ResultEntries?.FirstOrDefault();
                    if (best?.Audio != null && engine.ActiveTracks.ContainsKey(best.TrackId))
                        Note((float)best.Audio.Confidence);
                    return Task.CompletedTask;
                };
                return config;
            })
            .UsingServices(engine.ModelService)
            .Query(token)
            .ConfigureAwait(false);
    }

    private void Note(float similarity)
    {
        if (similarity > PeakSimilarity) PeakSimilarity = similarity;
        engine.ReportSimilarity(similarity);
    }

    private static async IAsyncEnumerable<AudioSamples> ConsumeAsync(BlockingCollection<AudioSamples> queue, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var sample in queue.GetConsumingEnumerable(token))
        {
            yield return sample;
            await Task.Yield();
        }
    }
}

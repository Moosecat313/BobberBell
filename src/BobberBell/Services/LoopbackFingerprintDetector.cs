using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BobberBell.Core.Audio;
using BobberBell.Core.Detection;
using BobberBell.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using IAudioService = SoundFingerprinting.Audio.IAudioService;
using SoundFingerprinting.Audio.NAudio;

namespace BobberBell.Services;

/// <summary>
/// WASAPI loopback capture feeding a <see cref="SplashFingerprintEngine"/> session. The engine converts the device's
/// native format (typically 48 kHz float stereo) to the 5512 Hz mono stream SoundFingerprinting requires, gates each
/// chunk on loudness and runs the realtime query. All audio stays in memory; nothing is recorded to disk or sent anywhere.
/// </summary>
internal sealed class LoopbackFingerprintDetector : IAudioDetector
{
    private readonly SplashFingerprintEngine engine;
    private readonly IAudioService audioService = new NAudioService();
    private readonly IAppLogger logger;
    private readonly object captureGate = new();
    private WasapiLoopbackCapture? loopback;
    private ListenSession? session;
    private CancellationTokenSource? listenCts;

    /// <summary>Creates the detector.</summary>
    /// <param name="logger">Application logger.</param>
    public LoopbackFingerprintDetector(IAppLogger logger)
    {
        this.logger = logger;
        engine = new SplashFingerprintEngine(logger);
        engine.SimilarityMeasured += s => SimilarityMeasured?.Invoke(s);
    }

    /// <inheritdoc />
    public event Action<float>? LevelMeasured;

    /// <inheritdoc />
    public event Action<float>? SimilarityMeasured;

    /// <inheritdoc />
    public bool HasReferences => engine.HasReferences;

    /// <inheritdoc />
    public Task LoadReferencesAsync(IReadOnlyList<(string Path, string Label)> sounds, CancellationToken ct = default)
        => engine.LoadReferencesAsync(audioService, sounds, ct); // NAudioService decodes each file to 5512 Hz mono

    /// <summary>Maps an NAudio wave format to the engine's platform-neutral description.</summary>
    /// <param name="format">Device mix format.</param>
    /// <returns>The equivalent <see cref="AudioSampleFormat"/>.</returns>
    /// <exception cref="NotSupportedException">The format cannot be decoded.</exception>
    internal static AudioSampleFormat ToSampleFormat(WaveFormat format)
    {
        Guid sub = format is WaveFormatExtensible ext ? ext.SubFormat : Guid.Empty;
        return AudioSampleFormat.FromWaveFormat((int)format.Encoding, sub, format.SampleRate, format.Channels, format.BitsPerSample, format.BlockAlign);
    }

    /// <inheritdoc />
    public async Task<string?> ListenAsync(MMDevice? device, DetectionThresholds thresholds, TimeSpan window, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var current = StartCapture(device, thresholds, cts);
        try
        {
            var timeout = Task.Delay(window, cts.Token);
            var done = await Task.WhenAny(current.Bite, timeout).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            return done == current.Bite ? await current.Bite.ConfigureAwait(true) : null;
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(true);
            await StopCaptureAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task<string?> ListenVolumeTriggerAsync(MMDevice? device, VolumeTriggerOptions options, TimeSpan window, CancellationToken ct)
    {
        StopCaptureDevice();
        var capture = device != null ? new WasapiLoopbackCapture(device) : new WasapiLoopbackCapture();
        VolumeTriggerDetector trigger;
        try
        {
            trigger = new VolumeTriggerDetector(ToSampleFormat(capture.WaveFormat), options);
        }
        catch (NotSupportedException ex)
        {
            capture.Dispose();
            logger.Error("Unsupported audio device format", ex);
            throw;
        }

        var gate = new object();
        var heard = new TaskCompletionSource<VolumeTriggerEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (s, a) =>
        {
            if (a.BytesRecorded <= 0) return;
            VolumeTriggerEvent? bite;
            float db;
            lock (gate)
            {
                if (heard.Task.IsCompleted) return;
                bite = trigger.Push(a.Buffer.AsSpan(0, a.BytesRecorded), out db);
            }

            LevelMeasured?.Invoke(db);
            if (bite != null) heard.TrySetResult(bite);
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (captureGate)
        {
            loopback = capture;
            listenCts = cts;
        }

        try
        {
            capture.StartRecording();
            var done = await Task.WhenAny(heard.Task, Task.Delay(window, cts.Token)).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (done != heard.Task) return null;
            var b = await heard.Task.ConfigureAwait(true);
            return string.Format(CultureInfo.InvariantCulture, "Splash (volume trigger: {0:0} dBFS, {1:0.0} s, tone {2:0.0} kHz)",
                b.PeakDb, b.DurationSeconds, b.CentroidHz / 1000);
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(true);
            StopCaptureDevice();
            lock (captureGate) listenCts = null;
            lock (gate)
            {
                trigger.Complete();
                foreach (var r in trigger.Rejected)
                {
                    logger.Info(string.Format(CultureInfo.InvariantCulture, "Ignored sound at {0:0.0} s: {1:0.00} s, {2:0} dBFS, tone {3:0.0} kHz ({4})",
                        r.StartSeconds, r.DurationSeconds, r.PeakDb, r.CentroidHz / 1000, r.Reason));
                }
            }
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

    private ListenSession StartCapture(MMDevice? device, DetectionThresholds thresholds, CancellationTokenSource cts)
    {
        StopCaptureDevice();
        var capture = device != null ? new WasapiLoopbackCapture(device) : new WasapiLoopbackCapture();
        ListenSession current;
        try
        {
            var format = ToSampleFormat(capture.WaveFormat);
            current = engine.StartSession(format, thresholds, db => LevelMeasured?.Invoke(db), cts.Token);
        }
        catch (NotSupportedException ex)
        {
            capture.Dispose();
            logger.Error("Unsupported audio device format", ex);
            throw;
        }

        capture.DataAvailable += (s, a) =>
        {
            if (a.BytesRecorded > 0) current.Push(a.Buffer.AsSpan(0, a.BytesRecorded));
        };

        lock (captureGate)
        {
            loopback = capture;
            session = current;
            listenCts = cts;
        }

        capture.StartRecording();
        return current;
    }

    private void StopCaptureDevice()
    {
        WasapiLoopbackCapture? capture;
        ListenSession? current;
        lock (captureGate)
        {
            capture = loopback;
            current = session;
            loopback = null;
        }

        if (capture != null)
        {
            try { capture.StopRecording(); }
            catch (InvalidOperationException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            capture.Dispose();
        }

        current?.Complete();
    }

    // Stops the device and waits for the fingerprint query to finish, so no work outlives the listen window.
    private async Task StopCaptureAsync()
    {
        StopCaptureDevice();
        ListenSession? current;
        lock (captureGate)
        {
            current = session;
            session = null;
            listenCts = null;
        }

        if (current != null)
        {
            try { await current.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { logger.Error("Detection query failed", ex); } // must not crash the session
        }
    }
}

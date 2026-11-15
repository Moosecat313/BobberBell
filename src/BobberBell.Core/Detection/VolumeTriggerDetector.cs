using System;
using System.Collections.Generic;
using BobberBell.Core.Audio;

namespace BobberBell.Core.Detection;

/// <summary>A sound the volume trigger heard, accepted as a bite or not.</summary>
/// <param name="StartSeconds">Start time from the beginning of the listen window.</param>
/// <param name="DurationSeconds">How long it stayed loud (so far, for a bite).</param>
/// <param name="PeakDb">Loudest 20 ms frame (dBFS, mono).</param>
/// <param name="CentroidHz">Spectral centroid (power-weighted, 100 Hz and up) over the sound, or 0 when not measured.</param>
/// <param name="IsBite">Whether it was accepted as a bite.</param>
/// <param name="Reason">Why it was rejected ("too short", "tone 4.1 kHz"), or empty for a bite.</param>
public sealed record VolumeTriggerEvent(double StartSeconds, double DurationSeconds, float PeakDb, double CentroidHz, bool IsBite, string Reason);

/// <summary>
/// Detects a bite from loudness instead of matching a reference recording: after an ignore time (the cast sound),
/// a sound that stays at or above a level for long enough, and whose spectral centroid is in the splash range,
/// is a bite. Works on the capture's native sample rate (no 5512 Hz resampling, so the centroid sees the full band).
/// Feed one listen window per instance. Not thread-safe; call <see cref="Push"/> from one thread.
/// </summary>
public sealed class VolumeTriggerDetector
{
    private const double MinCentroidBinHz = 100;
    private const double LogRejectedSeconds = 0.15;

    private readonly AudioSampleFormat format;
    private readonly int frameLength;
    private readonly int fftSize;
    private readonly double[] window;
    private readonly double[] re;
    private readonly double[] im;
    private readonly double[] eventPower;
    private readonly List<float> pending = new();
    private readonly List<VolumeTriggerEvent> rejected = new();
    private long frameIndex;
    private bool inEvent;
    private bool decided;
    private long eventStart;
    private long lastLoud;
    private float eventPeak;
    private VolumeTriggerEvent? bite;

    /// <summary>Creates a detector for one listen window.</summary>
    /// <param name="format">Capture format.</param>
    /// <param name="options">Thresholds.</param>
    /// <exception cref="NotSupportedException">The format cannot be decoded.</exception>
    public VolumeTriggerDetector(AudioSampleFormat format, VolumeTriggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        format.Validate();
        this.format = format;
        Options = options;
        frameLength = Math.Max(16, (int)Math.Round(format.SampleRate * VolumeTriggerOptions.FrameSeconds));
        fftSize = 1;
        while (fftSize < frameLength) fftSize <<= 1;
        window = new double[frameLength];
        for (int i = 0; i < frameLength; i++) window[i] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (frameLength - 1)));
        re = new double[fftSize];
        im = new double[fftSize];
        eventPower = new double[(fftSize / 2) + 1];
    }

    /// <summary>Gets the thresholds.</summary>
    public VolumeTriggerOptions Options { get; }

    /// <summary>Gets the bite, once heard.</summary>
    public VolumeTriggerEvent? Bite => bite;

    /// <summary>Gets loud sounds that were not accepted (at least 0.15 s long), for the log.</summary>
    public IReadOnlyList<VolumeTriggerEvent> Rejected => rejected;

    /// <summary>Gets the time analysed so far (s).</summary>
    public double ElapsedSeconds => frameIndex * VolumeTriggerOptions.FrameSeconds;

    /// <summary>Analyses the next chunk of captured bytes.</summary>
    /// <param name="data">Interleaved capture bytes.</param>
    /// <param name="peakDb">Peak level of the chunk (all channels, dBFS) for the meter.</param>
    /// <returns>The bite when this chunk completed one; otherwise <c>null</c>.</returns>
    public VolumeTriggerEvent? Push(ReadOnlySpan<byte> data, out float peakDb)
    {
        float[] mono = SampleDecoder.DownmixToMono(data, format, out float peak);
        peakDb = SampleDecoder.PeakToDb(peak);
        return PushMono(mono);
    }

    /// <summary>Analyses mono samples at the capture rate (used by tests and by <see cref="Push"/>).</summary>
    /// <param name="mono">Mono samples.</param>
    /// <returns>The bite when these samples completed one; otherwise <c>null</c>.</returns>
    public VolumeTriggerEvent? PushMono(ReadOnlySpan<float> mono)
    {
        if (bite != null) return null;
        foreach (float v in mono)
        {
            pending.Add(v);
            if (pending.Count < frameLength) continue;
            var found = ProcessFrame();
            pending.Clear();
            if (found != null) return found;
        }

        return null;
    }

    /// <summary>Ends the window: a sound still in progress is recorded as rejected if it was long enough to log.</summary>
    public void Complete()
    {
        if (inEvent && !decided) EndEvent();
        inEvent = false;
    }

    private VolumeTriggerEvent? ProcessFrame()
    {
        long idx = frameIndex++;
        double t = idx * VolumeTriggerOptions.FrameSeconds;
        if (t < Options.IgnoreSeconds) return null;

        float framePeak = 0f;
        foreach (float v in pending)
        {
            float a = Math.Abs(v);
            if (a > framePeak) framePeak = a;
        }

        float db = SampleDecoder.PeakToDb(framePeak);
        bool loud = db >= Options.LevelDb;
        if (!inEvent)
        {
            if (!loud) return null;
            inEvent = true;
            decided = false;
            eventStart = idx;
            eventPeak = db;
            Array.Clear(eventPower);
        }
        else if (!loud && (idx - lastLoud) * VolumeTriggerOptions.FrameSeconds > Options.BridgeSeconds)
        {
            EndEvent();
            return null;
        }

        if (loud)
        {
            lastLoud = idx;
            if (db > eventPeak) eventPeak = db;
        }

        AccumulateSpectrum();
        double duration = (lastLoud - eventStart + 1) * VolumeTriggerOptions.FrameSeconds;
        if (decided || duration < Options.MinSeconds) return null;

        decided = true;
        double centroid = Centroid();
        double start = eventStart * VolumeTriggerOptions.FrameSeconds;
        if (Options.ToneFilter && (centroid < Options.MinToneHz || centroid > Options.MaxToneHz))
        {
            rejected.Add(new VolumeTriggerEvent(start, duration, eventPeak, centroid, false, $"tone {centroid / 1000:0.0} kHz"));
            return null;
        }

        bite = new VolumeTriggerEvent(start, duration, eventPeak, centroid, true, string.Empty);
        return bite;
    }

    private void EndEvent()
    {
        inEvent = false;
        if (decided) return;
        double duration = (lastLoud - eventStart + 1) * VolumeTriggerOptions.FrameSeconds;
        if (duration >= LogRejectedSeconds)
            rejected.Add(new VolumeTriggerEvent(eventStart * VolumeTriggerOptions.FrameSeconds, duration, eventPeak, Centroid(), false, "too short"));
    }

    private void AccumulateSpectrum()
    {
        Array.Clear(re);
        Array.Clear(im);
        for (int i = 0; i < frameLength; i++) re[i] = pending[i] * window[i];
        Fft(re, im);
        for (int k = 0; k < eventPower.Length; k++) eventPower[k] += (re[k] * re[k]) + (im[k] * im[k]);
    }

    private double Centroid()
    {
        double binHz = (double)format.SampleRate / fftSize, num = 0, den = 0;
        for (int k = 0; k < eventPower.Length; k++)
        {
            double hz = k * binHz;
            if (hz < MinCentroidBinHz) continue;
            num += eventPower[k] * hz;
            den += eventPower[k];
        }

        return den > 0 ? num / den : 0;
    }

    /// <summary>In-place iterative radix-2 FFT (length must be a power of two).</summary>
    /// <param name="real">Real parts.</param>
    /// <param name="imag">Imaginary parts.</param>
    internal static void Fft(double[] real, double[] imag)
    {
        int n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + (len / 2);
                    double tr = (real[b] * cr) - (imag[b] * ci), ti = (real[b] * ci) + (imag[b] * cr);
                    real[b] = real[a] - tr;
                    imag[b] = imag[a] - ti;
                    real[a] += tr;
                    imag[a] += ti;
                    double ncr = (cr * wr) - (ci * wi);
                    ci = (cr * wi) + (ci * wr);
                    cr = ncr;
                }
            }
        }
    }
}

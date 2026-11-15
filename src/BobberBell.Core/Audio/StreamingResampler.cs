using System;

namespace BobberBell.Core.Audio;

/// <summary>
/// Band-limited sample-rate converter for a continuous mono stream (windowed-sinc interpolation with a Kaiser window).
/// The low-pass cutoff sits at 90% of the lower Nyquist frequency, so downsampling does not alias. State is kept across
/// <see cref="Process"/> calls, so chunk boundaries are seamless. Not thread-safe: use one instance per stream.
/// </summary>
public sealed class StreamingResampler
{
    private const int ZeroCrossings = 16;
    private const int TableResolution = 512; // kernel samples per zero crossing
    private const double KaiserBeta = 8.6;
    private const double Rolloff = 0.90;

    private static readonly float[] KernelTable = BuildKernelTable();

    private readonly double step;      // input samples per output sample
    private readonly double scale;     // cutoff relative to the input Nyquist (<= 1)
    private readonly double halfWidth; // kernel half width in input samples
    private float[] history = new float[4096];
    private int historyCount;
    private long historyStart;         // absolute input index of history[0]
    private long inputCount;           // total input samples received
    private long outputCount;          // total output samples produced

    /// <summary>Creates a resampler.</summary>
    /// <param name="inputRate">Input sample rate in Hz.</param>
    /// <param name="outputRate">Output sample rate in Hz.</param>
    public StreamingResampler(int inputRate, int outputRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(inputRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputRate, 1);
        InputRate = inputRate;
        OutputRate = outputRate;
        step = (double)inputRate / outputRate;
        scale = Math.Min(1.0, (double)outputRate / inputRate) * Rolloff;
        halfWidth = ZeroCrossings / scale;
    }

    /// <summary>Gets the input sample rate.</summary>
    public int InputRate { get; }

    /// <summary>Gets the output sample rate.</summary>
    public int OutputRate { get; }

    /// <summary>Gets the delay of the filter in input samples (output lags input by this much).</summary>
    public int LatencyInputSamples => (int)Math.Ceiling(halfWidth);

    /// <summary>Feeds input samples and returns every output sample that can be computed so far.</summary>
    /// <param name="input">Next mono input samples.</param>
    /// <returns>Resampled output (possibly empty).</returns>
    public float[] Process(ReadOnlySpan<float> input)
    {
        Append(input);
        long available = inputCount;
        // Output n is centred at input position n*step and needs input up to floor(centre + halfWidth).
        long last = (long)Math.Floor((available - 1 - halfWidth) / step);
        int count = (int)Math.Max(0, last - outputCount + 1);
        var output = new float[count];
        for (int i = 0; i < count; i++)
        {
            output[i] = Interpolate(outputCount * step);
            outputCount++;
        }

        Trim();
        return output;
    }

    private float Interpolate(double centre)
    {
        long first = Math.Max((long)Math.Ceiling(centre - halfWidth), 0);
        long end = (long)Math.Floor(centre + halfWidth);
        double acc = 0;
        for (long k = first; k <= end; k++)
        {
            double u = Math.Abs(centre - k) * scale * TableResolution;
            int i = (int)u;
            if (i >= KernelTable.Length - 1) continue;
            double frac = u - i;
            double h = KernelTable[i] + (KernelTable[i + 1] - KernelTable[i]) * frac;
            acc += h * history[k - historyStart];
        }

        return (float)(acc * scale);
    }

    private void Append(ReadOnlySpan<float> input)
    {
        if (historyCount + input.Length > history.Length)
            Array.Resize(ref history, Math.Max(history.Length * 2, historyCount + input.Length));
        for (int i = 0; i < input.Length; i++)
        {
            float v = input[i];
            history[historyCount + i] = float.IsFinite(v) ? v : 0f;
        }

        historyCount += input.Length;
        inputCount += input.Length;
    }

    // Drops input that no future output can reach.
    private void Trim()
    {
        long keepFrom = Math.Max(0, (long)Math.Ceiling(outputCount * step - halfWidth) - 1);
        int drop = (int)Math.Min(historyCount, Math.Max(0, keepFrom - historyStart));
        if (drop <= 0) return;
        Array.Copy(history, drop, history, 0, historyCount - drop);
        historyCount -= drop;
        historyStart += drop;
    }

    // sinc(u) * kaiser(u / ZeroCrossings) for u in [0, ZeroCrossings], TableResolution points per unit.
    private static float[] BuildKernelTable()
    {
        int n = (ZeroCrossings * TableResolution) + 2;
        var table = new float[n];
        double denom = BesselI0(KaiserBeta);
        for (int i = 0; i < n; i++)
        {
            double u = (double)i / TableResolution;
            if (u > ZeroCrossings) { table[i] = 0f; continue; }
            double sinc = u == 0 ? 1.0 : Math.Sin(Math.PI * u) / (Math.PI * u);
            double r = u / ZeroCrossings;
            double window = BesselI0(KaiserBeta * Math.Sqrt(1 - (r * r))) / denom;
            table[i] = (float)(sinc * window);
        }

        return table;
    }

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1, half = x / 2;
        for (int k = 1; k < 50; k++)
        {
            term *= half / k;
            double t2 = term * term;
            sum += t2;
            if (t2 < sum * 1e-17) break;
        }

        return sum;
    }
}

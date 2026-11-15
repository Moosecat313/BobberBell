using System;
using System.IO;
using BobberBell.Core.Licensing;
using BobberBell.Core.Session;

namespace BobberBell.Tests;

internal sealed class FakeTicks : ITickSource
{
    public long NowMs { get; set; } = 1_000_000;

    public void Advance(TimeSpan by) => NowMs += (long)by.TotalMilliseconds;
}

internal sealed class FakeMachineId(string id) : IMachineIdProvider
{
    public string Id { get; set; } = id;

    public string GetMachineId() => Id;
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Write(string name, int bytes)
    {
        string p = File(name);
        System.IO.File.WriteAllBytes(p, new byte[bytes]);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

internal sealed class ListLogger : BobberBell.Core.Logging.IAppLogger
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

    public event EventHandler<BobberBell.Core.Logging.LogEntry>? EntryWritten;

    public void Info(string message) => Lines.Enqueue("I " + message);

    public void Warn(string message) => Lines.Enqueue("W " + message);

    public void Error(string message, Exception? exception = null)
    {
        Lines.Enqueue("E " + message + (exception != null ? ": " + exception.Message : string.Empty));
        EntryWritten?.Invoke(this, null!);
    }
}

internal static class Signals
{
    public static float[] Sine(double hz, int rate, double seconds, double amplitude = 0.5)
    {
        var s = new float[(int)(rate * seconds)];
        for (int i = 0; i < s.Length; i++) s[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate));
        return s;
    }

    public static float[] Interleave(float[] mono, int channels)
    {
        var o = new float[mono.Length * channels];
        for (int i = 0; i < mono.Length; i++)
            for (int c = 0; c < channels; c++) o[(i * channels) + c] = mono[i];
        return o;
    }

    public static byte[] FloatBytes(float[] samples)
    {
        var b = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, b, 0, b.Length);
        return b;
    }

    public static double Rms(ReadOnlySpan<float> s)
    {
        double acc = 0;
        foreach (float v in s) acc += v * v;
        return Math.Sqrt(acc / Math.Max(1, s.Length));
    }

    // Goertzel power of one frequency (normalised by length).
    public static double PowerAt(ReadOnlySpan<float> s, double hz, int rate)
    {
        double w = 2 * Math.PI * hz / rate, coeff = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        foreach (float v in s)
        {
            double s0 = v + (coeff * s1) - s2;
            s2 = s1;
            s1 = s0;
        }

        return ((s1 * s1) + (s2 * s2) - (coeff * s1 * s2)) / ((double)s.Length * s.Length);
    }

    // Reads a 16-bit PCM WAV (the bundled splash files) as mono float.
    public static (float[] Samples, int Rate) ReadWav16(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        int pos = 12, rate = 0, channels = 1;
        while (pos + 8 <= d.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(d, pos, 4);
            int size = BitConverter.ToInt32(d, pos + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(d, pos + 10);
                rate = BitConverter.ToInt32(d, pos + 12);
            }
            else if (id == "data")
            {
                int frames = size / (2 * channels);
                var s = new float[frames];
                for (int i = 0; i < frames; i++) s[i] = BitConverter.ToInt16(d, pos + 8 + (i * 2 * channels)) / 32768f;
                return (s, rate);
            }

            pos += 8 + size + (size & 1);
        }

        throw new InvalidDataException("no data chunk");
    }
}

[CollectionDefinition(Name)]
public sealed class SoundFilesCollection
{
    public const string Name = "Bundled sound files";
}

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

using System;

namespace BobberBell.Core.Session;

/// <summary>Monotonic millisecond clock (immune to wall-clock changes).</summary>
public interface ITickSource
{
    /// <summary>Gets the current tick count in milliseconds.</summary>
    long NowMs { get; }
}

/// <summary><see cref="ITickSource"/> backed by <see cref="Environment.TickCount64"/>.</summary>
public sealed class SystemTickSource : ITickSource
{
    /// <summary>Gets the shared instance.</summary>
    public static SystemTickSource Instance { get; } = new();

    /// <inheritdoc />
    public long NowMs => Environment.TickCount64;
}

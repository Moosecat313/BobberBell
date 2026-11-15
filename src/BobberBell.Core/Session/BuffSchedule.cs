using System;

namespace BobberBell.Core.Session;

/// <summary>
/// When the next buff is due. Buffs are only applied between catches, so "due" means
/// "apply after the current catch".
/// </summary>
public sealed class BuffSchedule
{
    private readonly ITickSource ticks;

    /// <summary>Creates a schedule on the given clock.</summary>
    /// <param name="ticks">Monotonic clock.</param>
    public BuffSchedule(ITickSource ticks) => this.ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));

    /// <summary>Gets the tick at which the next buff is due, or <c>null</c> if not scheduled yet.</summary>
    public long? NextTick { get; private set; }

    /// <summary>Clears the schedule (session start or interval changed).</summary>
    public void Reset() => NextTick = null;

    /// <summary>Returns whether a buff is due, scheduling the first one an interval from now if needed.</summary>
    /// <param name="enabled">The Timed buffs toggle; when off the schedule is cleared.</param>
    /// <param name="intervalMinutes">Interval in minutes.</param>
    /// <returns><c>true</c> if a buff should be applied now.</returns>
    public bool IsDue(bool enabled, decimal intervalMinutes)
    {
        if (!enabled)
        {
            NextTick = null;
            return false;
        }

        NextTick ??= ticks.NowMs + IntervalMs(intervalMinutes);
        return ticks.NowMs >= NextTick;
    }

    /// <summary>Records that a buff was just applied; the next one is an interval from now.</summary>
    /// <param name="intervalMinutes">Interval in minutes.</param>
    public void MarkApplied(decimal intervalMinutes) => NextTick = ticks.NowMs + IntervalMs(intervalMinutes);

    /// <summary>Builds the "Next buff" status line.</summary>
    /// <param name="enabled">The Timed buffs toggle.</param>
    /// <param name="buffOnStart">The Buff on Start toggle.</param>
    /// <param name="isRunning">Whether a session is running.</param>
    /// <param name="intervalMinutes">Interval in minutes.</param>
    /// <returns>The status text.</returns>
    public string StatusText(bool enabled, bool buffOnStart, bool isRunning, decimal intervalMinutes)
    {
        if (!enabled) return "Next buff: off";
        if (!isRunning) return buffOnStart ? "Next buff: on Start" : $"Next buff: {intervalMinutes:0} min after Start";
        if (NextTick == null) return "Next buff: after the next catch";
        long now = ticks.NowMs;
        if (NextTick <= now) return "Next buff: after the current catch";
        var left = TimeSpan.FromMilliseconds(NextTick.Value - now);
        return $"Next buff: in {(int)left.TotalMinutes}:{left.Seconds:00} (between catches)";
    }

    private static long IntervalMs(decimal minutes) => (long)((double)minutes * 60_000);
}

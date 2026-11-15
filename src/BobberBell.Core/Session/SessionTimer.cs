using System;
using System.Globalization;

namespace BobberBell.Core.Session;

/// <summary>
/// Session time limit ("Stop after N h"). Uses a monotonic clock, so changing the system time
/// cannot extend or end a session early.
/// </summary>
public sealed class SessionTimer
{
    private readonly ITickSource ticks;
    private long? endTick;

    /// <summary>Creates a timer on the given clock.</summary>
    /// <param name="ticks">Monotonic clock.</param>
    public SessionTimer(ITickSource ticks) => this.ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));

    /// <summary>Gets a value indicating whether a limit is armed.</summary>
    public bool HasLimit => endTick != null;

    /// <summary>Arms the limit for a new session. Nothing is armed when auto-stop is off or hours is 0.</summary>
    /// <param name="autoStopEnabled">The Auto-stop toggle.</param>
    /// <param name="hours">Session length in hours (0 = no limit).</param>
    /// <returns><c>true</c> if a limit was armed.</returns>
    public bool Start(bool autoStopEnabled, decimal hours)
    {
        endTick = autoStopEnabled && hours > 0
            ? ticks.NowMs + (long)((double)hours * 3_600_000)
            : null;
        return endTick != null;
    }

    /// <summary>Disarms the limit (session stopped).</summary>
    public void Reset() => endTick = null;

    /// <summary>Gets the time left, or <c>null</c> when no limit is armed. May be zero or negative once expired.</summary>
    public TimeSpan? Remaining => endTick == null ? null : TimeSpan.FromMilliseconds(endTick.Value - ticks.NowMs);

    /// <summary>Gets a value indicating whether an armed limit has been reached.</summary>
    public bool IsExpired => Remaining is { } left && left <= TimeSpan.Zero;

    /// <summary>Builds the status line shown under the Start/Stop buttons.</summary>
    /// <param name="isRunning">Whether a session is running.</param>
    /// <param name="autoStopEnabled">The Auto-stop toggle.</param>
    /// <param name="hours">The Stop after value.</param>
    /// <returns>"Auto-stop in H:MM:SS", "No session limit", "Auto-stop after X h" or empty.</returns>
    public string StatusText(bool isRunning, bool autoStopEnabled, decimal hours)
    {
        if (isRunning && Remaining is { } left)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            return $"Auto-stop in {(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}";
        }

        if (isRunning) return "No session limit";
        if (autoStopEnabled && hours > 0)
            return "Auto-stop after " + hours.ToString("0.##", CultureInfo.CurrentCulture) + " h";
        return string.Empty;
    }
}

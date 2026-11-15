using System;
using BobberBell.Core.Session;

namespace BobberBell.Tests;

public class SessionTimerTests
{
    private readonly FakeTicks ticks = new();

    [Fact]
    public void Start_WithHours_ArmsLimit_AndCountsDown()
    {
        var t = new SessionTimer(ticks);
        Assert.True(t.Start(true, 0.25m));
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining);
        Assert.Equal("Auto-stop in 0:15:00", t.StatusText(true, true, 0.25m));
        ticks.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(1));
        Assert.Equal("Auto-stop in 0:00:59", t.StatusText(true, true, 0.25m));
        Assert.False(t.IsExpired);
    }

    [Fact]
    public void Expires_ExactlyAtLimit()
    {
        var t = new SessionTimer(ticks);
        t.Start(true, 1m);
        ticks.Advance(TimeSpan.FromHours(1) - TimeSpan.FromMilliseconds(1));
        Assert.False(t.IsExpired);
        ticks.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(t.IsExpired);
    }

    [Fact]
    public void ZeroHours_MeansNoLimit()
    {
        var t = new SessionTimer(ticks);
        Assert.False(t.Start(true, 0m));
        Assert.Null(t.Remaining);
        Assert.False(t.IsExpired);
        Assert.Equal("No session limit", t.StatusText(true, true, 0m));
    }

    [Fact]
    public void AutoStopOff_MeansNoLimit()
    {
        var t = new SessionTimer(ticks);
        Assert.False(t.Start(false, 5m));
        Assert.Equal("No session limit", t.StatusText(true, false, 5m));
        Assert.Equal(string.Empty, t.StatusText(false, false, 5m));
    }

    [Fact]
    public void Idle_ShowsConfiguredLimit()
    {
        Assert.Equal("Auto-stop after 0.25 h", new SessionTimer(ticks).StatusText(false, true, 0.25m));
    }

    [Fact]
    public void LongSessions_ShowTotalHours()
    {
        var t = new SessionTimer(ticks);
        t.Start(true, 168m);
        Assert.Equal("Auto-stop in 168:00:00", t.StatusText(true, true, 168m));
    }

    [Fact]
    public void Reset_DisarmsLimit()
    {
        var t = new SessionTimer(ticks);
        t.Start(true, 1m);
        t.Reset();
        Assert.False(t.HasLimit);
        ticks.Advance(TimeSpan.FromHours(2));
        Assert.False(t.IsExpired);
    }
}

public class BuffScheduleTests
{
    private readonly FakeTicks ticks = new();

    [Fact]
    public void FirstCheck_SchedulesOneIntervalOut()
    {
        var b = new BuffSchedule(ticks);
        Assert.False(b.IsDue(true, 10m));
        Assert.Equal(ticks.NowMs + 600_000, b.NextTick);
        ticks.Advance(TimeSpan.FromMinutes(10));
        Assert.True(b.IsDue(true, 10m));
    }

    [Fact]
    public void Disabled_ClearsSchedule()
    {
        var b = new BuffSchedule(ticks);
        b.IsDue(true, 10m);
        Assert.False(b.IsDue(false, 10m));
        Assert.Null(b.NextTick);
        Assert.Equal("Next buff: off", b.StatusText(false, false, true, 10m));
    }

    [Fact]
    public void MarkApplied_SchedulesNext_AndStatusCountsDown()
    {
        var b = new BuffSchedule(ticks);
        b.MarkApplied(10m);
        ticks.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("Next buff: in 9:30 (between catches)", b.StatusText(true, false, true, 10m));
        ticks.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("Next buff: after the current catch", b.StatusText(true, false, true, 10m));
    }

    [Fact]
    public void IdleStatus_DescribesStartBehavior()
    {
        var b = new BuffSchedule(ticks);
        Assert.Equal("Next buff: on Start", b.StatusText(true, true, false, 10m));
        Assert.Equal("Next buff: 10 min after Start", b.StatusText(true, false, false, 10m));
        Assert.Equal("Next buff: after the next catch", b.StatusText(true, false, true, 10m));
    }
}

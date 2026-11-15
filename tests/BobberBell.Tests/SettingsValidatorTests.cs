using System.Collections.Generic;
using BobberBell.Core.Files;
using BobberBell.Core.Settings;

namespace BobberBell.Tests;

public class SettingsValidatorTests
{
    private static SettingsValidationResult Run(AppSettings s) =>
        SettingsValidator.Validate(s, p => SoundFileCheck.Fail("file not found", notFound: true));

    [Fact]
    public void Defaults_AreUnchanged_AndProduceNoNotes()
    {
        var r = Run(new AppSettings());
        Assert.Empty(r.Notes);
        Assert.Equal("1", r.Settings.SelectedKey);
        Assert.Equal("2", r.Settings.BuffKey);
        Assert.Equal("3", r.Settings.RodKey);
        Assert.Equal(0.25m, r.Settings.AutoStopHours);
        Assert.True(r.Settings.AutoStopEnabled);
        Assert.Equal(SettingsLimits.DefaultWindowName, r.Settings.WindowName);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.25, 0.25)]
    [InlineData(12, 12)]
    [InlineData(168, 168)]
    public void SessionHours_InRange_AreKept(decimal saved, decimal expected)
    {
        var notes = new List<string>();
        Assert.Equal(expected, SettingsValidator.ValidateSessionHours(saved, notes));
        Assert.Empty(notes);
    }

    [Theory]
    [InlineData(168.01)]
    [InlineData(500)]
    [InlineData(1e20)]
    public void SessionHours_Above168_AreCappedAt168_WithNote(decimal saved)
    {
        var notes = new List<string>();
        Assert.Equal(168m, SettingsValidator.ValidateSessionHours(saved, notes));
        Assert.Single(notes);
        Assert.Contains("capped at 168 h", notes[0], System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-5)]
    public void SessionHours_Negative_Become025_WithNote(decimal saved)
    {
        var notes = new List<string>();
        Assert.Equal(0.25m, SettingsValidator.ValidateSessionHours(saved, notes));
        Assert.Single(notes);
        Assert.Contains("invalid", notes[0], System.StringComparison.Ordinal);
    }

    [Fact]
    public void NumericValues_AreClampedToControlRanges()
    {
        var r = Run(new AppSettings
        {
            Similarity = 5m,
            MinVolume = -500m,
            CastWaitMin = 0.1m,
            CastWaitMax = 99m,
            BuffIntervalMinutes = 1000m,
            BuffWaitSeconds = 0m,
        }).Settings;
        Assert.Equal(0.95m, r.Similarity);
        Assert.Equal(-80m, r.MinVolume);
        Assert.Equal(0.5m, r.CastWaitMin);
        Assert.Equal(10m, r.CastWaitMax);
        Assert.Equal(240m, r.BuffIntervalMinutes);
        Assert.Equal(0.5m, r.BuffWaitSeconds);
    }

    [Fact]
    public void CastMax_BelowCastMin_IsRaisedToCastMin()
    {
        var r = Run(new AppSettings { CastWaitMin = 4m, CastWaitMax = 2m }).Settings;
        Assert.Equal(4m, r.CastWaitMin);
        Assert.Equal(4m, r.CastWaitMax);
    }

    [Fact]
    public void BuffInterval_IsRoundedToWholeMinutes()
    {
        Assert.Equal(8m, Run(new AppSettings { BuffIntervalMinutes = 7.6m }).Settings.BuffIntervalMinutes);
    }

    [Theory]
    [InlineData("F5", "F5")]
    [InlineData("f5", "F5")]
    [InlineData("mouse 4", "Mouse 4")]
    [InlineData("Delete", "1")]
    [InlineData("", "1")]
    [InlineData(null, "1")]
    [InlineData("1; rm -rf", "1")]
    public void FishingKey_IsValidatedAgainstAllowList(string? saved, string expected)
    {
        Assert.Equal(expected, Run(new AppSettings { SelectedKey = saved! }).Settings.SelectedKey);
    }

    [Fact]
    public void InvalidBuffAndRodKeys_FallBackToDefaults()
    {
        var r = Run(new AppSettings { BuffKey = "NotAKey", RodKey = "F13" }).Settings;
        Assert.Equal("2", r.BuffKey);
        Assert.Equal("3", r.RodKey);
    }

    [Theory]
    [InlineData("WeatherBug", SettingsLimits.DefaultWindowName)]
    [InlineData("WeatherBug - v1.2.3", SettingsLimits.DefaultWindowName)]
    [InlineData("BobberBell - v0.9", SettingsLimits.DefaultWindowName)]
    [InlineData("   ", SettingsLimits.DefaultWindowName)]
    [InlineData("  Notepad  ", "Notepad")]
    [InlineData("Tab\tName", "TabName")]
    public void WindowName_IsNormalized(string saved, string expected)
    {
        Assert.Equal(expected, SettingsValidator.NormalizeWindowName(saved));
    }

    [Fact]
    public void WindowName_IsTruncatedTo128()
    {
        Assert.Equal(128, SettingsValidator.NormalizeWindowName(new string('x', 500)).Length);
    }

    [Fact]
    public void MissingLastSound_IsCleared_WithNote()
    {
        var r = Run(new AppSettings { LastSoundPath = "C:\\missing.wav" });
        Assert.Equal(string.Empty, r.Settings.LastSoundPath);
        Assert.Contains(r.Notes, n => n.StartsWith("Last sound file not found", System.StringComparison.Ordinal));
    }

    [Fact]
    public void RejectedLastSound_IsCleared_WithNote()
    {
        var r = SettingsValidator.Validate(new AppSettings { LastSoundPath = @"\\host\share\a.wav" }, p => SoundFileCheck.Fail("network and device paths are not allowed"));
        Assert.Equal(string.Empty, r.Settings.LastSoundPath);
        Assert.Contains(r.Notes, n => n.Contains("rejected", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("BobberSplash", "BobberSplash")]
    [InlineData("Custom file…", "Custom file…")]
    [InlineData("../../evil", "BobberSplash")]
    public void LibrarySound_MustBeAKnownEntry(string saved, string expected)
    {
        Assert.Equal(expected, Run(new AppSettings { LibrarySound = saved }).Settings.LibrarySound);
    }

    [Fact]
    public void OverlongDeviceName_IsDropped()
    {
        Assert.Equal(string.Empty, Run(new AppSettings { AudioDevice = new string('d', 1000) }).Settings.AudioDevice);
    }
}

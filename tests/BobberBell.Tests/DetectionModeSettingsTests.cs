using System.IO;
using BobberBell.Core.Files;
using BobberBell.Core.Settings;

namespace BobberBell.Tests;

public sealed class DetectionModeSettingsTests : System.IDisposable
{
    private readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    private SettingsStore Store() => new(dir.File("settings.json"), p => SoundFileCheck.Fail("file not found", notFound: true));

    // A v1.0.3 settings.json (no detection-mode keys) loads unchanged, in the default Volume trigger mode with trigger defaults.
    [Fact]
    public void OldSettingsFile_LoadsUnchanged_InVolumeTriggerMode()
    {
        File.WriteAllText(dir.File("settings.json"),
            "{\"SelectedKey\":\"1\",\"Similarity\":0.40,\"MinVolume\":-35,\"WindowName\":\"BobberBell - v1.0.3\",\"LastSoundPath\":\"\"," +
            "\"LibrarySound\":\"BobberSplash\",\"AudioDevice\":\"Headset Earphone (Alienware AW520H)\",\"CastWaitMin\":1.5,\"CastWaitMax\":2.5," +
            "\"AutoStopEnabled\":true,\"AutoStopHours\":0.25,\"BuffEnabled\":false,\"BuffIntervalMinutes\":10,\"BuffKey\":\"2\",\"RodKey\":\"3\"," +
            "\"BuffWaitSeconds\":3,\"BuffOnStart\":false}");
        var r = Store().Load();
        Assert.Equal(SettingsLoadStatus.Loaded, r.Status);
        var s = r.Validation!.Settings;
        Assert.Equal(SettingsLimits.DetectionModeVolumeTrigger, s.DetectionMode);
        Assert.Equal(0.40m, s.Similarity);
        Assert.Equal(-35m, s.MinVolume);
        Assert.Equal("Headset Earphone (Alienware AW520H)", s.AudioDevice);
        Assert.Equal(-38m, s.TriggerLevelDb);
        Assert.Equal(0.6m, s.TriggerMinSeconds);
        Assert.Equal(2.5m, s.TriggerIgnoreSeconds);
        Assert.True(s.TriggerToneFilter);
        Assert.Equal((300m, 3200m), (s.TriggerMinToneHz, s.TriggerMaxToneHz));
        Assert.Empty(r.Validation.Notes);
    }

    [Fact]
    public void VolumeTriggerSettings_RoundTrip()
    {
        var store = Store();
        store.Save(new AppSettings
        {
            DetectionMode = SettingsLimits.DetectionModeVolumeTrigger,
            TriggerLevelDb = -40m,
            TriggerMinSeconds = 0.5m,
            TriggerIgnoreSeconds = 3m,
            TriggerToneFilter = false,
            TriggerMinToneHz = 200m,
            TriggerMaxToneHz = 3500m,
        });
        var s = store.Load().Validation!.Settings;
        Assert.Equal(SettingsLimits.DetectionModeVolumeTrigger, s.DetectionMode);
        Assert.Equal((-40m, 0.5m, 3m, false, 200m, 3500m),
            (s.TriggerLevelDb, s.TriggerMinSeconds, s.TriggerIgnoreSeconds, s.TriggerToneFilter, s.TriggerMinToneHz, s.TriggerMaxToneHz));
    }

    [Theory]
    [InlineData("volumetrigger")]
    [InlineData("Other")]
    [InlineData("")]
    public void UnknownMode_FallsBackToVolumeTrigger(string mode)
    {
        var s = SettingsValidator.Validate(new AppSettings { DetectionMode = mode }, p => SoundFileCheck.Fail("x", notFound: true)).Settings;
        Assert.Equal(SettingsLimits.DetectionModeVolumeTrigger, s.DetectionMode);
    }

    [Fact]
    public void Defaults_AreVolumeTrigger_AndExplicitFingerprintIsKept()
    {
        Assert.Equal(SettingsLimits.DetectionModeVolumeTrigger, new AppSettings().DetectionMode);
        var s = SettingsValidator.Validate(new AppSettings { DetectionMode = SettingsLimits.DetectionModeFingerprint }, p => SoundFileCheck.Fail("x", notFound: true)).Settings;
        Assert.Equal(SettingsLimits.DetectionModeFingerprint, s.DetectionMode);
    }

    [Fact]
    public void TriggerValues_AreClamped()
    {
        var s = SettingsValidator.Validate(new AppSettings
        {
            TriggerLevelDb = -500m,
            TriggerMinSeconds = 50m,
            TriggerIgnoreSeconds = -1m,
            TriggerMinToneHz = 30000m,
            TriggerMaxToneHz = 10m,
        }, p => SoundFileCheck.Fail("x", notFound: true)).Settings;
        Assert.Equal(-80m, s.TriggerLevelDb);
        Assert.Equal(3m, s.TriggerMinSeconds);
        Assert.Equal(0m, s.TriggerIgnoreSeconds);
        Assert.Equal(20000m, s.TriggerMinToneHz);
        Assert.Equal(20000m, s.TriggerMaxToneHz);
    }
}

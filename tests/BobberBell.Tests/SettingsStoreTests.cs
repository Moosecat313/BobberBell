using System.IO;
using BobberBell.Core.Files;
using BobberBell.Core.Settings;

namespace BobberBell.Tests;

public sealed class SettingsStoreTests : System.IDisposable
{
    private readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    private SettingsStore Store() => new(dir.File("settings.json"), p => SoundFileCheck.Fail("file not found", notFound: true));

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = Store();
        store.Save(new AppSettings { SelectedKey = "F5", AutoStopHours = 2m, Similarity = 0.5m });
        store.Save(new AppSettings { SelectedKey = "F6", AutoStopHours = 3m, Similarity = 0.6m }); // replace path
        var r = store.Load();
        Assert.Equal(SettingsLoadStatus.Loaded, r.Status);
        Assert.Equal("F6", r.Validation!.Settings.SelectedKey);
        Assert.Equal(3m, r.Validation.Settings.AutoStopHours);
        Assert.False(File.Exists(store.SettingsPath + ".tmp"));
    }

    [Fact]
    public void MissingFile_ReportsMissing()
    {
        Assert.Equal(SettingsLoadStatus.Missing, Store().Load().Status);
    }

    [Fact]
    public void OversizedFile_IsRejectedBeforeParsing()
    {
        File.WriteAllText(dir.File("settings.json"), "{\"WindowName\":\"" + new string('a', 70_000) + "\"}");
        var r = Store().Load();
        Assert.Equal(SettingsLoadStatus.Failed, r.Status);
        Assert.Contains("too large", r.Error, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Similarity\": \"0.5\"}")]
    [InlineData("{\"Similarity\": 0.5,}")]
    [InlineData("{/* c */ \"Similarity\": 0.5}")]
    [InlineData("{\"Similarity\": 0.5, \"Similarity\": 0.9}")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    public void MalformedOrNonConformingJson_FailsSafely(string json)
    {
        File.WriteAllText(dir.File("settings.json"), json);
        Assert.Equal(SettingsLoadStatus.Failed, Store().Load().Status);
    }

    [Fact]
    public void DeeplyNestedJson_IsRejected()
    {
        string deep = new string('[', 64) + new string(']', 64);
        File.WriteAllText(dir.File("settings.json"), "{\"Extra\":" + deep + "}");
        Assert.Equal(SettingsLoadStatus.Failed, Store().Load().Status);
    }

    [Fact]
    public void UnknownProperties_AreIgnored_ForCompatibility()
    {
        File.WriteAllText(dir.File("settings.json"), "{\"SendInputs\": true, \"SelectedKey\": \"Q\"}");
        var r = Store().Load();
        Assert.Equal(SettingsLoadStatus.Loaded, r.Status);
        Assert.Equal("Q", r.Validation!.Settings.SelectedKey);
    }

    [Fact]
    public void LoadedValues_AreValidated()
    {
        File.WriteAllText(dir.File("settings.json"), "{\"AutoStopHours\": 9999, \"SelectedKey\": \"Nope\", \"Similarity\": 2}");
        var r = Store().Load();
        Assert.Equal(168m, r.Validation!.Settings.AutoStopHours);
        Assert.Equal("1", r.Validation.Settings.SelectedKey);
        Assert.Equal(0.95m, r.Validation.Settings.Similarity);
        Assert.Single(r.Validation.Notes);
    }

    [Fact]
    public void Serialize_DoesNotPersistSendInputs()
    {
        Assert.DoesNotContain("SendInputs", SettingsStore.Serialize(new AppSettings()), System.StringComparison.Ordinal);
    }
}

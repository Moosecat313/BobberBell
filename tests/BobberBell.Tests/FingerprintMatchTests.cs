using System;
using System.IO;
using System.Threading.Tasks;
using BobberBell.Core.Detection;
using BobberBell.Core.Settings;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;

namespace BobberBell.Tests;

// End-to-end check of the fingerprint stage with the bundled splash sounds (managed WAV decoder, runs on any OS).
public class FingerprintMatchTests
{
    private static readonly string SoundsDir = Path.Combine(AppContext.BaseDirectory, "Sounds");

    private static async Task<(InMemoryModelService Model, IAudioService Audio)> IndexAsync(string file)
    {
        var model = new InMemoryModelService();
        var audio = new SoundFingerprintingAudioService();
        var hashes = await FingerprintCommandBuilder.Instance.BuildFingerprintCommand().From(file).UsingServices(audio).Hash();
        await model.InsertAsync(new TrackInfo("ref", "BobberSplash1", "library"), hashes);
        return (model, audio);
    }

    [Fact]
    public async Task ReferenceSound_MatchesItself_AboveDefaultThreshold()
    {
        string file = Path.Combine(SoundsDir, "bobbersplash-1.wav");
        var (model, audio) = await IndexAsync(file);
        var result = await QueryCommandBuilder.Instance.BuildQueryCommand().From(file).UsingServices(model, audio).Query(TestContext.Current.CancellationToken);
        var best = result.BestMatch?.Audio;
        Assert.NotNull(best);
        var gate = new DetectionThresholds((float)SettingsLimits.DefaultSimilarity, (float)SettingsLimits.DefaultMinVolume);
        Assert.True(gate.IsMatch(best.Confidence), $"confidence {best.Confidence:0.00}");
    }

    [Fact]
    public async Task Silence_DoesNotMatch()
    {
        string file = Path.Combine(SoundsDir, "bobbersplash-1.wav");
        var (model, audio) = await IndexAsync(file);
        var silence = new AudioSamples(new float[5512 * 3], "silence", 5512);
        var result = await QueryCommandBuilder.Instance.BuildQueryCommand().From(silence).UsingServices(model, audio).Query(TestContext.Current.CancellationToken);
        var gate = new DetectionThresholds((float)SettingsLimits.DefaultSimilarity, (float)SettingsLimits.DefaultMinVolume);
        Assert.DoesNotContain(result.ResultEntries, e => e.Audio != null && gate.IsMatch(e.Audio.Confidence));
        Assert.False(gate.PassesVolume(DetectionThresholds.PeakDb(silence.Samples)));
    }
}

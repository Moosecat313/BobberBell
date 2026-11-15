using BobberBell.Core.Detection;

namespace BobberBell.Tests;

public class DetectionThresholdsTests
{
    [Fact]
    public void Silence_IsMinus120()
    {
        Assert.Equal(-120f, DetectionThresholds.PeakDb(new float[512]));
        Assert.Equal(-120f, DetectionThresholds.PeakDb(System.ReadOnlySpan<float>.Empty));
    }

    [Theory]
    [InlineData(1.0f, 0f)]
    [InlineData(0.5f, -6.0206f)]
    [InlineData(-0.1f, -20f)]
    public void PeakDb_UsesAbsolutePeak(float peak, float expectedDb)
    {
        var samples = new float[] { 0.01f, peak, -0.02f };
        Assert.Equal(expectedDb, DetectionThresholds.PeakDb(samples), 3);
    }

    [Fact]
    public void VolumeGate_IsInclusive()
    {
        var t = new DetectionThresholds(0.40f, -35f);
        Assert.True(t.PassesVolume(-35f));
        Assert.True(t.PassesVolume(-10f));
        Assert.False(t.PassesVolume(-35.01f));
        Assert.False(t.PassesVolume(-120f));
    }

    [Theory]
    [InlineData(0.41, true)]
    [InlineData(0.95, true)]
    [InlineData(0.399, false)]
    [InlineData(0.0, false)]
    public void SimilarityGate_IsInclusive(double confidence, bool expected)
    {
        Assert.Equal(expected, new DetectionThresholds(0.40f, -35f).IsMatch(confidence));
    }

    [Theory]
    [InlineData(-120f, 0)]
    [InlineData(-80f, 0)]
    [InlineData(-40f, 50)]
    [InlineData(0f, 100)]
    [InlineData(6f, 100)]
    public void VolumeMeter_MapsMinus80To0(float db, int pct)
    {
        Assert.Equal(pct, DetectionThresholds.VolumeToPercent(db));
    }

    [Theory]
    [InlineData(-1f, 0)]
    [InlineData(0.42f, 42)]
    [InlineData(1.5f, 100)]
    public void SimilarityMeter_IsClamped(float sim, int pct)
    {
        Assert.Equal(pct, DetectionThresholds.SimilarityToPercent(sim));
    }
}

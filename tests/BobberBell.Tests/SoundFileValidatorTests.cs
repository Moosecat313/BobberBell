using System.IO;
using BobberBell.Core.Files;

namespace BobberBell.Tests;

public sealed class SoundFileValidatorTests : System.IDisposable
{
    private readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    [Theory]
    [InlineData("a.wav")]
    [InlineData("a.MP3")]
    [InlineData("a.flac")]
    [InlineData("a.ogg")]
    public void AllowedTypes_Pass(string name)
    {
        var r = SoundFileValidator.Validate(dir.Write(name, 100));
        Assert.True(r.IsValid, r.Reason);
        Assert.Equal(Path.GetFullPath(dir.File(name)), r.FullPath);
    }

    [Theory]
    [InlineData("a.exe")]
    [InlineData("a.wav.lnk")]
    [InlineData("a")]
    [InlineData("a.json")]
    public void OtherTypes_AreRejected(string name)
    {
        var r = SoundFileValidator.Validate(dir.Write(name, 100));
        Assert.False(r.IsValid);
        Assert.Equal("unsupported file type", r.Reason);
    }

    [Fact]
    public void Missing_IsReportedAsNotFound()
    {
        var r = SoundFileValidator.Validate(dir.File("nope.wav"));
        Assert.False(r.IsValid);
        Assert.True(r.NotFound);
    }

    [Fact]
    public void EmptyAndOversizedFiles_AreRejected()
    {
        Assert.Equal("file is empty", SoundFileValidator.Validate(dir.Write("e.wav", 0)).Reason);
        string big = dir.File("big.wav");
        using (var fs = File.Create(big)) fs.SetLength(SoundFileValidator.MaxFileBytes + 1);
        Assert.Equal("file is larger than 50 MB", SoundFileValidator.Validate(big).Reason);
    }

    [Theory]
    [InlineData(@"\\attacker\share\splash.wav")]
    [InlineData(@"\\?\C:\x.wav")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData("//host/share/a.wav")]
    public void UncAndDevicePaths_AreRejected(string path)
    {
        var r = SoundFileValidator.Validate(path);
        Assert.False(r.IsValid);
        Assert.Contains("not allowed", r.Reason, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\music\a.wav:evil.exe")]
    [InlineData("a.wav:stream")]
    public void AlternateDataStreams_AreRejected(string path)
    {
        Assert.False(SoundFileValidator.Validate(path).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("bad\0name.wav")]
    public void EmptyOrInvalidCharacters_AreRejected(string? path)
    {
        Assert.False(SoundFileValidator.Validate(path).IsValid);
    }

    [Fact]
    public void Directory_IsRejected()
    {
        Directory.CreateDirectory(dir.File("folder.wav"));
        Assert.False(SoundFileValidator.Validate(dir.File("folder.wav")).IsValid);
    }

    [Fact]
    public void LibraryFiles_MustStayInsideSoundsFolder()
    {
        string sounds = Path.Combine(dir.Path, "Sounds");
        Directory.CreateDirectory(sounds);
        File.WriteAllBytes(Path.Combine(sounds, "bobbersplash-1.wav"), new byte[10]);
        dir.Write("outside.wav", 10);

        Assert.True(SoundFileValidator.ValidateLibraryFile(sounds, "bobbersplash-1.wav").IsValid);
        Assert.False(SoundFileValidator.ValidateLibraryFile(sounds, "../outside.wav").IsValid);
        Assert.False(SoundFileValidator.ValidateLibraryFile(sounds, Path.Combine("..", "outside.wav")).IsValid);
        Assert.False(SoundFileValidator.IsUnderDirectory(Path.Combine(sounds, "..", "outside.wav"), sounds));
        Assert.False(SoundFileValidator.IsUnderDirectory(sounds + "Evil" + Path.DirectorySeparatorChar + "a.wav", sounds));
    }
}

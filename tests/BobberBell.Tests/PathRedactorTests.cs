using System;
using System.IO;
using BobberBell.Core.Logging;

namespace BobberBell.Tests;

public class PathRedactorTests
{
    private static PathRedactor Windows() => new(
        new[]
        {
            (@"C:\Users\alice\AppData\Local", "%LOCALAPPDATA%"),
            (@"C:\Users\alice\AppData\Roaming", "%APPDATA%"),
            (@"C:\Users\alice", "~"),
        },
        "alice");

    [Theory]
    [InlineData(@"Settings saved → C:\Users\alice\AppData\Local\BobberBell\settings.json", @"Settings saved → %LOCALAPPDATA%\BobberBell\settings.json")]
    [InlineData(@"C:\Users\alice\AppData\Roaming\BobberBell\license.dat", @"%APPDATA%\BobberBell\license.dat")]
    [InlineData(@"Loaded C:\Users\alice\Music\a.wav", @"Loaded ~\Music\a.wav")]
    [InlineData(@"c:\users\ALICE\appdata\local\x", @"%LOCALAPPDATA%\x")]
    [InlineData(@"D:\Users\alice\backup\a.wav", @"D:\Users\~\backup\a.wav")]
    [InlineData("no paths here", "no paths here")]
    public void KnownRoots_AreReplaced_LongestFirst(string input, string expected)
    {
        Assert.Equal(expected, Windows().Redact(input));
    }

    [Fact]
    public void ShortUserNames_AreNotReplacedInsideOtherWords()
    {
        var r = new PathRedactor(Array.Empty<(string, string)>(), "al");
        Assert.Equal(@"D:\Users\al\x", r.Redact(@"D:\Users\al\x"));
    }

    [Fact]
    public void Null_BecomesEmpty()
    {
        Assert.Equal(string.Empty, Windows().Redact(null));
    }

    [Fact]
    public void Logger_WritesRedactedLines_AndRaisesEvent()
    {
        using var dir = new TempDir();
        var logger = new FileAppLogger(dir.File("log.txt"), Windows(), () => new DateTime(2026, 11, 14, 9, 30, 5));
        LogEntry? seen = null;
        logger.EntryWritten += (s, e) => seen = e;
        logger.Error(@"Load failed for C:\Users\alice\Music\a.wav", new InvalidOperationException("boom"));
        string line = File.ReadAllText(dir.File("log.txt"));
        Assert.Equal("2026-11-14 09:30:05  Load failed for ~\\Music\\a.wav: boom" + Environment.NewLine, line);
        Assert.DoesNotContain("alice", line, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LogLevel.Error, seen!.Level);
    }

    [Fact]
    public void Logger_RotatesAbove2MB()
    {
        using var dir = new TempDir();
        string path = dir.File("log.txt");
        File.WriteAllBytes(path, new byte[FileAppLogger.RotateBytes + 1]);
        new FileAppLogger(path, Windows()).Info("after rotate");
        Assert.True(File.Exists(dir.File("log.old.txt")));
        Assert.True(new FileInfo(path).Length < 100);
    }
}

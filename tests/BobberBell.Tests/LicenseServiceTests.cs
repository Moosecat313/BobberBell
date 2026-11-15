using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BobberBell.Core.Licensing;

namespace BobberBell.Tests;

// Uses a synthetic code and its hash only. No issued code is used, printed or needed.
public sealed class LicenseServiceTests : IDisposable
{
    private const string SyntheticCode = "TEST-CODE-0000-AAAA-ZZZZ";
    private static readonly string SyntheticHashHex = Convert.ToHexString(LicenseService.DeriveCodeHash(SyntheticCode)!);
    private readonly TempDir dir = new();
    private readonly FakeMachineId machine = new("4C4C4544-0000-1111-2222-333344445555");

    public void Dispose() => dir.Dispose();

    private string LicensePath => dir.File("license.dat");

    private LicenseService Service() => new(LicensePath, machine, new[] { SyntheticHashHex });

    [Theory]
    [InlineData("abcd-efgh-1234-5678", "ABCDEFGH12345678")]
    [InlineData("  a b\tc  ", "ABC")]
    [InlineData(null, "")]
    [InlineData("---", "")]
    public void NormalizeCode_KeepsLettersAndDigitsUpperCased(string? input, string expected)
    {
        Assert.Equal(expected, LicenseService.NormalizeCode(input));
    }

    [Fact]
    public void DeriveCodeHash_IsFormatInsensitive_And32Bytes()
    {
        var a = LicenseService.DeriveCodeHash("test code 0000 aaaa zzzz")!;
        Assert.Equal(32, a.Length);
        Assert.Equal(SyntheticHashHex, Convert.ToHexString(a));
        Assert.Null(LicenseService.DeriveCodeHash("  -- "));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJKLMNO")]          // 15
    [InlineData("ABCD-EFGH-IJKL-MNO")]       // 15 after normalizing
    public void ShortKeys_AreRejected_BeforeHashing(string input)
    {
        Assert.Equal(LicenseCheckResult.TooShort, Service().Activate(input));
        Assert.False(File.Exists(LicensePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEFGHIJKLMNOP")]         // 16, well-formed but unknown
    [InlineData("TEST-CODE-0000-AAAA-ZZZY")]
    public void EmptyOrUnknownKeys_AreInvalid(string input)
    {
        Assert.Equal(LicenseCheckResult.Invalid, Service().Activate(input));
        Assert.False(File.Exists(LicensePath));
    }

    [Fact]
    public void ValidKey_IsSaved_BoundToThisMachine()
    {
        var svc = Service();
        Assert.Equal(LicenseCheckResult.Activated, svc.Activate(SyntheticCode.ToLowerInvariant()));
        string text = File.ReadAllText(LicensePath);
        var parts = text.Split('|');
        Assert.Equal(3, parts.Length);
        Assert.Equal("WB2", parts[0]);
        Assert.Equal(SyntheticHashHex, parts[1]);
        Assert.DoesNotContain("TEST", text, StringComparison.OrdinalIgnoreCase); // code itself never stored

        // Known-answer check of the v1.0 binding: HMAC-SHA256(codeHash, "WeatherBug.License.v2|" + MachineGuid).
        byte[] expected = HMACSHA256.HashData(Convert.FromHexString(SyntheticHashHex), Encoding.UTF8.GetBytes("WeatherBug.License.v2|" + machine.Id));
        Assert.Equal(Convert.ToHexString(expected), parts[2]);
        Assert.True(svc.HasValidStoredLicense());
    }

    [Fact]
    public void StoredLicense_FromAnotherMachine_IsRejected()
    {
        Service().Activate(SyntheticCode);
        machine.Id = "DIFFERENT-MACHINE";
        Assert.False(Service().HasValidStoredLicense());
    }

    [Fact]
    public void StoredLicense_WithTamperedBinding_IsRejected()
    {
        Service().Activate(SyntheticCode);
        var parts = File.ReadAllText(LicensePath).Split('|');
        char last = parts[2][^1];
        parts[2] = parts[2][..^1] + (last == '0' ? '1' : '0');
        File.WriteAllText(LicensePath, string.Join('|', parts));
        Assert.False(Service().HasValidStoredLicense());
    }

    [Theory]
    [InlineData("WB2|zz|zz")]
    [InlineData("WB2|ABC|DEF")]
    [InlineData("WB2||")]
    [InlineData("garbage")]
    [InlineData("")]
    public void MalformedLicenseFiles_AreRejected(string content)
    {
        File.WriteAllText(LicensePath, content);
        Assert.False(Service().HasValidStoredLicense());
    }

    [Fact]
    public void OversizedLicenseFile_IsRejectedWithoutReading()
    {
        File.WriteAllText(LicensePath, new string('A', LicenseService.MaxLicenseFileBytes + 1));
        Assert.False(Service().HasValidStoredLicense());
    }

    [Fact]
    public void NoMachineId_AcceptsForSessionOnly_AndNeverWritesUnboundFile()
    {
        machine.Id = string.Empty;
        Assert.Equal(LicenseCheckResult.SessionOnly, Service().Activate(SyntheticCode));
        Assert.False(File.Exists(LicensePath));
    }

    [Fact]
    public void NoMachineId_DoesNotTrustStoredBoundLicense()
    {
        Service().Activate(SyntheticCode);
        machine.Id = string.Empty;
        Assert.False(Service().HasValidStoredLicense());
    }

    [Fact]
    public void LegacyPlaintextLicense_IsMigratedToBoundFormat()
    {
        File.WriteAllText(LicensePath, SyntheticCode);
        Assert.True(Service().HasValidStoredLicense());
        Assert.StartsWith("WB2|", File.ReadAllText(LicensePath), StringComparison.Ordinal);
        Assert.True(Service().HasValidStoredLicense());
    }

    [Fact]
    public void IsKnownHash_RejectsWrongLengthsAndNull()
    {
        var svc = Service();
        Assert.False(svc.IsKnownHash(null));
        Assert.False(svc.IsKnownHash(new byte[31]));
        Assert.False(svc.IsKnownHash(new byte[33]));
        Assert.False(svc.IsKnownHash(new byte[32]));
        Assert.True(svc.IsKnownHash(Convert.FromHexString(SyntheticHashHex)));
    }

    [Fact]
    public void ProductionAllowList_RejectsSyntheticCode()
    {
        Assert.Equal(LicenseCheckResult.Invalid, new LicenseService(LicensePath, machine).Activate(SyntheticCode));
    }

    [Fact]
    public void BindingLiteral_IsUnchanged()
    {
        // Changing this literal would invalidate every existing license.dat.
        var field = typeof(LicenseService).GetField("LegacyBindingLiteral", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal("WeatherBug.License.v2|", field.GetRawConstantValue());
    }
}

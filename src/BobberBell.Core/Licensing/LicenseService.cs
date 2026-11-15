using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BobberBell.Core.Licensing;

/// <summary>
/// Offline license check. Codes are never stored: only salted PBKDF2-SHA256 hashes are compiled in, every
/// comparison is constant-time, and license.dat holds the code hash plus an HMAC that binds it to this PC.
/// The algorithm, constants and file format are identical to v1.0 so existing license.dat files stay valid.
/// </summary>
public sealed class LicenseService
{
    /// <summary>Issued codes have at least this many letters/digits after normalizing; shorter input is rejected before hashing.</summary>
    public const int MinCodeLength = 16;

    /// <summary>Largest license.dat accepted, in bytes (a valid file is about 135 bytes).</summary>
    public const int MaxLicenseFileBytes = 4096;

    private const string LicensePrefix = "WB2";
    private const int HashBytes = 32;
    private const string LicenseSalt = "3FD9657A00F04F4E3DAC3431AA18AD6F";
    private const int LicenseIterations = 200000;

    // DO NOT CHANGE this literal. It is part of the HMAC input that binds license.dat to a PC, so every friend's
    // existing license.dat was written with it; changing it (even to "BobberBell...") would invalidate them all
    // and force everyone to re-enter their code. If a new label is ever needed: accept both the old and the new
    // binding when checking, and rewrite license.dat with the new label after the next successful check.
    private const string LegacyBindingLiteral = "WeatherBug.License.v2|";

    // License codes are stored only as salted PBKDF2-SHA256 hashes (no plaintext codes in the assembly).
    private static readonly string[] IssuedHashes =
    {
        "8A1DA96D62E1DE28EF6FC99F67C27C109BD31E06C19BFFC1B7FAACA427A7A253",
        "CEE35C6CC2F360802616EC42C1A9941DE66D5C5E81CE6140F2AE82631CDD7354",
        "BD283C38A385D88BBF29DB6BF52055A972631E865D396A60CA1BE04CA4D11C25",
        "4C0B7465F73412CCAFA887580802B6E9D2F46CA959BEDF1433B6C3839050367F",
        "80F5D7F4D3CC533E1D62CDE0FE6BF8B7252033DAA1373156FFF49C41157F756C",
        "36CA17B6A78217A70CCD396F731F4C3D01B415AA9808561D65A84D022738CFBB",
        "055FFE46AA40588B6AE4A9CDEC851E8468A4139704A0BC111E4D367123B604C1",
        "7EC8936F5A6700C4FD35812948901E4B0A65CF8A9944F253D528E29151195340",
        "A610BD83FF5BAA35DD4F3F91F17F57104AA2AABF790FB1AD88B999D3CA482D5D",
        "9298B5B64339C4DC5361C33D97E40E145DD4F17F8EC9A8307721634398DCCBD6",
        "E5075A7AD66AAFD1A952870988B3A22E09613D95BBD63B153E75AB9FBDCAB4BD"
    };

    private readonly byte[][] knownHashes;
    private readonly IMachineIdProvider machineIdProvider;

    /// <summary>Creates a service using the issued hashes compiled into the app.</summary>
    /// <param name="licensePath">Full path of license.dat.</param>
    /// <param name="machineIdProvider">Source of the PC binding id.</param>
    public LicenseService(string licensePath, IMachineIdProvider machineIdProvider)
        : this(licensePath, machineIdProvider, IssuedHashes)
    {
    }

    /// <summary>Creates a service with an explicit hash allow-list (used by tests with synthetic codes).</summary>
    /// <param name="licensePath">Full path of license.dat.</param>
    /// <param name="machineIdProvider">Source of the PC binding id.</param>
    /// <param name="knownHashHex">Allowed code hashes as 64-character hex strings.</param>
    public LicenseService(string licensePath, IMachineIdProvider machineIdProvider, IEnumerable<string> knownHashHex)
    {
        ArgumentException.ThrowIfNullOrEmpty(licensePath);
        ArgumentNullException.ThrowIfNull(knownHashHex);
        LicensePath = licensePath;
        this.machineIdProvider = machineIdProvider ?? throw new ArgumentNullException(nameof(machineIdProvider));
        knownHashes = knownHashHex.Select(Convert.FromHexString).ToArray();
    }

    /// <summary>Gets the full path of license.dat.</summary>
    public string LicensePath { get; }

    /// <summary>Strips everything except letters and digits and upper-cases the rest.</summary>
    /// <param name="code">Raw user input.</param>
    /// <returns>The normalized code (possibly empty).</returns>
    public static string NormalizeCode(string? code) =>
        new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>Derives the PBKDF2-SHA256 hash of a code (200,000 iterations, 32 bytes).</summary>
    /// <param name="code">Raw or normalized code.</param>
    /// <returns>The hash, or <c>null</c> when the normalized code is empty.</returns>
    public static byte[]? DeriveCodeHash(string? code)
    {
        string n = NormalizeCode(code);
        if (n.Length == 0) return null;
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(n), Convert.FromHexString(LicenseSalt),
            LicenseIterations, HashAlgorithmName.SHA256, HashBytes);
    }

    /// <summary>
    /// Constant-time membership test: every known hash is compared with
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> and the results are OR-ed, so timing does not reveal which one matched.
    /// </summary>
    /// <param name="hash">Candidate code hash.</param>
    /// <returns><c>true</c> if the hash is issued.</returns>
    public bool IsKnownHash(byte[]? hash)
    {
        if (hash == null || hash.Length != HashBytes) return false;
        bool ok = false;
        foreach (var known in knownHashes)
            ok |= CryptographicOperations.FixedTimeEquals(hash, known);
        return ok;
    }

    /// <summary>
    /// Returns whether license.dat holds a valid license bound to this PC. A valid v1.0 plaintext file is migrated
    /// to the bound format in place. Any error (missing, oversized, malformed, wrong PC) returns <c>false</c>.
    /// </summary>
    /// <returns><c>true</c> if no key prompt is needed.</returns>
    public bool HasValidStoredLicense()
    {
        try
        {
            var fi = new FileInfo(LicensePath);
            if (!fi.Exists || fi.Length > MaxLicenseFileBytes) return false;
            string text = File.ReadAllText(LicensePath).Trim();
            var parts = text.Split('|');
            if (parts.Length == 3 && parts[0] == LicensePrefix)
            {
                try
                {
                    byte[] codeHash = Convert.FromHexString(parts[1]);
                    byte[] binding = Convert.FromHexString(parts[2]);
                    string machineId = machineIdProvider.GetMachineId();
                    if (machineId.Length == 0) return false; // can't verify the binding: ask for the code instead
                    return IsKnownHash(codeHash) && CryptographicOperations.FixedTimeEquals(binding, Binding(codeHash, machineId));
                }
                catch (FormatException)
                {
                    return false;
                }
            }

            byte[]? legacy = DeriveCodeHash(text);
            if (!IsKnownHash(legacy)) return false;
            TrySave(legacy!);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Checks a key the user typed and, if valid, saves it bound to this PC.</summary>
    /// <param name="input">Raw user input.</param>
    /// <returns>The outcome; see <see cref="LicenseCheckResult"/>.</returns>
    public LicenseCheckResult Activate(string? input)
    {
        int len = NormalizeCode(input).Length;
        if (len > 0 && len < MinCodeLength) return LicenseCheckResult.TooShort;
        byte[]? hash = DeriveCodeHash(input);
        if (!IsKnownHash(hash)) return LicenseCheckResult.Invalid;
        return TrySave(hash!) ? LicenseCheckResult.Activated : LicenseCheckResult.SessionOnly;
    }

    // Binds a license to this PC: HMAC-SHA256(code hash, literal + MachineGuid).
    private static byte[] Binding(byte[] codeHash, string machineId) =>
        HMACSHA256.HashData(codeHash, Encoding.UTF8.GetBytes(LegacyBindingLiteral + machineId));

    // Returns false only when the license can't be bound (no machine id): never save an unbound license,
    // since it would work on any PC. I/O errors are ignored, as in v1.0 (the key is asked for again next launch).
    private bool TrySave(byte[] codeHash)
    {
        string machineId = machineIdProvider.GetMachineId();
        if (machineId.Length == 0) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
            string text = $"{LicensePrefix}|{Convert.ToHexString(codeHash)}|{Convert.ToHexString(Binding(codeHash, machineId))}";
            string tmp = LicensePath + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, LicensePath, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return true;
    }
}

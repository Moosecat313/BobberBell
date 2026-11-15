using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BobberBell.Core.Session;

namespace BobberBell.Core.Licensing;

/// <summary>
/// A per-PC trial of <see cref="TrialSeconds"/> seconds of total running time. Used seconds are stored in two places
/// (the trial file and a registry mirror) as <c>BT1|seconds|HMAC</c>, signed with a key derived from this PC's
/// MachineGuid. On load the larger valid value wins, so deleting one copy does not reset the trial, and used time
/// never decreases. A record that fails its check (edited, or copied from another PC) ends the trial.
/// </summary>
/// <remarks>
/// This is a client-side trial: someone who removes both copies, or patches the binary, can reset it. That is an
/// accepted limit (see docs/THREAT_MODEL.md); it keeps honest users honest without any server.
/// If the MachineGuid cannot be read there is no trial (a key is required), because an unbound trial could be reset
/// simply by deleting files.
/// </remarks>
public sealed class TrialService
{
    /// <summary>Total trial length per PC, in seconds (5 minutes).</summary>
    public const int TrialSeconds = 300;

    /// <summary>How often running time is saved while the trial is counting, in seconds.</summary>
    public const int PersistIntervalSeconds = 15;

    private const string Prefix = "BT1";

    // Key-derivation label for trial records. Deliberately different from the license binding literal.
    private static readonly byte[] TrialKeyLabel = Encoding.UTF8.GetBytes("BobberBell.Trial.v1/key-derivation");

    private readonly IMachineIdProvider machineIdProvider;
    private readonly ITrialRecordStore file;
    private readonly ITrialRecordStore mirror;
    private readonly ITickSource ticks;
    private byte[]? key;
    private long storedSeconds;      // largest value known to be stored (never decreases)
    private long? countingSinceMs;   // tick when counting started, or null when not counting
    private long countedBaseSeconds; // used seconds when counting started
    private long lastPersistMs;

    /// <summary>Creates the service.</summary>
    /// <param name="machineIdProvider">Source of the MachineGuid the records are bound to.</param>
    /// <param name="file">Primary store (trial.dat).</param>
    /// <param name="mirror">Mirror store (registry).</param>
    /// <param name="ticks">Monotonic clock.</param>
    public TrialService(IMachineIdProvider machineIdProvider, ITrialRecordStore file, ITrialRecordStore mirror, ITickSource ticks)
    {
        this.machineIdProvider = machineIdProvider ?? throw new ArgumentNullException(nameof(machineIdProvider));
        this.file = file ?? throw new ArgumentNullException(nameof(file));
        this.mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        this.ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
    }

    /// <summary>Gets the status found by the last <see cref="Load"/>.</summary>
    public TrialStatus Status { get; private set; } = TrialStatus.NoMachineId;

    /// <summary>Gets a value indicating whether the trial can be started or continued.</summary>
    public bool IsAvailable => Status is TrialStatus.Fresh or TrialStatus.InProgress;

    /// <summary>Gets a value indicating whether trial time is currently being counted.</summary>
    public bool IsCounting => countingSinceMs.HasValue;

    /// <summary>Gets the used trial seconds, including time counted since <see cref="Start"/>.</summary>
    public long UsedSeconds
    {
        get
        {
            if (countingSinceMs is not long since) return storedSeconds;
            long elapsedMs = Math.Max(0, ticks.NowMs - since);
            long counted = countedBaseSeconds + (long)Math.Ceiling(elapsedMs / 1000.0);
            return Math.Min(TrialSeconds, Math.Max(storedSeconds, counted));
        }
    }

    /// <summary>Gets the remaining trial seconds (0 when exhausted or unavailable).</summary>
    public long RemainingSeconds => Status is TrialStatus.Tampered or TrialStatus.NoMachineId ? 0 : Math.Max(0, TrialSeconds - UsedSeconds);

    /// <summary>Gets a value indicating whether a running trial has used all its time.</summary>
    public bool IsExpired => RemainingSeconds <= 0;

    /// <summary>Formats seconds as <c>M:SS</c>.</summary>
    /// <param name="seconds">Seconds.</param>
    /// <returns>Formatted time.</returns>
    public static string FormatRemaining(long seconds)
    {
        seconds = Math.Max(0, seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>Reads both stores and decides whether a trial is available.</summary>
    /// <returns>The resulting status.</returns>
    public TrialStatus Load()
    {
        countingSinceMs = null;
        string machineId = machineIdProvider.GetMachineId();
        if (string.IsNullOrEmpty(machineId))
        {
            key = null;
            storedSeconds = TrialSeconds;
            return Status = TrialStatus.NoMachineId;
        }

        key = DeriveKey(machineId);
        var a = ReadRecord(file);
        var b = ReadRecord(mirror);
        if (a.Invalid || b.Invalid)
        {
            storedSeconds = TrialSeconds;
            return Status = TrialStatus.Tampered;
        }

        storedSeconds = Math.Max(a.Seconds ?? 0, b.Seconds ?? 0);
        if (storedSeconds >= TrialSeconds) return Status = TrialStatus.Exhausted;
        return Status = a.Seconds is null && b.Seconds is null ? TrialStatus.Fresh : TrialStatus.InProgress;
    }

    /// <summary>Starts counting running time against the trial and saves the current value to both stores.</summary>
    /// <exception cref="InvalidOperationException">No trial is available.</exception>
    public void Start()
    {
        if (!IsAvailable) throw new InvalidOperationException("No trial time is available.");
        countedBaseSeconds = storedSeconds;
        countingSinceMs = ticks.NowMs;
        Persist();
    }

    /// <summary>Saves the used time if <see cref="PersistIntervalSeconds"/> have passed since the last save.</summary>
    /// <returns><c>true</c> if a save was attempted.</returns>
    public bool PersistIfDue()
    {
        if (!IsCounting || ticks.NowMs - lastPersistMs < PersistIntervalSeconds * 1000L) return false;
        Persist();
        return true;
    }

    /// <summary>Saves the used time to both stores (never lower than what is already stored).</summary>
    /// <returns><c>true</c> if both stores were written.</returns>
    public bool Persist()
    {
        if (key == null) return false;
        long used = UsedSeconds;
        storedSeconds = Math.Max(storedSeconds, used);
        lastPersistMs = ticks.NowMs;
        if (storedSeconds >= TrialSeconds && Status is TrialStatus.Fresh or TrialStatus.InProgress) Status = TrialStatus.Exhausted;
        else if (Status == TrialStatus.Fresh && storedSeconds > 0) Status = TrialStatus.InProgress;
        string record = CreateRecord(key, storedSeconds);
        bool ok = TryWrite(file, record);
        return TryWrite(mirror, record) && ok;
    }

    /// <summary>Stops counting and saves the final used time.</summary>
    public void Stop()
    {
        if (!IsCounting) return;
        Persist();
        countingSinceMs = null;
    }

    /// <summary>Builds a signed record (exposed for tests).</summary>
    /// <param name="machineId">MachineGuid the record is bound to.</param>
    /// <param name="usedSeconds">Used seconds.</param>
    /// <returns>The record text.</returns>
    public static string CreateRecord(string machineId, long usedSeconds) => CreateRecord(DeriveKey(machineId), usedSeconds);

    private static string CreateRecord(byte[] key, long usedSeconds)
    {
        string body = string.Create(CultureInfo.InvariantCulture, $"{Prefix}|{usedSeconds}");
        return body + "|" + Convert.ToHexString(Mac(key, body));
    }

    private static byte[] DeriveKey(string machineId) =>
        HMACSHA256.HashData(TrialKeyLabel, Encoding.UTF8.GetBytes(machineId.Trim().ToUpperInvariant()));

    private static byte[] Mac(byte[] key, string body) => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(body));

    private (long? Seconds, bool Invalid) ReadRecord(ITrialRecordStore store)
    {
        string? text;
        try
        {
            text = store.Read();
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (null, true); // present but unreadable: no trial rather than a free reset
        }

        if (text == null) return (null, false);
        return TryParse(text.Trim(), out long seconds) ? (seconds, false) : (null, true);
    }

    private bool TryParse(string text, out long seconds)
    {
        seconds = 0;
        if (key == null || text.Length > 200) return false;
        string[] parts = text.Split('|');
        if (parts.Length != 3 || parts[0] != Prefix) return false;
        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value > TrialSeconds) return false;
        byte[] given;
        try { given = Convert.FromHexString(parts[2]); }
        catch (FormatException) { return false; }
        byte[] expected = Mac(key, parts[0] + "|" + parts[1]);
        if (!CryptographicOperations.FixedTimeEquals(given, expected)) return false;
        seconds = value;
        return true;
    }

    private static bool TryWrite(ITrialRecordStore store, string record)
    {
        try
        {
            store.Write(record);
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}

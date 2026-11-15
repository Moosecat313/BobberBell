using System;
using System.IO;
using BobberBell.Core.Licensing;

namespace BobberBell.Tests;

public class TrialServiceTests
{
    private const string MachineA = "11111111-2222-3333-4444-555555555555";
    private const string MachineB = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";

    private sealed class MemoryStore : ITrialRecordStore
    {
        public string? Value { get; set; }

        public bool FailWrites { get; set; }

        public bool FailReads { get; set; }

        public string? Read() => FailReads ? throw new IOException("locked") : Value;

        public void Write(string record)
        {
            if (FailWrites) throw new UnauthorizedAccessException("denied");
            Value = record;
        }
    }

    private sealed class Rig
    {
        public FakeTicks Ticks { get; } = new();

        public FakeMachineId Machine { get; } = new(MachineA);

        public MemoryStore File { get; } = new();

        public MemoryStore Mirror { get; } = new();

        public TrialService NewService() => new(Machine, File, Mirror, Ticks);
    }

    [Fact]
    public void FreshPc_Gets300Seconds()
    {
        var rig = new Rig();
        var trial = rig.NewService();
        Assert.Equal(TrialStatus.Fresh, trial.Load());
        Assert.True(trial.IsAvailable);
        Assert.Equal(300, trial.RemainingSeconds);
        Assert.Equal("5:00", TrialService.FormatRemaining(trial.RemainingSeconds));
    }

    [Fact]
    public void RunningTime_Accumulates_AcrossLaunches()
    {
        var rig = new Rig();
        var first = rig.NewService();
        first.Load();
        first.Start();
        rig.Ticks.Advance(TimeSpan.FromSeconds(100));
        Assert.Equal(200, first.RemainingSeconds);
        first.Stop();

        rig.Ticks.Advance(TimeSpan.FromHours(5)); // time while the app is closed does not count
        var second = rig.NewService();
        Assert.Equal(TrialStatus.InProgress, second.Load());
        Assert.Equal(100, second.UsedSeconds);
        second.Start();
        rig.Ticks.Advance(TimeSpan.FromSeconds(50));
        second.Stop();
        Assert.Equal(150, rig.NewService().LoadAndGetUsed());
    }

    [Fact]
    public void PartialSeconds_RoundUp()
    {
        var rig = new Rig();
        var trial = rig.NewService();
        trial.Load();
        trial.Start();
        rig.Ticks.NowMs += 1500;
        Assert.Equal(2, trial.UsedSeconds);
    }

    [Fact]
    public void Exhausted_BlocksTrial_AndExpiresWhileRunning()
    {
        var rig = new Rig();
        var trial = rig.NewService();
        trial.Load();
        trial.Start();
        rig.Ticks.Advance(TimeSpan.FromSeconds(299));
        Assert.False(trial.IsExpired);
        rig.Ticks.Advance(TimeSpan.FromSeconds(2));
        Assert.True(trial.IsExpired);
        Assert.Equal(0, trial.RemainingSeconds);
        trial.Stop();

        var next = rig.NewService();
        Assert.Equal(TrialStatus.Exhausted, next.Load());
        Assert.False(next.IsAvailable);
        Assert.Throws<InvalidOperationException>(next.Start);
    }

    [Fact]
    public void PersistIfDue_SavesEvery15Seconds()
    {
        var rig = new Rig();
        var trial = rig.NewService();
        trial.Load();
        trial.Start();
        rig.Ticks.Advance(TimeSpan.FromSeconds(10));
        Assert.False(trial.PersistIfDue());
        Assert.Equal(0, rig.NewService().LoadAndGetUsed());
        rig.Ticks.Advance(TimeSpan.FromSeconds(6));
        Assert.True(trial.PersistIfDue());
        Assert.Equal(16, rig.NewService().LoadAndGetUsed()); // a crash now loses at most 15 s
    }

    [Theory]
    [InlineData("BT1|10|00")]
    [InlineData("BT1|10")]
    [InlineData("garbage")]
    [InlineData("BT1|-5|AA")]
    [InlineData("BT2|10|AA")]
    public void TamperedFile_IsRejected_AsNoTrial(string record)
    {
        var rig = new Rig();
        rig.File.Value = record;
        var trial = rig.NewService();
        Assert.Equal(TrialStatus.Tampered, trial.Load());
        Assert.False(trial.IsAvailable);
        Assert.Equal(0, trial.RemainingSeconds);
    }

    [Fact]
    public void EditedSeconds_BreakTheSignature()
    {
        var rig = new Rig();
        string genuine = TrialService.CreateRecord(MachineA, 250);
        rig.File.Value = genuine.Replace("|250|", "|010|", StringComparison.Ordinal);
        rig.Mirror.Value = genuine;
        Assert.Equal(TrialStatus.Tampered, rig.NewService().Load());
    }

    [Fact]
    public void RecordFromAnotherMachine_IsRejected()
    {
        var rig = new Rig();
        rig.File.Value = TrialService.CreateRecord(MachineB, 0);
        Assert.Equal(TrialStatus.Tampered, rig.NewService().Load());
    }

    [Fact]
    public void DeletedFile_MirrorKeepsUsedTime()
    {
        var rig = new Rig();
        var trial = rig.NewService();
        trial.Load();
        trial.Start();
        rig.Ticks.Advance(TimeSpan.FromSeconds(120));
        trial.Stop();

        rig.File.Value = null; // user deletes trial.dat
        var next = rig.NewService();
        Assert.Equal(TrialStatus.InProgress, next.Load());
        Assert.Equal(120, next.UsedSeconds);
    }

    [Fact]
    public void LargerOfFileAndMirror_Wins_AndBothAreRewritten()
    {
        var rig = new Rig();
        rig.File.Value = TrialService.CreateRecord(MachineA, 50);
        rig.Mirror.Value = TrialService.CreateRecord(MachineA, 120);
        var trial = rig.NewService();
        trial.Load();
        Assert.Equal(120, trial.UsedSeconds);
        trial.Start();
        Assert.Equal(TrialService.CreateRecord(MachineA, 120), rig.File.Value);
        Assert.Equal(rig.File.Value, rig.Mirror.Value);
    }

    [Fact]
    public void UsedTime_NeverDecreases()
    {
        var rig = new Rig();
        var trial = rig.NewService();
        trial.Load();
        trial.Start();
        rig.Ticks.Advance(TimeSpan.FromSeconds(30));
        trial.Persist();
        rig.Ticks.NowMs -= 20_000; // a clock going backwards cannot give time back
        trial.Persist();
        Assert.Equal(30, rig.NewService().LoadAndGetUsed());
    }

    [Fact]
    public void UsedTime_IsCappedAtTrialLength()
    {
        var rig = new Rig();
        var trial = rig.NewService();
        trial.Load();
        trial.Start();
        rig.Ticks.Advance(TimeSpan.FromHours(3));
        trial.Stop();
        Assert.Equal(TrialService.CreateRecord(MachineA, 300), rig.File.Value);
        Assert.Equal(TrialStatus.Exhausted, rig.NewService().Load());
    }

    [Fact]
    public void MissingMachineGuid_MeansNoTrial_AndNothingIsWritten()
    {
        var rig = new Rig();
        rig.Machine.Id = string.Empty;
        var trial = rig.NewService();
        Assert.Equal(TrialStatus.NoMachineId, trial.Load());
        Assert.False(trial.IsAvailable);
        Assert.Equal(0, trial.RemainingSeconds);
        Assert.False(trial.Persist());
        Assert.Null(rig.File.Value);
        Assert.Null(rig.Mirror.Value);
    }

    [Fact]
    public void UnreadableStore_MeansNoTrial()
    {
        var rig = new Rig();
        rig.Mirror.FailReads = true;
        Assert.Equal(TrialStatus.Tampered, rig.NewService().Load());
    }

    [Fact]
    public void WriteFailures_DoNotThrow_AndOtherCopyIsStillWritten()
    {
        var rig = new Rig();
        rig.Mirror.FailWrites = true;
        var trial = rig.NewService();
        trial.Load();
        trial.Start();
        rig.Ticks.Advance(TimeSpan.FromSeconds(20));
        Assert.False(trial.Persist());
        Assert.Equal(TrialService.CreateRecord(MachineA, 20), rig.File.Value);
    }

    [Fact]
    public void Records_AreBoundToMachine_AndDifferPerMachine()
    {
        Assert.NotEqual(TrialService.CreateRecord(MachineA, 10), TrialService.CreateRecord(MachineB, 10));
        Assert.Equal(TrialService.CreateRecord(MachineA, 10), TrialService.CreateRecord(MachineA.ToLowerInvariant(), 10));
        Assert.StartsWith("BT1|10|", TrialService.CreateRecord(MachineA, 10), StringComparison.Ordinal);
    }

    [Fact]
    public void FileStore_RoundTrips_AndRejectsOversizedFiles()
    {
        using var dir = new TempDir();
        var store = new FileTrialRecordStore(Path.Combine(dir.Path, "sub", "trial.dat"));
        Assert.Null(store.Read());
        store.Write("BT1|1|AB");
        Assert.Equal("BT1|1|AB", store.Read());
        Assert.False(File.Exists(Path.Combine(dir.Path, "sub", "trial.dat.tmp")));

        string big = dir.Write("big.dat", FileTrialRecordStore.MaxFileBytes + 1);
        Assert.Throws<InvalidDataException>(() => new FileTrialRecordStore(big).Read());
        var rig = new Rig();
        Assert.Equal(TrialStatus.Tampered, new TrialService(rig.Machine, new FileTrialRecordStore(big), rig.Mirror, rig.Ticks).Load());
    }
}

internal static class TrialServiceTestExtensions
{
    public static long LoadAndGetUsed(this TrialService trial)
    {
        trial.Load();
        return trial.UsedSeconds;
    }
}

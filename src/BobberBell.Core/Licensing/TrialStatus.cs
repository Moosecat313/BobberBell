namespace BobberBell.Core.Licensing;

/// <summary>Why the trial is or is not available.</summary>
public enum TrialStatus
{
    /// <summary>No trial time used yet on this PC.</summary>
    Fresh,

    /// <summary>Some trial time is left.</summary>
    InProgress,

    /// <summary>All trial time has been used.</summary>
    Exhausted,

    /// <summary>A stored record failed its signature or format check (edited, or copied from another PC); no trial.</summary>
    Tampered,

    /// <summary>This PC's MachineGuid could not be read, so a trial cannot be bound to it; a key is required.</summary>
    NoMachineId,
}

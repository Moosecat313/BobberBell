namespace BobberBell.Core.Licensing;

/// <summary>One place a signed trial record is kept (the trial file, or its registry mirror).</summary>
public interface ITrialRecordStore
{
    /// <summary>Reads the stored record.</summary>
    /// <returns>The record text, or <c>null</c> if nothing is stored.</returns>
    /// <exception cref="System.IO.IOException">The store exists but could not be read.</exception>
    string? Read();

    /// <summary>Replaces the stored record.</summary>
    /// <param name="record">Record text.</param>
    void Write(string record);
}

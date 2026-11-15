using System;
using System.IO;
using System.Text;

namespace BobberBell.Core.Licensing;

/// <summary>Keeps the trial record in a small text file (written atomically via a temporary file).</summary>
public sealed class FileTrialRecordStore : ITrialRecordStore
{
    /// <summary>Largest trial file that is read; anything bigger is treated as tampered.</summary>
    public const int MaxFileBytes = 1024;

    private readonly string path;

    /// <summary>Creates the store.</summary>
    /// <param name="path">Full path of the trial file.</param>
    public FileTrialRecordStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        this.path = path;
    }

    /// <inheritdoc />
    public string? Read()
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;
        if (info.Length > MaxFileBytes) throw new InvalidDataException("Trial file is too large.");
        return File.ReadAllText(path, Encoding.ASCII);
    }

    /// <inheritdoc />
    public void Write(string record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, record, Encoding.ASCII);
        File.Move(tmp, path, overwrite: true);
    }
}

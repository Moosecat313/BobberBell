using System;
using BobberBell.Core.Licensing;
using Microsoft.Win32;

namespace BobberBell.Services;

/// <summary>Registry mirror of the trial record: <c>HKCU\Software\BobberBell</c>, value <c>Trial</c>.</summary>
internal sealed class RegistryTrialRecordStore : ITrialRecordStore
{
    private const string KeyPath = @"Software\BobberBell";
    private const string ValueName = "Trial";

    /// <inheritdoc />
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        object? value = key?.GetValue(ValueName);
        return value switch
        {
            null => null,
            string s => s,
            _ => throw new System.IO.InvalidDataException("Trial mirror has an unexpected type."),
        };
    }

    /// <inheritdoc />
    public void Write(string record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, record, RegistryValueKind.String);
    }
}

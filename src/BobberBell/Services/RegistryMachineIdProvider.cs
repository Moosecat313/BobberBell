using System;
using System.Security;
using BobberBell.Core.Licensing;
using Microsoft.Win32;

namespace BobberBell.Services;

/// <summary>Reads the Windows MachineGuid (64-bit registry view) used to bind license.dat to this PC.</summary>
internal sealed class RegistryMachineIdProvider : IMachineIdProvider
{
    /// <inheritdoc />
    public string GetMachineId()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return (key?.GetValue("MachineGuid") as string ?? string.Empty).Trim().ToUpperInvariant();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return string.Empty;
        }
    }
}

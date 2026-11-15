namespace BobberBell.Core.Licensing;

/// <summary>Supplies the stable per-PC identifier a license is bound to.</summary>
public interface IMachineIdProvider
{
    /// <summary>Returns the machine id (trimmed, upper-case), or an empty string if it cannot be read.</summary>
    /// <returns>The machine id or <see cref="string.Empty"/>.</returns>
    string GetMachineId();
}

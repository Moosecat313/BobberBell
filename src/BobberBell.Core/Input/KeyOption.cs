namespace BobberBell.Core.Input;

/// <summary>A key the user can bind, with the Win32 virtual-key code that is sent for it.</summary>
/// <param name="Name">Display and settings name, for example <c>"1"</c>, <c>"F5"</c> or <c>"Mouse 4"</c>.</param>
/// <param name="VirtualKey">Win32 virtual-key code (<c>VK_*</c>).</param>
public sealed record KeyOption(string Name, ushort VirtualKey)
{
    /// <summary>Returns <see cref="Name"/> (used by combo boxes).</summary>
    /// <returns>The display name.</returns>
    public override string ToString() => Name;
}

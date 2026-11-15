using System;
using System.Collections.Generic;
using System.Linq;

namespace BobberBell.Core.Input;

/// <summary>
/// The single allow-list of bindable keys shared by the fishing, buff and rod key pickers.
/// Settings are validated against it, so a hand-edited settings file can never bind an arbitrary key.
/// </summary>
public static class KeyCatalog
{
    /// <summary>Default fishing key.</summary>
    public const string DefaultFishingKey = "1";

    /// <summary>Default buff key.</summary>
    public const string DefaultBuffKey = "2";

    /// <summary>Default rod key.</summary>
    public const string DefaultRodKey = "3";

    /// <summary>Win32 <c>VK_XBUTTON1</c> (mouse button 4).</summary>
    public const ushort VkXButton1 = 0x05;

    /// <summary>Win32 <c>VK_XBUTTON2</c> (mouse button 5).</summary>
    public const ushort VkXButton2 = 0x06;

    /// <summary>Gets all bindable keys in picker order.</summary>
    public static IReadOnlyList<KeyOption> All { get; } = Build();

    /// <summary>Finds a key by name (case-insensitive).</summary>
    /// <param name="name">Key name from the UI or a settings file.</param>
    /// <returns>The key, or <c>null</c> if the name is not on the allow-list.</returns>
    public static KeyOption? Find(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : All.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns whether <paramref name="name"/> is a bindable key.</summary>
    /// <param name="name">Key name to check.</param>
    /// <returns><c>true</c> if the key is on the allow-list.</returns>
    public static bool IsValid(string? name) => Find(name) != null;

    private static KeyOption[] Build()
    {
        var list = new List<KeyOption>
        {
            new("Space", 0x20),
            new("Enter", 0x0D),
            new("Escape", 0x1B),
            new("Tab", 0x09),
            new("Mouse 4", VkXButton1),
            new("Mouse 5", VkXButton2),
        };
        for (int i = 1; i <= 9; i++) list.Add(new(i.ToString(System.Globalization.CultureInfo.InvariantCulture), (ushort)(0x30 + i)));
        list.Add(new("0", 0x30));
        list.Add(new("-", 0xBD));   // VK_OEM_MINUS
        list.Add(new("=", 0xBB));   // VK_OEM_PLUS
        for (char c = 'A'; c <= 'Z'; c++) list.Add(new(c.ToString(), (ushort)c));
        for (int i = 1; i <= 12; i++) list.Add(new("F" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), (ushort)(0x70 + i - 1)));
        list.Add(new("Left Ctrl", 0xA2));
        list.Add(new("Left Shift", 0xA0));
        list.Add(new("Up", 0x26));
        list.Add(new("Down", 0x28));
        list.Add(new("Left", 0x25));
        list.Add(new("Right", 0x27));
        return list.ToArray();
    }
}

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BobberBell.Core.Input;
using BobberBell.Core.Logging;
using BobberBell.Interop;
using WindowsInput;

namespace BobberBell.Services;

/// <summary>
/// Sends keys with <c>SendInput</c> using hardware scan codes (key down, a random 40–90 ms hold, key up) and
/// Mouse 4/5 as XBUTTON clicks. Falls back to a virtual-key press if the scan-code send fails.
/// </summary>
internal sealed class WindowsInputSender : IInputSender
{
    private readonly InputSimulator inputSim = new();
    private readonly IAppLogger logger;

    /// <summary>Creates the sender.</summary>
    /// <param name="logger">Logger for fallback notices.</param>
    public WindowsInputSender(IAppLogger logger) => this.logger = logger;

    /// <inheritdoc />
    public async Task PressAsync(KeyOption key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var vk = (VirtualKeyCode)key.VirtualKey;
        if (key.VirtualKey == KeyCatalog.VkXButton1) { inputSim.Mouse.XButtonClick(1); return; }
        if (key.VirtualKey == KeyCatalog.VkXButton2) { inputSim.Mouse.XButtonClick(2); return; }
        if (!SendScanKey(vk, false))
        {
            logger.Warn($"Scan-code send failed for {vk}; used virtual-key fallback");
            inputSim.Keyboard.KeyPress(vk);
            return;
        }

        try { await Task.Delay(40 + Jitter.Next(51)).ConfigureAwait(false); }
        finally { SendScanKey(vk, true); } // always released, even if the session stops mid-hold
    }

    private static bool IsExtendedKey(VirtualKeyCode k) => k switch
    {
        VirtualKeyCode.UP or VirtualKeyCode.DOWN or VirtualKeyCode.LEFT or VirtualKeyCode.RIGHT or
        VirtualKeyCode.INSERT or VirtualKeyCode.DELETE or VirtualKeyCode.HOME or VirtualKeyCode.END or
        VirtualKeyCode.PRIOR or VirtualKeyCode.NEXT or VirtualKeyCode.RCONTROL or VirtualKeyCode.RMENU or
        VirtualKeyCode.DIVIDE or VirtualKeyCode.NUMLOCK => true,
        _ => false,
    };

    private static bool SendScanKey(VirtualKeyCode vk, bool keyUp)
    {
        ushort scan = (ushort)NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC);
        if (scan == 0) return false;
        uint flags = NativeMethods.KEYEVENTF_SCANCODE
            | (IsExtendedKey(vk) ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0)
            | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0);
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION { ki = new NativeMethods.KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags } },
            },
        };
        return NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == 1;
    }
}

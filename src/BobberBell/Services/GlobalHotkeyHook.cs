using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BobberBell.Interop;

namespace BobberBell.Services;

/// <summary>
/// Low-level keyboard hook for the global F8 (start/stop) and Esc (stop) hotkeys. Keys still pass through to
/// the game, and keys this app injects itself are ignored. Read-only: the hook never records or blocks keys.
/// </summary>
internal sealed class GlobalHotkeyHook : IDisposable
{
    private readonly Action<Keys> onKeyDown;
    private NativeMethods.LowLevelKeyboardProc? hookProc; // kept alive while the hook is installed
    private IntPtr hookHandle = IntPtr.Zero;

    /// <summary>Creates the hook (not yet installed).</summary>
    /// <param name="onKeyDown">Called for each physical F8 or Esc key-down.</param>
    public GlobalHotkeyHook(Action<Keys> onKeyDown) => this.onKeyDown = onKeyDown;

    /// <summary>Installs the hook if not already installed.</summary>
    /// <returns><c>false</c> if Windows refused the hook.</returns>
    public bool Install()
    {
        if (hookHandle != IntPtr.Zero) return true;
        hookProc = HookCallback;
        hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, hookProc, NativeMethods.GetModuleHandle(null), 0);
        return hookHandle != IntPtr.Zero;
    }

    /// <summary>Removes the hook.</summary>
    public void Dispose()
    {
        if (hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            bool injected = (kb.flags & NativeMethods.LLKHF_INJECTED) != 0; // ignore keys this app sends
            if (!injected && (kb.vkCode == (uint)Keys.F8 || kb.vkCode == (uint)Keys.Escape))
                onKeyDown((Keys)kb.vkCode);
        }

        return NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }
}

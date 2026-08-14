namespace Wincy.Interop;

/// <summary>
/// Restores focus to whatever window was in front before the popup opened and
/// synthesises Ctrl+V into it — the Windows counterpart of Maccy's CGEvent paste.
/// </summary>
public static class Paster
{
    private static readonly ushort[] ModifierKeysToRelease =
    [
        NativeMethods.VK_LMENU, NativeMethods.VK_RMENU,
        NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT,
        NativeMethods.VK_LWIN, NativeMethods.VK_RWIN,
        NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL
    ];

    /// <summary>
    /// Brings <paramref name="hwnd"/> back to the foreground.
    ///
    /// Windows refuses SetForegroundWindow from a process that does not own the
    /// foreground, so we temporarily attach our input queue to the target thread —
    /// the standard workaround, and the same one every paste utility uses.
    /// </summary>
    public static bool RestoreForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var current = NativeMethods.GetForegroundWindow();
        if (current == hwnd)
        {
            return true;
        }

        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out var targetProcess);
        var ourThread = NativeMethods.GetCurrentThreadId();

        NativeMethods.AllowSetForegroundWindow((int)targetProcess);

        var attached = targetThread != ourThread &&
                       NativeMethods.AttachThreadInput(ourThread, targetThread, true);

        try
        {
            return NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(ourThread, targetThread, false);
            }
        }
    }

    /// <summary>
    /// Sends Ctrl+V. Any modifier the user is still physically holding (Alt, Shift —
    /// they were part of the shortcut that triggered the paste) is released first,
    /// otherwise the target app would see Ctrl+Alt+V instead.
    /// </summary>
    public static void SendPaste()
    {
        var inputs = new List<NativeMethods.INPUT>(12);

        foreach (var key in ModifierKeysToRelease)
        {
            if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0)
            {
                inputs.Add(Key(key, down: false));
            }
        }

        inputs.Add(Key(NativeMethods.VK_CONTROL, down: true));
        inputs.Add(Key(NativeMethods.VK_V, down: true));
        inputs.Add(Key(NativeMethods.VK_V, down: false));
        inputs.Add(Key(NativeMethods.VK_CONTROL, down: false));

        var array = inputs.ToArray();
        var sent = NativeMethods.SendInput(
            (uint)array.Length, array, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != array.Length)
        {
            Log.Warn($"SendInput delivered {sent} of {array.Length} events; the paste may not have landed");
        }
    }

    private static NativeMethods.INPUT Key(ushort virtualKey, bool down) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = (ushort)NativeMethods.MapVirtualKey(virtualKey, 0),
                dwFlags = down ? 0 : NativeMethods.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };
}

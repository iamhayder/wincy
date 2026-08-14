namespace Wincy.Interop;

/// <summary>
/// A low-level keyboard hook, installed only while the popup is open.
///
/// It exists for two reasons Maccy gets from NSEvent monitors:
///   1. the popup must know which modifiers are physically held, so rows can show
///      the shortcut that the current modifiers would trigger;
///   2. the cycle gesture (hold the hotkey modifiers, tap the key repeatedly) needs
///      to fire the moment the last modifier is released.
/// </summary>
public sealed class ModifierWatcher : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _disposed;

    /// <summary>Raised on the hook thread whenever the held-modifier set changes.</summary>
    public event Action<HotKeyModifiers>? ModifiersChanged;

    /// <summary>Raised when every modifier has been released.</summary>
    public event Action? AllModifiersReleased;

    public HotKeyModifiers Current { get; private set; } = HotKeyModifiers.None;

    public bool IsRunning => _hook != IntPtr.Zero;

    public ModifierWatcher()
    {
        _proc = HookProc;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        var module = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, module, 0);

        if (_hook == IntPtr.Zero)
        {
            Log.Warn("Could not install the keyboard hook; modifier-dependent shortcuts will not update live");
            return;
        }

        // Publish the state the user is already holding — the hotkey's own modifiers are
        // still down at this point, and the rows need the right badge immediately.
        Current = Read();
        ModifiersChanged?.Invoke(Current);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;

        if (Current != HotKeyModifiers.None)
        {
            Current = HotKeyModifiers.None;
            ModifiersChanged?.Invoke(Current);
        }
    }

    /// <summary>Polls the physical modifier state without needing the hook.</summary>
    public static HotKeyModifiers Read()
    {
        var modifiers = HotKeyModifiers.None;

        if (IsDown(NativeMethods.VK_CONTROL)) modifiers |= HotKeyModifiers.Control;
        if (IsDown(NativeMethods.VK_MENU)) modifiers |= HotKeyModifiers.Alt;
        if (IsDown(NativeMethods.VK_SHIFT)) modifiers |= HotKeyModifiers.Shift;
        if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN)) modifiers |= HotKeyModifiers.Windows;

        return modifiers;
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_KEYUP
                or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP)
            {
                // GetAsyncKeyState has not caught up with the event being delivered,
                // so read the state after letting the hook chain settle.
                var updated = Read();

                if (updated != Current)
                {
                    Current = updated;
                    ModifiersChanged?.Invoke(updated);

                    if (updated == HotKeyModifiers.None)
                    {
                        AllModifiersReleased?.Invoke();
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}

namespace Wincy.Interop;

/// <summary>
/// Owns the system-wide hotkeys. Each registration gets an id; WM_HOTKEY on the
/// message window is routed back to the caller.
/// </summary>
public sealed class HotKeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _handlers = [];
    private readonly Dictionary<string, int> _idsByName = [];
    private int _nextId = 1;
    private bool _disposed;

    public HotKeyManager(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_HOTKEY)
        {
            return false;
        }

        if (_handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
            return true;
        }

        return false;
    }

    /// <summary>
    /// (Re)registers a named hotkey. Returns false when the combination is already
    /// claimed by another process — the caller surfaces that in Settings.
    /// </summary>
    public bool Register(string name, HotKey hotKey, Action handler)
    {
        Unregister(name);

        if (!hotKey.IsValid)
        {
            return true;
        }

        var id = _nextId++;

        // Deliberately no MOD_NOREPEAT: holding the combination and tapping the key
        // again is how the cycle-through-history gesture works.
        if (!NativeMethods.RegisterHotKey(_window.Handle, id, hotKey.NativeModifiers, hotKey.VirtualKey))
        {
            Log.Warn($"Could not register hotkey '{name}' ({hotKey}) — it is probably taken by another app");
            return false;
        }

        _idsByName[name] = id;
        _handlers[id] = handler;
        Log.Info($"Registered hotkey '{name}' as {hotKey}");
        return true;
    }

    public void Unregister(string name)
    {
        if (!_idsByName.TryGetValue(name, out var id))
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_window.Handle, id);
        _idsByName.Remove(name);
        _handlers.Remove(id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessage;

        foreach (var id in _idsByName.Values)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        }

        _idsByName.Clear();
        _handlers.Clear();
    }
}

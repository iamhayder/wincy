using System.Runtime.InteropServices;

namespace Wincy.Interop;

/// <summary>
/// Notification-area icon driven straight through Shell_NotifyIcon, so the app takes
/// no dependency on WinForms. This is Wincy's menu-bar item.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = NativeMethods.WM_APP + 1;
    private const int IconId = 1;

    private readonly MessageWindow _window;
    private NativeMethods.NOTIFYICONDATA _data;
    private bool _added;
    private bool _disposed;
    private IntPtr _icon = IntPtr.Zero;

    /// <summary>Left click. The held modifiers are passed through, mirroring Maccy's option-click behaviours.</summary>
    public event Action<HotKeyModifiers>? Clicked;

    /// <summary>Right click, at the cursor position in physical pixels.</summary>
    public event Action<POINT>? ContextMenuRequested;

    public TrayIcon(MessageWindow window, IntPtr icon, string tooltip)
    {
        _window = window;
        _icon = icon;

        _data = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = window.Handle,
            uID = IconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = icon,
            szTip = Truncate(tooltip, 127),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            uVersion = NativeMethods.NOTIFYICON_VERSION_4
        };

        _window.MessageReceived += OnMessage;
    }

    public void Show()
    {
        if (_added)
        {
            return;
        }

        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _data);

        if (_added)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref _data);
        }
        else
        {
            Log.Warn("Could not add the tray icon");
        }
    }

    public void Hide()
    {
        if (!_added)
        {
            return;
        }

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _data);
        _added = false;
    }

    public void SetTooltip(string tooltip)
    {
        _data.szTip = Truncate(tooltip, 127);
        _data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;

        if (_added)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _data);
        }
    }

    public void SetIcon(IntPtr icon)
    {
        var previous = _icon;

        _icon = icon;
        _data.hIcon = icon;

        if (_added)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _data);
        }

        // Only now is the shell finished with the old handle.
        if (previous != IntPtr.Zero && previous != icon)
        {
            NativeMethods.DestroyIcon(previous);
        }
    }

    /// <summary>
    /// Balloon notification. Used instead of toast notifications because those
    /// require an MSIX identity, and Wincy deliberately ships as a plain executable.
    /// </summary>
    public void Notify(string title, string message)
    {
        if (!_added)
        {
            return;
        }

        var previousFlags = _data.uFlags;
        _data.uFlags = NativeMethods.NIF_INFO;
        _data.szInfoTitle = Truncate(title, 63);
        _data.szInfo = Truncate(message, 255);
        _data.dwInfoFlags = NativeMethods.NIIF_NONE;

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _data);

        _data.uFlags = previousFlags;
        _data.szInfo = string.Empty;
        _data.szInfoTitle = string.Empty;
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != CallbackMessage)
        {
            return false;
        }

        // With NOTIFYICON_VERSION_4 the event is in the low word of lParam and the
        // cursor position — already in screen coordinates — is in wParam.
        var eventCode = (int)((uint)lParam.ToInt64() & 0xFFFF);
        var x = (short)((uint)wParam.ToInt64() & 0xFFFF);
        var y = (short)(((uint)wParam.ToInt64() >> 16) & 0xFFFF);

        switch (eventCode)
        {
            case NativeMethods.WM_LBUTTONUP:
                Clicked?.Invoke(ModifierWatcher.Read());
                return true;

            case NativeMethods.WM_RBUTTONUP:
            case NativeMethods.WM_CONTEXTMENU:
                ContextMenuRequested?.Invoke(new POINT(x, y));
                return true;
        }

        return false;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessage;
        Hide();

        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}

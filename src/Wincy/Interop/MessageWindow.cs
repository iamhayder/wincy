using System.Windows.Interop;

namespace Wincy.Interop;

/// <summary>
/// A message-only window. Everything that needs an HWND but no pixels — the
/// clipboard format listener, the global hotkey, the tray icon callback and
/// system theme-change broadcasts — hangs off this.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private bool _disposed;

    public IntPtr Handle => _source.Handle;

    /// <summary>Raised for every message. Set <c>handled</c> to swallow it.</summary>
    public event Func<int, IntPtr, IntPtr, bool>? MessageReceived;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("WincyMessageWindow")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var handlers = MessageReceived;
        if (handlers is null)
        {
            return IntPtr.Zero;
        }

        foreach (Func<int, IntPtr, IntPtr, bool> handler in handlers.GetInvocationList())
        {
            try
            {
                if (handler(msg, wParam, lParam))
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Log.Error("MessageWindow handler threw", ex);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

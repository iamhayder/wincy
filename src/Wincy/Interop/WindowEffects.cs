using System.Windows;
using System.Windows.Interop;

namespace Wincy.Interop;

public enum BackdropKind
{
    None,
    Mica,
    Acrylic
}

/// <summary>
/// DWM window decoration: rounded corners, dark title bar, and the Mica/Acrylic
/// backdrop that gives the popup the same "material" feel Maccy gets from
/// NSVisualEffectView.
/// </summary>
public static class WindowEffects
{
    /// <summary>Backdrops and corner preferences landed in Windows 11 22000.</summary>
    public static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22000;

    public static IntPtr HandleOf(Window window) => new WindowInteropHelper(window).Handle;

    public static void ApplyDarkMode(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    public static void ApplyRoundedCorners(IntPtr hwnd, bool rounded = true)
    {
        if (hwnd == IntPtr.Zero || !SupportsBackdrop)
        {
            return;
        }

        var value = rounded ? NativeMethods.DWMWCP_ROUND : NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
    }

    /// <summary>
    /// Enables a system backdrop. Returns false on Windows 10, where the caller should
    /// fall back to a solid themed brush.
    /// </summary>
    public static bool ApplyBackdrop(IntPtr hwnd, BackdropKind kind)
    {
        if (hwnd == IntPtr.Zero || !SupportsBackdrop)
        {
            return false;
        }

        // The backdrop is only visible through the client area, so the frame has to
        // be extended into it first.
        var margins = new NativeMethods.MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1
        };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        var value = kind switch
        {
            BackdropKind.Mica => NativeMethods.DWMSBT_MAINWINDOW,
            BackdropKind.Acrylic => NativeMethods.DWMSBT_TRANSIENTWINDOW,
            _ => NativeMethods.DWMSBT_NONE
        };

        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int)) == 0;
    }

    /// <summary>
    /// Marks the window as a tool window so it never shows up in the taskbar or in
    /// Alt+Tab — the popup is an overlay, not a document.
    /// </summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW;
        style &= ~(long)NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>Moves and resizes using physical pixels, bypassing WPF's DIP mapping.</summary>
    public static void SetBounds(IntPtr hwnd, int x, int y, int width, int height, bool activate = true)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var flags = NativeMethods.SWP_SHOWWINDOW;
        if (!activate)
        {
            flags |= NativeMethods.SWP_NOACTIVATE;
        }

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, width, height, flags);
    }

    public static RECT GetBounds(IntPtr hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }
}

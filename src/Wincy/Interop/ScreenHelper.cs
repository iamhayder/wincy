namespace Wincy.Interop;

/// <summary>A monitor, in physical pixels, plus its scale factor.</summary>
public readonly record struct MonitorInfo(
    IntPtr Handle,
    RECT Bounds,
    RECT WorkArea,
    string DeviceName,
    bool IsPrimary,
    double Scale)
{
    public string DisplayName => IsPrimary ? $"{DeviceName} (primary)" : DeviceName;
}

/// <summary>
/// Monitor geometry in physical pixels.
///
/// The popup is positioned with SetWindowPos rather than WPF's Left/Top because
/// WPF measures in device-independent units relative to the primary monitor, which
/// lands the window in the wrong place on mixed-DPI setups.
/// </summary>
public static class ScreenHelper
{
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITORINFOF_PRIMARY = 1;

    public static List<MonitorInfo> All()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr handle, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var info = Describe(handle);
                if (info.HasValue)
                {
                    monitors.Add(info.Value);
                }

                return true;
            }, IntPtr.Zero);

        // Primary first, then left-to-right: keeps the Settings picker stable.
        return [.. monitors.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left)];
    }

    public static MonitorInfo? Describe(IntPtr handle)
    {
        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
        };

        if (!NativeMethods.GetMonitorInfo(handle, ref info))
        {
            return null;
        }

        var scale = 1.0;
        try
        {
            if (NativeMethods.GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                scale = dpiX / 96.0;
            }
        }
        catch (DllNotFoundException)
        {
            // shcore.dll is Windows 8.1+. Falling back to 1.0 is fine.
        }

        return new MonitorInfo(
            handle,
            info.rcMonitor,
            info.rcWork,
            info.szDevice,
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
            scale);
    }

    public static MonitorInfo FromPoint(POINT point)
    {
        var handle = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return Describe(handle) ?? Primary();
    }

    public static MonitorInfo FromWindow(IntPtr hwnd)
    {
        var handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return Describe(handle) ?? Primary();
    }

    public static MonitorInfo Primary()
    {
        var handle = NativeMethods.MonitorFromPoint(new POINT(0, 0), NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        return Describe(handle) ?? new MonitorInfo(
            IntPtr.Zero,
            new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
            "DISPLAY1",
            true,
            1.0);
    }

    public static POINT CursorPosition()
    {
        NativeMethods.GetCursorPos(out var point);
        return point;
    }

    /// <summary>Keeps a window rectangle fully inside the given monitor's work area.</summary>
    public static (int X, int Y) Constrain(int x, int y, int width, int height, MonitorInfo monitor)
    {
        var work = monitor.WorkArea;
        var maxX = Math.Max(work.Left, work.Right - width);
        var maxY = Math.Max(work.Top, work.Bottom - height);

        return (
            Math.Clamp(x, work.Left, maxX),
            Math.Clamp(y, work.Top, maxY));
    }
}

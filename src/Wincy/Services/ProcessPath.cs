using System.Text;
using Wincy.Interop;

namespace Wincy.Services;

/// <summary>Reads the name of the process that owns a window handle.</summary>
public static class ProcessPath
{
    public static string? FromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return FromProcessId(processId);
    }

    public static string? FromProcessId(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString(0, (int)size)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}

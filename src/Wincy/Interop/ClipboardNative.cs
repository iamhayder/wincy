using System.Runtime.InteropServices;

namespace Wincy.Interop;

/// <summary>
/// Raw clipboard access.
///
/// WPF's <c>System.Windows.Clipboard</c> is not used here on purpose: Wincy needs to
/// enumerate every format an app published and round-trip the exact bytes, which the
/// managed wrapper cannot do without reinterpreting the data.
/// </summary>
public static class ClipboardNative
{
    private const int OpenAttempts = 10;
    private const int OpenRetryDelayMs = 20;

    /// <summary>
    /// Opens the clipboard, runs <paramref name="work"/>, and always closes again.
    /// The clipboard is a global mutex owned by whoever grabbed it last, so opening
    /// is retried a few times before giving up.
    /// </summary>
    public static bool Use(IntPtr owner, Action work)
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(owner))
            {
                try
                {
                    work();
                    return true;
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            }

            Thread.Sleep(OpenRetryDelayMs);
        }

        Log.Warn("Could not open the clipboard after " + OpenAttempts + " attempts");
        return false;
    }

    /// <summary>Every format currently published on the clipboard. Requires an open clipboard.</summary>
    public static List<uint> EnumerateFormats()
    {
        var formats = new List<uint>();
        uint format = 0;

        while ((format = NativeMethods.EnumClipboardFormats(format)) != 0)
        {
            formats.Add(format);

            if (formats.Count > 256)
            {
                break; // Defensive: never spin on a misbehaving provider.
            }
        }

        return formats;
    }

    /// <summary>Copies the bytes of one format out of the clipboard. Requires an open clipboard.</summary>
    public static byte[]? ReadBytes(uint format)
    {
        var handle = NativeMethods.GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var size = (int)NativeMethods.GlobalSize(handle);
            if (size <= 0)
            {
                return null;
            }

            var buffer = new byte[size];
            Marshal.Copy(pointer, buffer, 0, size);
            return buffer;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// Publishes one format. Requires an open, emptied clipboard. On success the
    /// memory block belongs to the system and must not be freed by us.
    /// </summary>
    public static bool WriteBytes(uint format, byte[] data)
    {
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GHND, (UIntPtr)(uint)data.Length);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }

        Marshal.Copy(data, 0, pointer, data.Length);
        NativeMethods.GlobalUnlock(handle);

        if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }

        return true;
    }

    /// <summary>The process that owns the current clipboard contents, if it can be determined.</summary>
    public static IntPtr Owner() => NativeMethods.GetClipboardOwner();

    public static uint SequenceNumber() => NativeMethods.GetClipboardSequenceNumber();
}

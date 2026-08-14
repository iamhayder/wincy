using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Wincy.Interop;

namespace Wincy.Services;

/// <summary>
/// Caches the small icon of each source application, so the list can show which app
/// a copy came from without hitting the shell on every row render.
/// </summary>
public sealed class AppIconCache
{
    private readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public BitmapSource? Get(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(executablePath, out var cached))
            {
                return cached;
            }
        }

        var icon = Extract(executablePath);

        lock (_gate)
        {
            _cache[executablePath] = icon;
        }

        return icon;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
        }
    }

    private static BitmapSource? Extract(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var small = new IntPtr[1];

        try
        {
            if (NativeMethods.ExtractIconEx(path, 0, null, small, 1) == 0 || small[0] == IntPtr.Zero)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                small[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the icon for {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
        finally
        {
            if (small[0] != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(small[0]);
            }
        }
    }
}

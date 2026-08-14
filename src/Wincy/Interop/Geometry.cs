using System.Runtime.InteropServices;

namespace Wincy.Interop;

/// <summary>
/// Win32 POINT. Public rather than nested inside <c>NativeMethods</c> because screen
/// coordinates cross into the view layer, and an internal type cannot appear in a
/// public signature.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override readonly string ToString() => $"({X}, {Y})";
}

/// <summary>Win32 RECT: edges, not offsets and sizes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;

    public override readonly string ToString() => $"[{Left}, {Top}, {Right}, {Bottom}]";
}

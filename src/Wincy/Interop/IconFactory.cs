using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wincy.Interop;

/// <summary>
/// Turns a WPF bitmap into an HICON.
///
/// The tray API only speaks HICON, but drawing the glyph as vector geometry and
/// rasterising it at the current small-icon size keeps the icon crisp on every DPI —
/// which a fixed-size .ico resource would not.
/// </summary>
public static class IconFactory
{
    public static int SmallIconSize
    {
        get
        {
            var size = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
            return size > 0 ? size : 16;
        }
    }

    /// <summary>Rasterises a geometry into a square icon of the given size.</summary>
    public static IntPtr FromGeometry(Geometry geometry, Brush fill, int size, Brush? background = null)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            if (background is not null)
            {
                var radius = size * 0.22;
                context.DrawRoundedRectangle(
                    background, null, new System.Windows.Rect(0, 0, size, size), radius, radius);
            }

            // Scale the glyph into the icon box with a little breathing room.
            var bounds = geometry.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                var inset = background is null ? size * 0.06 : size * 0.22;
                var available = size - (inset * 2);
                var scale = Math.Min(available / bounds.Width, available / bounds.Height);

                var group = new TransformGroup();
                group.Children.Add(new TranslateTransform(-bounds.X, -bounds.Y));
                group.Children.Add(new ScaleTransform(scale, scale));
                group.Children.Add(new TranslateTransform(
                    (size - (bounds.Width * scale)) / 2,
                    (size - (bounds.Height * scale)) / 2));

                context.PushTransform(group);
                context.DrawGeometry(fill, null, geometry);
                context.Pop();
            }
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        return FromBitmap(target);
    }

    /// <summary>Builds an HICON from a 32bpp bitmap. The caller owns the result and must destroy it.</summary>
    public static IntPtr FromBitmap(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;

        var pixels = new byte[stride * height];
        var converted = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);

        var header = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = width,
            // Negative height means a top-down bitmap, which matches WPF's row order.
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = NativeMethods.BI_RGB
        };

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var colorBitmap = IntPtr.Zero;
        var maskBitmap = IntPtr.Zero;

        try
        {
            colorBitmap = NativeMethods.CreateDIBSection(
                screenDc, ref header, NativeMethods.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);

            if (colorBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            Marshal.Copy(pixels, 0, bits, pixels.Length);

            // A 1bpp mask is still required even for alpha icons; an all-zero mask means
            // "use the colour bitmap's alpha".
            maskBitmap = NativeMethods.CreateBitmap(width, height, 1, 1, IntPtr.Zero);
            if (maskBitmap == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var info = new NativeMethods.ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = maskBitmap,
                hbmColor = colorBitmap
            };

            return NativeMethods.CreateIconIndirect(ref info);
        }
        finally
        {
            if (colorBitmap != IntPtr.Zero) NativeMethods.DeleteObject(colorBitmap);
            if (maskBitmap != IntPtr.Zero) NativeMethods.DeleteObject(maskBitmap);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}

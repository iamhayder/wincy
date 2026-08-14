using System.Windows.Media;
using Wincy.Interop;
using Wincy.Services;

namespace Wincy.Views;

/// <summary>
/// The three tray glyphs Wincy offers, mirroring Maccy's menu-bar icon choices.
/// They are drawn as vector paths and rasterised at the current small-icon size, so
/// they stay sharp at any scaling factor.
/// </summary>
public static class TrayIconFactory
{
    // A clipboard: board outline with a clip at the top.
    private const string ClipboardPath =
        "M 9,2 L 15,2 A 1,1 0 0 1 16,3 L 16,4 L 18,4 A 2,2 0 0 1 20,6 L 20,21 " +
        "A 2,2 0 0 1 18,23 L 6,23 A 2,2 0 0 1 4,21 L 4,6 A 2,2 0 0 1 6,4 L 8,4 " +
        "L 8,3 A 1,1 0 0 1 9,2 Z " +
        "M 10,4 L 10,6 L 14,6 L 14,4 Z " +
        "M 6,6 L 6,21 L 18,21 L 18,6 L 16,6 L 16,8 L 8,8 L 8,6 Z";

    // Scissors: two blades crossed over two finger holes.
    private const string ScissorsPath =
        "M 6,2 L 13,12 L 11.5,14.2 L 4.6,4.5 Z " +
        "M 18,2 L 11,12 L 12.5,14.2 L 19.4,4.5 Z " +
        "M 6,16 A 3.2,3.2 0 1 0 6,22.4 A 3.2,3.2 0 1 0 6,16 Z " +
        "M 6,18 A 1.2,1.2 0 1 1 6,20.4 A 1.2,1.2 0 1 1 6,18 Z " +
        "M 18,16 A 3.2,3.2 0 1 0 18,22.4 A 3.2,3.2 0 1 0 18,16 Z " +
        "M 18,18 A 1.2,1.2 0 1 1 18,20.4 A 1.2,1.2 0 1 1 18,18 Z";

    // Wincy's own mark: a stack of three offset cards.
    private const string StackPath =
        "M 7,3 L 19,3 A 2,2 0 0 1 21,5 L 21,15 A 2,2 0 0 1 19,17 L 7,17 " +
        "A 2,2 0 0 1 5,15 L 5,5 A 2,2 0 0 1 7,3 Z " +
        "M 7,5 L 7,15 L 19,15 L 19,5 Z " +
        "M 3,7 L 3,19 A 2,2 0 0 0 5,21 L 17,21 L 17,19 L 5,19 L 5,7 Z";

    public static IntPtr Create(TrayIconStyle style)
    {
        var size = IconFactory.SmallIconSize;

        (string data, Brush? background) = style switch
        {
            TrayIconStyle.Clipboard => (ClipboardPath, (Brush?)null),
            TrayIconStyle.Scissors => (ScissorsPath, (Brush?)null),
            _ => (StackPath, (Brush?)new SolidColorBrush(SystemTheme.Accent()))
        };

        var geometry = Geometry.Parse(data);
        geometry.Freeze();

        // Without a badge behind it the glyph is drawn in the tray's own foreground
        // colour, so it inverts correctly between light and dark taskbars.
        var fill = background is null
            ? new SolidColorBrush(SystemTheme.IsDark() ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20))
            : Brushes.White;

        background?.Freeze();
        fill.Freeze();

        var icon = IconFactory.FromGeometry(geometry, fill, size, background);

        if (icon == IntPtr.Zero)
        {
            Log.Warn("Could not build the tray icon");
        }

        return icon;
    }

    /// <summary>The same mark, as a brush-fillable geometry, for the About window.</summary>
    public static Geometry Mark()
    {
        var geometry = Geometry.Parse(StackPath);
        geometry.Freeze();
        return geometry;
    }
}

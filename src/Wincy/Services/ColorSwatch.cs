using System.Globalization;
using System.Windows.Media;

namespace Wincy.Services;

/// <summary>
/// Recognises a copied CSS colour so the row can show a swatch next to it, matching
/// Maccy's ColorImage behaviour.
/// </summary>
public static class ColorSwatch
{
    public static Color? Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var text = title.Trim();

        if (text.StartsWith('#'))
        {
            return ParseHex(text[1..]);
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRgb(text);
        }

        return null;
    }

    private static Color? ParseHex(string hex)
    {
        static bool IsHex(string value) =>
            value.All(c => Uri.IsHexDigit(c));

        if (!IsHex(hex))
        {
            return null;
        }

        static byte Component(string value) =>
            byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        switch (hex.Length)
        {
            case 3:
                return Color.FromRgb(
                    Component($"{hex[0]}{hex[0]}"),
                    Component($"{hex[1]}{hex[1]}"),
                    Component($"{hex[2]}{hex[2]}"));

            case 6:
                return Color.FromRgb(
                    Component(hex[..2]),
                    Component(hex.Substring(2, 2)),
                    Component(hex.Substring(4, 2)));

            case 8:
                return Color.FromArgb(
                    Component(hex.Substring(6, 2)),
                    Component(hex[..2]),
                    Component(hex.Substring(2, 2)),
                    Component(hex.Substring(4, 2)));

            default:
                return null;
        }
    }

    private static Color? ParseRgb(string text)
    {
        var open = text.IndexOf('(');
        var close = text.IndexOf(')');
        if (open < 0 || close <= open)
        {
            return null;
        }

        var parts = text[(open + 1)..close]
            .Split(new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length is < 3 or > 4)
        {
            return null;
        }

        if (!byte.TryParse(parts[0], out var r) ||
            !byte.TryParse(parts[1], out var g) ||
            !byte.TryParse(parts[2], out var b))
        {
            return null;
        }

        if (parts.Length == 4 &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
        {
            var a = alpha <= 1.0 ? alpha * 255 : alpha;
            return Color.FromArgb((byte)Math.Clamp(a, 0, 255), r, g, b);
        }

        return Color.FromRgb(r, g, b);
    }
}

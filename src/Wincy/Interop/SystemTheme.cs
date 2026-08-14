using System.Windows.Media;
using Microsoft.Win32;

namespace Wincy.Interop;

/// <summary>Reads the user's light/dark preference and accent colour, and watches for changes.</summary>
public static class SystemTheme
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // AppsUseLightTheme: 0 = dark, 1 = light. Missing means light.
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The DWM colourization colour, which is what Windows draws selection and accents
    /// with. Falls back to the Windows default blue when DWM has nothing to report.
    /// </summary>
    public static Color Accent()
    {
        try
        {
            if (NativeMethods.DwmGetColorizationColor(out var argb, out _) == 0)
            {
                var color = Color.FromRgb(
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));

                // Fully desaturated results mean DWM is in "automatic" mode with no
                // usable colour; the default reads better than grey selection.
                if (color is { R: 0, G: 0, B: 0 })
                {
                    return DefaultAccent;
                }

                return color;
            }
        }
        catch
        {
            // Fall through.
        }

        return DefaultAccent;
    }

    public static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary>
    /// Chooses black or white text for a background, using the WCAG relative
    /// luminance formula so selection rows stay readable with any accent colour.
    /// </summary>
    public static Color ForegroundFor(Color background)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        var luminance =
            0.2126 * Channel(background.R) +
            0.7152 * Channel(background.G) +
            0.0722 * Channel(background.B);

        return luminance > 0.45 ? Colors.Black : Colors.White;
    }

    /// <summary>
    /// Raised when the user switches theme or accent colour. Wired to the message
    /// window's WM_SETTINGCHANGE / WM_DWMCOLORIZATIONCOLORCHANGED.
    /// </summary>
    public static event Action? Changed;

    public static void Attach(MessageWindow window)
    {
        window.MessageReceived += (msg, _, lParam) =>
        {
            switch (msg)
            {
                case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                    Changed?.Invoke();
                    break;
                case NativeMethods.WM_SETTINGCHANGE:
                    if (lParam != IntPtr.Zero)
                    {
                        var section = System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam);
                        if (section is "ImmersiveColorSet")
                        {
                            Changed?.Invoke();
                        }
                    }

                    break;
            }

            return false;
        };
    }
}

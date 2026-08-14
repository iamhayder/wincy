using System.Media;
using Wincy.Interop;

namespace Wincy.Services;

/// <summary>
/// Feedback when something is recorded or copied. Maccy plays Write/Knock sounds and
/// posts a user notification; Wincy uses the matching system sounds and a tray balloon,
/// which needs no packaged identity.
/// </summary>
public sealed class Notifier(AppSettings settings)
{
    private TrayIcon? _tray;

    public void Attach(TrayIcon tray) => _tray = tray;

    /// <summary>A new copy was recorded.</summary>
    public void Recorded(string title) => Notify("Copied", title, SystemSounds.Asterisk);

    /// <summary>A history item was placed back on the clipboard.</summary>
    public void Reused(string title) => Notify("Wincy", title, SystemSounds.Hand);

    private void Notify(string heading, string body, SystemSound sound)
    {
        if (settings.PlaySounds)
        {
            try
            {
                sound.Play();
            }
            catch
            {
                // Sound output is optional; never let it interrupt a copy.
            }
        }

        if (settings.ShowNotifications && !string.IsNullOrWhiteSpace(body))
        {
            _tray?.Notify(heading, Shorten(body));
        }
    }

    private static string Shorten(string value)
    {
        var single = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= 120 ? single : single[..120] + "…";
    }
}

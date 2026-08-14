using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Wincy.Interop;
using Wincy.Models;

namespace Wincy.Services;

/// <summary>
/// Watches the clipboard and puts history items back on it.
///
/// Unlike Maccy, which polls NSPasteboard's changeCount every 500 ms, Wincy uses
/// <c>AddClipboardFormatListener</c> and reacts to WM_CLIPBOARDUPDATE. A short settle
/// delay follows each notification because many applications publish their formats in
/// more than one pass, and reading immediately would capture only the first.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private readonly MessageWindow _window;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _debounce;

    private uint _lastSequence;
    private bool _listening;
    private bool _disposed;

    /// <summary>Raised on the UI thread for every copy Wincy decides to record.</summary>
    public event Action<ClipItem>? NewCopy;

    /// <summary>Raised when the clipboard changed because of something other than Wincy.</summary>
    public event Action? ExternalChange;

    public ClipboardService(MessageWindow window, AppSettings settings)
    {
        _window = window;
        _settings = settings;

        _debounce = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, settings.ClipboardDebounceMs))
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Capture();
        };
    }

    public void Start()
    {
        if (_listening)
        {
            return;
        }

        _lastSequence = ClipboardNative.SequenceNumber();
        _window.MessageReceived += OnMessage;

        _listening = NativeMethods.AddClipboardFormatListener(_window.Handle);
        if (!_listening)
        {
            Log.Error("AddClipboardFormatListener failed; clipboard history will not update");
            return;
        }

        Log.Info("Clipboard listener started");
    }

    public void Stop()
    {
        if (!_listening)
        {
            return;
        }

        NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _window.MessageReceived -= OnMessage;
        _listening = false;
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE)
        {
            return false;
        }

        _debounce.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _settings.ClipboardDebounceMs));
        _debounce.Stop();
        _debounce.Start();
        return false;
    }

    // ------------------------------------------------------------------ capture

    private void Capture()
    {
        var sequence = ClipboardNative.SequenceNumber();
        if (sequence == _lastSequence)
        {
            return;
        }

        _lastSequence = sequence;

        List<ClipContent>? contents = null;
        var fromWincy = false;
        var rejected = false;
        string? ownerPath = null;

        var opened = ClipboardNative.Use(_window.Handle, () =>
        {
            var formatIds = ClipboardNative.EnumerateFormats();
            var names = formatIds.ToDictionary(id => id, ClipFormats.ToName);

            fromWincy = names.Values.Any(n =>
                string.Equals(n, ClipFormats.WincySource, StringComparison.OrdinalIgnoreCase));

            if (IsBlockedByPrivacyMarker(names))
            {
                rejected = true;
                return;
            }

            if (IsBlockedByIgnoredFormat(names.Values))
            {
                rejected = true;
                return;
            }

            contents = ReadContents(formatIds, names);
        });

        if (!opened)
        {
            return;
        }

        if (!fromWincy)
        {
            ExternalChange?.Invoke();
        }

        if (rejected)
        {
            Log.Info("Skipping a copy that is marked as private or ignored");
            return;
        }

        // Honour the "turn off" switch after reading, so the sequence number stays in
        // step and the next real copy is not missed.
        if (_settings.IgnoreEvents)
        {
            if (_settings.IgnoreOnlyNextEvent)
            {
                _settings.IgnoreEvents = false;
                _settings.IgnoreOnlyNextEvent = false;
            }

            return;
        }

        if (contents is null || contents.Count == 0)
        {
            return;
        }

        ownerPath = SourceApplicationPath();

        if (ownerPath is not null && IsIgnoredApplication(ownerPath))
        {
            Log.Info($"Skipping a copy from an ignored app: {Path.GetFileName(ownerPath)}");
            return;
        }

        var item = new ClipItem(contents)
        {
            Application = ownerPath
        };

        if (IsBlockedByRegex(item))
        {
            Log.Info("Skipping a copy that matched an ignore pattern");
            return;
        }

        if (IsEmptyText(item))
        {
            return;
        }

        item.Title = item.GenerateTitle(_settings.ShowSpecialSymbols);
        NewCopy?.Invoke(item);
    }

    private List<ClipContent> ReadContents(List<uint> formatIds, Dictionary<uint, string> names)
    {
        var enabled = _settings.EnabledFormats.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contents = new List<ClipContent>();
        var storedImage = false;

        foreach (var id in formatIds)
        {
            var name = names[id];

            if (string.Equals(name, ClipFormats.WincySource, StringComparison.OrdinalIgnoreCase))
            {
                contents.Add(new ClipContent(name, [1]));
                continue;
            }

            if (!enabled.Contains(name))
            {
                continue;
            }

            // Images: keep one representation. PNG is preferred; a DIB is transcoded
            // to PNG so that a full-screen screenshot costs kilobytes, not megabytes.
            if (ClipFormats.IsImage(name))
            {
                if (storedImage)
                {
                    continue;
                }

                var raw = ClipboardNative.ReadBytes(id);
                if (raw is null || raw.Length == 0)
                {
                    continue;
                }

                if (string.Equals(name, ClipFormats.Png, StringComparison.OrdinalIgnoreCase))
                {
                    contents.Add(new ClipContent(ClipFormats.Png, raw));
                    storedImage = true;
                }
                else
                {
                    var decoded = ImageHelper.Decode(raw, isDib: true);
                    var png = decoded is null ? null : ImageHelper.EncodePng(decoded);
                    if (png is not null)
                    {
                        contents.Add(new ClipContent(ClipFormats.Png, png));
                        storedImage = true;
                    }
                }

                continue;
            }

            var bytes = ClipboardNative.ReadBytes(id);
            if (bytes is { Length: > 0 })
            {
                contents.Add(new ClipContent(name, bytes));
            }
        }

        return contents;
    }

    /// <summary>
    /// Formats that mean "do not record me". Password managers and browsers in private
    /// mode publish these; honouring them is the whole reason Wincy is safe to run.
    /// </summary>
    private static bool IsBlockedByPrivacyMarker(Dictionary<uint, string> names)
    {
        foreach (var (id, name) in names)
        {
            if (!ClipFormats.PrivacyMarkers.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // "CanIncludeInClipboardHistory" and "CanUploadToCloudClipboard" carry a
            // DWORD: zero forbids, non-zero explicitly allows.
            if (name is "CanIncludeInClipboardHistory" or "CanUploadToCloudClipboard")
            {
                var value = ClipboardNative.ReadBytes(id);
                if (value is { Length: >= 4 } && BitConverter.ToInt32(value, 0) != 0)
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }

    private bool IsBlockedByIgnoredFormat(IEnumerable<string> names)
    {
        if (_settings.IgnoredFormats.Count == 0)
        {
            return false;
        }

        var ignored = _settings.IgnoredFormats.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Any(ignored.Contains);
    }

    private bool IsIgnoredApplication(string path)
    {
        var name = Path.GetFileName(path);

        bool Matches(string entry) =>
            entry.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            entry.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            entry.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase);

        var listed = _settings.IgnoredApps.Any(Matches);
        return _settings.IgnoreAllAppsExceptListed ? !listed : listed;
    }

    private bool IsBlockedByRegex(ClipItem item)
    {
        if (_settings.IgnoreRegexes.Count == 0)
        {
            return false;
        }

        var text = item.Text;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var pattern in _settings.IgnoreRegexes)
        {
            try
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // An invalid pattern should not block copying; Settings validates on entry.
            }
            catch (RegexMatchTimeoutException)
            {
                Log.Warn($"Ignore pattern '{pattern}' timed out");
            }
        }

        return false;
    }

    /// <summary>
    /// Drops whitespace-only text copies, unless the item also carries rich content —
    /// the same guard Maccy applies to empty pasteboard strings.
    /// </summary>
    private static bool IsEmptyText(ClipItem item)
    {
        if (item.HasImage || item.FileNames.Count > 0)
        {
            return false;
        }

        var text = item.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(item.Rtf) && string.IsNullOrWhiteSpace(item.Html);
    }

    private string? SourceApplicationPath()
    {
        // The clipboard owner is the app that actually performed the copy. It can be
        // null when the owner published data and exited, so fall back to whatever is
        // in front.
        var owner = ClipboardNative.Owner();
        var path = ProcessPath.FromWindow(owner);

        if (!string.IsNullOrEmpty(path) && !IsSelf(path))
        {
            return path;
        }

        var foreground = ProcessPath.FromWindow(NativeMethods.GetForegroundWindow());
        return IsSelf(foreground) ? path : foreground;
    }

    private static bool IsSelf(string? path) =>
        !string.IsNullOrEmpty(path) &&
        string.Equals(Path.GetFileName(path), "Wincy.exe", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------- write

    /// <summary>Places a history item back on the clipboard.</summary>
    public void Copy(ClipItem item, bool removeFormatting)
    {
        var contents = removeFormatting ? StripFormatting(item.Contents) : item.Contents;

        ClipboardNative.Use(_window.Handle, () =>
        {
            NativeMethods.EmptyClipboard();

            foreach (var content in contents)
            {
                if (content.Value is not { Length: > 0 })
                {
                    continue;
                }

                if (ClipFormats.NonIdentifyingFormats.Contains(content.Format))
                {
                    continue;
                }

                if (string.Equals(content.Format, ClipFormats.Png, StringComparison.OrdinalIgnoreCase))
                {
                    WriteImage(content.Value);
                    continue;
                }

                ClipboardNative.WriteBytes(ClipFormats.ToId(content.Format), content.Value);
            }

            // Marker so the listener recognises this update as ours.
            ClipboardNative.WriteBytes(ClipFormats.ToId(ClipFormats.WincySource), [1]);
        });

        Log.Info($"Copied '{Shorten(item.Title)}' to the clipboard");
    }

    private static void WriteImage(byte[] png)
    {
        ClipboardNative.WriteBytes(ClipFormats.ToId(ClipFormats.Png), png);

        // Most Windows apps only look for CF_DIB, so publish one alongside the PNG.
        var decoded = ImageHelper.Decode(png, isDib: false);
        if (decoded is null)
        {
            return;
        }

        var dib = ImageHelper.BitmapToDib(decoded);
        if (dib is not null)
        {
            ClipboardNative.WriteBytes(NativeMethods.CF_DIB, dib);
        }
    }

    /// <summary>
    /// Keeps only plain text, plus file paths. Mirrors Maccy's clearFormatting: if the
    /// item has no text representation at all, formatting removal is a no-op.
    /// </summary>
    private static List<ClipContent> StripFormatting(List<ClipContent> contents)
    {
        var text = contents
            .Where(c => string.Equals(c.Format, ClipFormats.UnicodeText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (text.Count == 0)
        {
            return contents;
        }

        var files = contents
            .Where(c => string.Equals(c.Format, ClipFormats.Drop, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return [.. text, .. files];
    }

    /// <summary>Puts arbitrary text on the clipboard (used when Enter is pressed with no selection).</summary>
    public void CopyText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + '\0');

        ClipboardNative.Use(_window.Handle, () =>
        {
            NativeMethods.EmptyClipboard();
            ClipboardNative.WriteBytes(NativeMethods.CF_UNICODETEXT, bytes);
            ClipboardNative.WriteBytes(ClipFormats.ToId(ClipFormats.WincySource), [1]);
        });
    }

    /// <summary>Empties the system clipboard, for the "clear system clipboard too" setting.</summary>
    public void ClearSystem()
    {
        if (!_settings.ClearSystemClipboard)
        {
            return;
        }

        ClipboardNative.Use(_window.Handle, () => NativeMethods.EmptyClipboard());
    }

    public static void Paste() => Paster.SendPaste();

    private static string Shorten(string value) =>
        value.Length <= 60 ? value : value[..60] + "…";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounce.Stop();
        Stop();
    }
}

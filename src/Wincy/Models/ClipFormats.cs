using System.Text;
using Wincy.Interop;

namespace Wincy.Models;

/// <summary>
/// Clipboard format bookkeeping.
///
/// Formats are persisted by *name* rather than by numeric id, because registered
/// format ids are assigned per-session and would be meaningless after a restart.
/// Standard formats get stable synthetic names ("CF_UNICODETEXT", ...).
/// </summary>
public static class ClipFormats
{
    public const string UnicodeText = "CF_UNICODETEXT";
    public const string Drop = "CF_HDROP";
    public const string Dib = "CF_DIB";
    public const string DibV5 = "CF_DIBV5";
    public const string OemText = "CF_OEMTEXT";
    public const string AnsiText = "CF_TEXT";

    public const string Html = "HTML Format";
    public const string Rtf = "Rich Text Format";
    public const string Png = "PNG";

    /// <summary>Marker Wincy writes so the listener can recognise its own copies.</summary>
    public const string WincySource = "WincySource";

    /// <summary>Records which app the copy originated from, mirroring Maccy's "source" type.</summary>
    public const string SourceApplication = "WincySourceApplication";

    /// <summary>
    /// Formats whose presence means "do not record this". These are the Windows
    /// equivalents of Maccy's transient / concealed / auto-generated pasteboard types:
    /// password managers and similar apps set them on purpose.
    /// </summary>
    public static readonly string[] PrivacyMarkers =
    [
        "Clipboard Viewer Ignore",
        "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard"
    ];

    /// <summary>Extra formats ignored by default, editable in Settings → Ignore.</summary>
    public static readonly string[] DefaultIgnoredFormats =
    [
        "Clipboard Viewer Ignore",
        "ExcludeClipboardContentFromMonitorProcessing",
        "org.nspasteboard.TransientType",
        "PasswordSafe",
        "KeePass"
    ];

    public static readonly string[] TextFormats = [UnicodeText, Html, Rtf];
    public static readonly string[] ImageFormats = [Png, Dib, DibV5];
    public static readonly string[] FileFormats = [Drop];

    public static readonly string[] AllStorableFormats =
        [.. TextFormats, .. ImageFormats, .. FileFormats];

    /// <summary>
    /// Formats that carry no user content and therefore must not participate in
    /// duplicate detection — the analogue of Maccy's <c>transientTypes</c>.
    /// </summary>
    public static readonly HashSet<string> NonIdentifyingFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        WincySource,
        SourceApplication,
        "Clipboard Viewer Ignore",
        "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
        "Preferred DropEffect",
        "Shell IDList Array",
        "DataObjectAttributes",
        "DataObjectAttributesRequiringElevation",
        "Chromium internal source URL",
        "Chromium Web Custom MIME Data Format",
        "chromium/x-web-custom-data",
        "chromium/x-source-url",
        "chromium/x-renderer-taint",
        "WebKit Smart Paste Document Range",
        "com.apple.webkit.custom-pasteboard-data",
        "AppleWebKitPasteboardData"
    };

    /// <summary>Formats that arrive as raw bytes with no useful text or image projection.</summary>
    public static readonly string[] DynamicPrefixes =
    [
        "Ole Private Data",
        "Object Descriptor",
        "Link Source",
        "Embed Source",
        "Embedded Object",
        "Link Source Descriptor",
        "ObjectLink"
    ];

    private static readonly Dictionary<string, uint> RegisteredCache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>Maps a persisted format name back to a numeric clipboard format id.</summary>
    public static uint ToId(string name) => name switch
    {
        UnicodeText => NativeMethods.CF_UNICODETEXT,
        Drop => NativeMethods.CF_HDROP,
        Dib => NativeMethods.CF_DIB,
        DibV5 => NativeMethods.CF_DIBV5,
        OemText => NativeMethods.CF_OEMTEXT,
        AnsiText => NativeMethods.CF_TEXT,
        _ => Register(name)
    };

    /// <summary>Maps a live clipboard format id to the name Wincy persists.</summary>
    public static string ToName(uint id)
    {
        switch (id)
        {
            case NativeMethods.CF_UNICODETEXT: return UnicodeText;
            case NativeMethods.CF_HDROP: return Drop;
            case NativeMethods.CF_DIB: return Dib;
            case NativeMethods.CF_DIBV5: return DibV5;
            case NativeMethods.CF_OEMTEXT: return OemText;
            case NativeMethods.CF_TEXT: return AnsiText;
            case NativeMethods.CF_BITMAP: return "CF_BITMAP";
        }

        var buffer = new StringBuilder(256);
        var length = NativeMethods.GetClipboardFormatName(id, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : $"CF_{id}";
    }

    private static uint Register(string name)
    {
        lock (Gate)
        {
            if (RegisteredCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var id = NativeMethods.RegisterClipboardFormat(name);
            RegisteredCache[name] = id;
            return id;
        }
    }

    public static bool IsImage(string format) =>
        ImageFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    public static bool IsText(string format) =>
        TextFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    public static bool IsFile(string format) =>
        FileFormats.Contains(format, StringComparer.OrdinalIgnoreCase);
}

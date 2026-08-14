using System.IO;
using System.Text;
using Wincy.Services;

namespace Wincy.Models;

/// <summary>A single entry in the clipboard history, with all of its representations.</summary>
public sealed class ClipItem
{
    /// <summary>
    /// Letters offered as pin shortcuts. Mirrors Maccy's list, minus the keys
    /// Windows conventions reserve inside the popup:
    /// a = select all, q = quit, v = paste, w = close, z = undo.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedPins =
    [
        "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l",
        "m", "n", "o", "p", "r", "s", "t", "u", "x", "y"
    ];

    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Full path of the process that owned the clipboard when this was copied.</summary>
    public string? Application { get; set; }

    public DateTime FirstCopiedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastCopiedAt { get; set; } = DateTime.UtcNow;

    public int NumberOfCopies { get; set; } = 1;

    /// <summary>Single character pin shortcut, or null when unpinned.</summary>
    public string? Pin { get; set; }

    public List<ClipContent> Contents { get; set; } = [];

    public bool IsPinned => !string.IsNullOrEmpty(Pin);

    public ClipItem()
    {
    }

    public ClipItem(IEnumerable<ClipContent> contents)
    {
        Contents = [.. contents];
    }

    // ------------------------------------------------------------- projections

    /// <summary>
    /// Bytes for the first of the given formats that this item carries. The emptiness
    /// check uses the recorded length rather than the bytes, so a miss costs no read.
    /// </summary>
    public byte[]? Data(params string[] formats)
    {
        foreach (var format in formats)
        {
            var content = Contents.FirstOrDefault(c =>
                string.Equals(c.Format, format, StringComparison.OrdinalIgnoreCase) && c.HasValue);

            if (content is not null)
            {
                return content.Value;
            }
        }

        return null;
    }

    public bool Has(string format) => Contents.Any(c =>
        string.Equals(c.Format, format, StringComparison.OrdinalIgnoreCase));

    public string? Text
    {
        get
        {
            var data = Data(ClipFormats.UnicodeText);
            return data is null ? null : DecodeUtf16(data);
        }
    }

    public byte[]? HtmlData => Data(ClipFormats.Html);

    public string? Html
    {
        get
        {
            var data = HtmlData;
            return data is null ? null : HtmlClipboardFormat.ExtractPlainText(Encoding.UTF8.GetString(data));
        }
    }

    public byte[]? RtfData => Data(ClipFormats.Rtf);

    public string? Rtf
    {
        get
        {
            var data = RtfData;
            return data is null ? null : RtfFormat.ExtractPlainText(Encoding.ASCII.GetString(data));
        }
    }

    public byte[]? ImageData => Data(ClipFormats.Png, ClipFormats.Dib, ClipFormats.DibV5);

    /// <summary>
    /// Answered from the format list alone. Asking <see cref="ImageData"/> would pull the
    /// whole blob out of the database just to decide whether a row shows a thumbnail.
    /// </summary>
    public bool HasImage => Contents.Any(c => ClipFormats.IsImage(c.Format) && c.HasValue);

    public IReadOnlyList<string> FileNames
    {
        get
        {
            var data = Data(ClipFormats.Drop);
            return data is null ? [] : DropFiles.ParsePaths(data);
        }
    }

    /// <summary>Text used for the preview pane and for title generation.</summary>
    public string PreviewableText
    {
        get
        {
            var files = FileNames;
            if (files.Count > 0)
            {
                return string.Join("\n", files);
            }

            var text = Text;
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }

            var rtf = Rtf;
            if (!string.IsNullOrEmpty(rtf))
            {
                return rtf;
            }

            var html = Html;
            if (!string.IsNullOrEmpty(html))
            {
                return html;
            }

            return Title;
        }
    }

    public string? ApplicationName =>
        string.IsNullOrEmpty(Application) ? null : Path.GetFileNameWithoutExtension(Application);

    // ------------------------------------------------------------- behaviour

    /// <summary>
    /// True when this item contains everything <paramref name="other"/> does, ignoring
    /// bookkeeping formats. Used to collapse duplicate copies, as Maccy's supersedes does.
    /// </summary>
    public bool Supersedes(ClipItem other)
    {
        var meaningful = other.Contents
            .Where(c => !ClipFormats.NonIdentifyingFormats.Contains(c.Format))
            .ToList();

        if (meaningful.Count == 0)
        {
            return false;
        }

        return meaningful.All(candidate => Contents.Any(candidate.HasSameValue));
    }

    /// <summary>
    /// Builds the single-line title shown in the list. Images get an empty title
    /// (the row renders a thumbnail instead).
    /// </summary>
    public string GenerateTitle(bool showSpecialSymbols)
    {
        if (HasImage)
        {
            return string.Empty;
        }

        // 1k characters is the same performance trade-off Maccy makes.
        var title = PreviewableText;
        if (title.Length > 1000)
        {
            title = title[..1000];
        }

        if (showSpecialSymbols)
        {
            var leading = title.Length - title.TrimStart(' ').Length;
            var trailing = title.Length - title.TrimEnd(' ').Length;

            var builder = new StringBuilder(title.Length);
            for (var i = 0; i < title.Length; i++)
            {
                var c = title[i];
                if (c == ' ' && (i < leading || i >= title.Length - trailing))
                {
                    builder.Append('·');
                }
                else
                {
                    builder.Append(c switch
                    {
                        '\r' => '⏎',
                        '\n' => '⏎',
                        '\t' => '⇥',
                        _ => c
                    });
                }
            }

            // Collapse the CRLF pair into a single return glyph.
            return builder.ToString().Replace("⏎⏎", "⏎");
        }

        return title.Trim();
    }

    private static string DecodeUtf16(byte[] data)
    {
        var text = Encoding.Unicode.GetString(data);
        var terminator = text.IndexOf('\0');
        return terminator >= 0 ? text[..terminator] : text;
    }
}

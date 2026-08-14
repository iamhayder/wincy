using System.Globalization;
using System.Text;

namespace Wincy.Services;

/// <summary>Parses the CF_HDROP payload into file paths, and builds one back.</summary>
public static class DropFiles
{
    public static IReadOnlyList<string> ParsePaths(byte[] data)
    {
        if (data.Length < 20)
        {
            return [];
        }

        // DROPFILES: pFiles (offset to the name list), POINT pt, BOOL fNC, BOOL fWide
        var offset = BitConverter.ToUInt32(data, 0);
        var wide = BitConverter.ToInt32(data, 16) != 0;

        if (offset >= data.Length)
        {
            return [];
        }

        var payload = data.AsSpan((int)offset);
        var text = wide
            ? Encoding.Unicode.GetString(payload)
            : Encoding.Default.GetString(payload);

        return [.. text.Split('\0', StringSplitOptions.RemoveEmptyEntries)];
    }

    public static byte[] Build(IEnumerable<string> paths)
    {
        var list = new StringBuilder();
        foreach (var path in paths)
        {
            list.Append(path).Append('\0');
        }

        list.Append('\0');

        var names = Encoding.Unicode.GetBytes(list.ToString());
        var header = 20; // sizeof(DROPFILES)
        var buffer = new byte[header + names.Length];

        BitConverter.GetBytes((uint)header).CopyTo(buffer, 0);   // pFiles
        BitConverter.GetBytes(0).CopyTo(buffer, 4);              // pt.x
        BitConverter.GetBytes(0).CopyTo(buffer, 8);              // pt.y
        BitConverter.GetBytes(0).CopyTo(buffer, 12);             // fNC
        BitConverter.GetBytes(1).CopyTo(buffer, 16);             // fWide
        names.CopyTo(buffer, header);

        return buffer;
    }
}

/// <summary>
/// The "HTML Format" clipboard payload is an HTML fragment wrapped in a header of
/// byte offsets. We only need the readable text out of it for titles and previews.
/// </summary>
public static class HtmlClipboardFormat
{
    public static string ExtractPlainText(string payload)
    {
        var fragment = ExtractFragment(payload);
        return StripTags(fragment);
    }

    public static string ExtractFragment(string payload)
    {
        var start = payload.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        var end = payload.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);

        if (start >= 0 && end > start)
        {
            start += "<!--StartFragment-->".Length;
            return payload[start..end];
        }

        // Fall back to skipping the offset header, which ends at the first '<'.
        var body = payload.IndexOf('<');
        return body >= 0 ? payload[body..] : payload;
    }

    private static string StripTags(string html)
    {
        var builder = new StringBuilder(html.Length);
        var inTag = false;

        foreach (var c in html)
        {
            switch (c)
            {
                case '<':
                    inTag = true;
                    break;
                case '>':
                    inTag = false;
                    builder.Append(' ');
                    break;
                default:
                    if (!inTag)
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return DecodeEntities(builder.ToString()).Trim();
    }

    private static string DecodeEntities(string text)
    {
        if (!text.Contains('&'))
        {
            return text;
        }

        return text
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'");
    }

    /// <summary>Wraps an HTML fragment in the header Windows expects when writing back.</summary>
    public static byte[] Build(string fragment)
    {
        const string headerTemplate =
            "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";

        var pre = "<html><body>\r\n<!--StartFragment-->";
        var post = "<!--EndFragment-->\r\n</body></html>";
        var headerLength = string.Format(CultureInfo.InvariantCulture, headerTemplate, 0, 0, 0, 0).Length;

        var startHtml = headerLength;
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(pre);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(post);

        var header = string.Format(
            CultureInfo.InvariantCulture, headerTemplate, startHtml, endHtml, startFragment, endFragment);

        return Encoding.UTF8.GetBytes(header + pre + fragment + post);
    }
}

/// <summary>Minimal RTF reader: enough to recover the plain text for titles and previews.</summary>
public static class RtfFormat
{
    public static string ExtractPlainText(string rtf)
    {
        var builder = new StringBuilder(rtf.Length);
        var depth = 0;
        var skipDepth = int.MaxValue;
        var i = 0;

        while (i < rtf.Length)
        {
            var c = rtf[i];

            if (c == '{')
            {
                depth++;
                i++;
                continue;
            }

            if (c == '}')
            {
                if (depth <= skipDepth)
                {
                    skipDepth = int.MaxValue;
                }

                depth--;
                i++;
                continue;
            }

            if (c == '\\')
            {
                i++;
                if (i >= rtf.Length)
                {
                    break;
                }

                // Escaped literal characters.
                if (rtf[i] is '\\' or '{' or '}')
                {
                    if (skipDepth == int.MaxValue)
                    {
                        builder.Append(rtf[i]);
                    }

                    i++;
                    continue;
                }

                // \'hh — a byte in the current code page.
                if (rtf[i] == '\'' && i + 2 < rtf.Length)
                {
                    if (skipDepth == int.MaxValue &&
                        byte.TryParse(rtf.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                    {
                        builder.Append((char)value);
                    }

                    i += 3;
                    continue;
                }

                var wordStart = i;
                while (i < rtf.Length && char.IsLetter(rtf[i]))
                {
                    i++;
                }

                var word = rtf[wordStart..i];

                var numberStart = i;
                if (i < rtf.Length && (rtf[i] == '-' || char.IsDigit(rtf[i])))
                {
                    i++;
                    while (i < rtf.Length && char.IsDigit(rtf[i]))
                    {
                        i++;
                    }
                }

                var parameter = numberStart == i ? null : rtf[numberStart..i];

                if (i < rtf.Length && rtf[i] == ' ')
                {
                    i++;
                }

                switch (word)
                {
                    case "par" or "line" or "row":
                        if (skipDepth == int.MaxValue)
                        {
                            builder.Append('\n');
                        }

                        break;
                    case "tab":
                        if (skipDepth == int.MaxValue)
                        {
                            builder.Append('\t');
                        }

                        break;
                    case "u" when parameter is not null && int.TryParse(parameter, out var code):
                        if (skipDepth == int.MaxValue)
                        {
                            builder.Append((char)(code < 0 ? code + 65536 : code));
                        }

                        // \uN is followed by a fallback character we must swallow.
                        if (i < rtf.Length && rtf[i] == '?')
                        {
                            i++;
                        }

                        break;
                    // Groups whose content is metadata, not text.
                    case "fonttbl" or "colortbl" or "stylesheet" or "info" or "generator"
                        or "pict" or "themedata" or "colorschememapping" or "datastore" or "listtable"
                        or "listoverridetable" or "rsidtbl" or "xmlnstbl" or "mmathPr":
                        skipDepth = Math.Min(skipDepth, depth);
                        break;
                }

                continue;
            }

            if (c is not ('\r' or '\n'))
            {
                if (skipDepth == int.MaxValue)
                {
                    builder.Append(c);
                }
            }

            i++;
        }

        return builder.ToString().Trim();
    }
}

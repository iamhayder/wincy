using System.Text;
using Wincy.Services;
using Xunit;

namespace Wincy.Tests;

public class DropFilesTests
{
    [Fact]
    public void RoundTripsFilePaths()
    {
        string[] paths = [@"C:\a\one.txt", @"C:\a\two with space.png"];

        var parsed = DropFiles.ParsePaths(DropFiles.Build(paths));

        Assert.Equal(paths, parsed);
    }

    [Fact]
    public void RoundTripsUnicodePaths()
    {
        string[] paths = [@"C:\naïve\файл.txt", @"C:\日本語\画像.png"];

        var parsed = DropFiles.ParsePaths(DropFiles.Build(paths));

        Assert.Equal(paths, parsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(19)]
    public void RejectsTruncatedPayloads(int length) =>
        Assert.Empty(DropFiles.ParsePaths(new byte[length]));

    [Fact]
    public void RejectsAnOutOfRangeOffset()
    {
        // pFiles points past the end of the buffer.
        var data = new byte[24];
        BitConverter.GetBytes(9999u).CopyTo(data, 0);

        Assert.Empty(DropFiles.ParsePaths(data));
    }
}

public class HtmlClipboardFormatTests
{
    [Fact]
    public void BuildsAPayloadItsOwnParserUnderstands()
    {
        var payload = Encoding.UTF8.GetString(HtmlClipboardFormat.Build("<b>Hello</b>"));

        Assert.Contains("<b>Hello</b>", HtmlClipboardFormat.ExtractFragment(payload));
    }

    [Fact]
    public void StripsTagsAndDecodesEntities()
    {
        var payload = Encoding.UTF8.GetString(HtmlClipboardFormat.Build("<b>Hello</b> &amp; welcome"));

        var text = HtmlClipboardFormat.ExtractPlainText(payload);

        Assert.Contains("Hello", text);
        Assert.Contains("&", text);
        Assert.DoesNotContain("<b>", text);
    }

    [Fact]
    public void HandlesPayloadsWithoutFragmentMarkers()
    {
        // Some applications emit the offset header but no StartFragment comment.
        const string raw = "Version:0.9\r\nStartHTML:000000097\r\nEndHTML:000000200\r\n" +
                           "<html><body><p>Plain paragraph</p></body></html>";

        Assert.Contains("Plain paragraph", HtmlClipboardFormat.ExtractPlainText(raw));
    }

    [Fact]
    public void DoesNotLeakTheOffsetHeaderIntoTheText()
    {
        var payload = Encoding.UTF8.GetString(HtmlClipboardFormat.Build("<p>Body</p>"));

        var text = HtmlClipboardFormat.ExtractPlainText(payload);

        Assert.DoesNotContain("StartHTML", text);
        Assert.DoesNotContain("Version:", text);
    }
}

public class RtfFormatTests
{
    private const string Sample =
        @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Calibri;}}" +
        @"{\colortbl ;\red255\green0\blue0;}" +
        @"\f0\fs22 Hello \b world\b0\par Second line\par}";

    [Fact]
    public void RecoversTheVisibleText()
    {
        var text = RtfFormat.ExtractPlainText(Sample);

        Assert.Contains("Hello", text);
        Assert.Contains("world", text);
        Assert.Contains("Second line", text);
    }

    [Fact]
    public void DiscardsMetadataGroups()
    {
        var text = RtfFormat.ExtractPlainText(Sample);

        Assert.DoesNotContain("Calibri", text);
        Assert.DoesNotContain("red255", text);
    }

    [Fact]
    public void TurnsParagraphBreaksIntoNewlines() =>
        Assert.Contains('\n', RtfFormat.ExtractPlainText(Sample));

    [Fact]
    public void KeepsEscapedBraces() =>
        Assert.Equal("a{b}c", RtfFormat.ExtractPlainText(@"{\rtf1 a\{b\}c}"));

    [Fact]
    public void DecodesHexEscapes() =>
        Assert.Equal("café", RtfFormat.ExtractPlainText(@"{\rtf1 caf\'e9}"));

    [Fact]
    public void DecodesUnicodeEscapesAndSwallowsTheFallbackCharacter() =>
        Assert.Equal("\u2603", RtfFormat.ExtractPlainText(@"{\rtf1 \u9731?}"));

    [Fact]
    public void SurvivesAnUnterminatedControlWord() =>
        Assert.Equal(string.Empty, RtfFormat.ExtractPlainText(@"{\rtf1 \"));
}

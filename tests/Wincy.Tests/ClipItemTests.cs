using System.Text;
using Wincy.Models;
using Wincy.Services;
using Xunit;

namespace Wincy.Tests;

public class ClipItemTests
{
    private static ClipItem Text(string value) =>
        new([new ClipContent(ClipFormats.UnicodeText, Encoding.Unicode.GetBytes(value))]);

    [Fact]
    public void ReadsBackItsOwnText() =>
        Assert.Equal("hello", Text("hello").Text);

    [Fact]
    public void IdenticalCopiesSupersedeEachOther()
    {
        var first = Text("same");
        var second = Text("same");

        Assert.True(first.Supersedes(second));
        Assert.True(second.Supersedes(first));
    }

    [Fact]
    public void DifferentCopiesDoNot()
    {
        Assert.False(Text("one").Supersedes(Text("two")));
    }

    [Fact]
    public void BookkeepingFormatsAreIgnoredWhenComparing()
    {
        // A copy Wincy put back carries its own marker. That must not stop it
        // matching the stored original, or every paste would create a duplicate.
        var stored = Text("same");
        var reCopied = Text("same");
        reCopied.Contents.Add(new ClipContent(ClipFormats.WincySource, [1]));

        Assert.True(stored.Supersedes(reCopied));
    }

    [Fact]
    public void AnItemWithNoMeaningfulContentSupersedesNothing()
    {
        var marker = new ClipItem([new ClipContent(ClipFormats.WincySource, [1])]);

        Assert.False(Text("anything").Supersedes(marker));
    }

    [Fact]
    public void TitleShowsSpecialSymbolsWhenAsked()
    {
        var title = Text("a\tb\nc").GenerateTitle(showSpecialSymbols: true);

        Assert.Contains('⇥', title);
        Assert.Contains('⏎', title);
    }

    [Fact]
    public void TitleIsTrimmedWhenSymbolsAreOff()
    {
        var title = Text("  padded  ").GenerateTitle(showSpecialSymbols: false);

        Assert.Equal("padded", title);
    }

    [Fact]
    public void LeadingAndTrailingSpacesBecomeVisibleDots()
    {
        var title = Text("  x  ").GenerateTitle(showSpecialSymbols: true);

        Assert.StartsWith("··", title);
        Assert.EndsWith("··", title);
    }

    [Fact]
    public void TitleIsCappedForPerformance()
    {
        var title = Text(new string('x', 5000)).GenerateTitle(showSpecialSymbols: false);

        Assert.True(title.Length <= 1000);
    }

    [Fact]
    public void FilePathsAreRecovered()
    {
        string[] paths = [@"C:\a.txt", @"C:\b.txt"];
        var item = new ClipItem([new ClipContent(ClipFormats.Drop, DropFiles.Build(paths))]);

        Assert.Equal(paths, item.FileNames);
        Assert.Equal(string.Join("\n", paths), item.PreviewableText);
    }

    [Fact]
    public void HasImageDoesNotDependOnReadingTheBlob()
    {
        var item = new ClipItem([new ClipContent(ClipFormats.Png, [1, 2, 3])]);

        Assert.True(item.HasImage);
        Assert.Empty(item.GenerateTitle(showSpecialSymbols: false));
    }
}

public class ClipContentTests
{
    [Fact]
    public void HashesAreComparedInsteadOfBlobs()
    {
        var a = new ClipContent(ClipFormats.UnicodeText, [1, 2, 3]);
        var b = new ClipContent(ClipFormats.UnicodeText, [1, 2, 3]);

        Assert.NotNull(a.Hash);
        Assert.Equal(a.Hash, b.Hash);
        Assert.True(a.HasSameValue(b));
    }

    [Fact]
    public void DifferentBytesProduceDifferentHashes()
    {
        var a = new ClipContent(ClipFormats.UnicodeText, [1, 2, 3]);
        var b = new ClipContent(ClipFormats.UnicodeText, [3, 2, 1]);

        Assert.NotEqual(a.Hash, b.Hash);
        Assert.False(a.HasSameValue(b));
    }

    [Fact]
    public void FormatIsPartOfIdentity()
    {
        var a = new ClipContent(ClipFormats.UnicodeText, [1]);
        var b = new ClipContent(ClipFormats.Rtf, [1]);

        Assert.False(a.HasSameValue(b));
    }

    [Fact]
    public void DeferredValuesAreFetchedOnDemandExactlyOnce()
    {
        var reads = 0;
        var content = new ClipContent { Id = 7, Format = ClipFormats.UnicodeText, Length = 3 };
        content.DeferValue(id =>
        {
            reads++;
            Assert.Equal(7, id);
            return [1, 2, 3];
        });

        Assert.Equal(0, reads);
        Assert.Equal<byte[]?>([1, 2, 3], content.Value);
        _ = content.Value;
        Assert.Equal(1, reads);
    }
}

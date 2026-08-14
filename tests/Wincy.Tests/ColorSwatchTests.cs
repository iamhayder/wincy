using System.Windows.Media;
using Wincy.Services;
using Xunit;

namespace Wincy.Tests;

public class ColorSwatchTests
{
    [Theory]
    [InlineData("#FF8800", 0xFF, 0x88, 0x00)]
    [InlineData("#f80", 0xFF, 0x88, 0x00)]
    [InlineData("rgb(255, 136, 0)", 0xFF, 0x88, 0x00)]
    public void ParsesColours(string input, byte r, byte g, byte b)
    {
        var parsed = ColorSwatch.Parse(input);

        Assert.NotNull(parsed);
        Assert.Equal(Color.FromRgb(r, g, b), parsed!.Value);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("#zzzzzz")]
    [InlineData("#12345")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsAnythingElse(string? input) =>
        Assert.Null(ColorSwatch.Parse(input));
}

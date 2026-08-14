using Wincy.Models;
using Wincy.Services;
using Xunit;

namespace Wincy.Tests;

public class SorterTests
{
    private static ClipItem At(DateTime when, int copies = 1, string? pin = null) =>
        new() { LastCopiedAt = when, FirstCopiedAt = when, NumberOfCopies = copies, Pin = pin };

    [Fact]
    public void SortsMostRecentFirst()
    {
        var settings = new AppSettings { SortBy = SortBy.LastCopiedAt };
        var older = At(new DateTime(2026, 1, 1));
        var newer = At(new DateTime(2026, 6, 1));

        var sorted = new Sorter(settings).Sort([older, newer]);

        Assert.Same(newer, sorted[0]);
    }

    [Fact]
    public void SortsByCopyCountWhenAsked()
    {
        var settings = new AppSettings { SortBy = SortBy.NumberOfCopies };
        var once = At(new DateTime(2026, 6, 1));
        var often = At(new DateTime(2026, 1, 1), copies: 9);

        var sorted = new Sorter(settings).Sort([once, often]);

        Assert.Same(often, sorted[0]);
    }

    [Fact]
    public void PinnedItemsFloatToTheTop()
    {
        var settings = new AppSettings { PinTo = PinsPosition.Top };
        var recent = At(new DateTime(2026, 6, 1));
        var pinned = At(new DateTime(2020, 1, 1), pin: "b");

        var sorted = new Sorter(settings).Sort([recent, pinned]);

        Assert.Same(pinned, sorted[0]);
    }

    [Fact]
    public void PinnedItemsCanSinkToTheBottomInstead()
    {
        var settings = new AppSettings { PinTo = PinsPosition.Bottom };
        var recent = At(new DateTime(2026, 6, 1));
        var pinned = At(new DateTime(2020, 1, 1), pin: "b");

        var sorted = new Sorter(settings).Sort([recent, pinned]);

        Assert.Same(pinned, sorted[^1]);
    }
}

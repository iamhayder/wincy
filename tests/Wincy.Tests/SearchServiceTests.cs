using Wincy.Services;
using Xunit;

namespace Wincy.Tests;

public class SearchServiceTests
{
    private static readonly string[] Items =
    [
        "https://example.com/very/long/path",
        "SELECT * FROM users WHERE id = 1",
        "hello world",
        "Hello there",
        "#FF8800"
    ];

    private readonly SearchService _search = new();

    private static string Title(string value) => value;

    private List<SearchResult<string>> Run(string query, SearchMode mode) =>
        _search.Search(query, Items, Title, mode);

    [Fact]
    public void EmptyQueryReturnsEverything() =>
        Assert.Equal(Items.Length, Run(string.Empty, SearchMode.Exact).Count);

    [Fact]
    public void ExactIsCaseInsensitive() =>
        Assert.Equal(2, Run("hello", SearchMode.Exact).Count);

    [Fact]
    public void ExactReportsTheMatchedRange()
    {
        var results = Run("hello", SearchMode.Exact);

        Assert.All(results, result =>
        {
            var range = Assert.Single(result.Ranges);
            var matched = result.Item.Substring(range.Start, range.Length);
            Assert.Equal("hello", matched, ignoreCase: true);
        });
    }

    [Fact]
    public void ExactReturnsNothingForAMiss() =>
        Assert.Empty(Run("zzzz", SearchMode.Exact));

    [Fact]
    public void RegexMatches()
    {
        var result = Assert.Single(Run("^SELECT.*users", SearchMode.Regex));

        Assert.StartsWith("SELECT", result.Item);
    }

    [Fact]
    public void AHalfTypedRegexDoesNotThrow() =>
        Assert.Empty(Run("([", SearchMode.Regex));

    [Fact]
    public void FuzzyMatchesNonAdjacentCharacters() =>
        Assert.Contains(Run("hlwrld", SearchMode.Fuzzy), r => r.Item == "hello world");

    [Fact]
    public void FuzzyReturnsBestMatchFirst()
    {
        var results = Run("exam", SearchMode.Fuzzy);

        Assert.NotEmpty(results);
        Assert.Contains("example", results[0].Item);
    }

    [Fact]
    public void FuzzyScoresAreAscending()
    {
        var results = Run("e", SearchMode.Fuzzy);

        Assert.All(
            results.Zip(results.Skip(1)),
            pair => Assert.True(pair.First.Score <= pair.Second.Score));
    }

    [Fact]
    public void MixedPrefersExactWhenItHits() =>
        Assert.Equal(2, Run("hello", SearchMode.Mixed).Count);

    [Fact]
    public void MixedFallsBackToFuzzy() =>
        Assert.Contains(Run("hlwrld", SearchMode.Mixed), r => r.Item == "hello world");

    [Theory]
    [InlineData(SearchMode.Exact)]
    [InlineData(SearchMode.Regex)]
    [InlineData(SearchMode.Fuzzy)]
    [InlineData(SearchMode.Mixed)]
    public void RangesStayInsideTheTitle(SearchMode mode)
    {
        // The row renderer slices the title by these ranges; an out-of-bounds
        // range would throw during layout rather than at search time.
        foreach (var result in Run("e", mode))
        {
            Assert.All(result.Ranges, range =>
            {
                Assert.True(range.Start >= 0);
                Assert.True(range.End <= result.Item.Length);
            });
        }
    }
}

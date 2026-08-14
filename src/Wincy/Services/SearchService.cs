using System.Text.RegularExpressions;

namespace Wincy.Services;

/// <summary>A half-open range of characters in a title, used to highlight matches.</summary>
public readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed class SearchResult<T>(T item, double score, IReadOnlyList<TextRange> ranges)
{
    public T Item { get; } = item;

    /// <summary>Lower is better. Zero for non-fuzzy matches.</summary>
    public double Score { get; } = score;

    public IReadOnlyList<TextRange> Ranges { get; } = ranges;
}

/// <summary>
/// The four search modes Maccy offers: exact, fuzzy, regex, and mixed (which tries
/// each in turn and keeps the first that finds anything).
/// </summary>
public sealed class SearchService
{
    private static readonly IReadOnlyList<TextRange> NoRanges = [];

    /// <summary>Longer titles are truncated before fuzzy scoring, which is O(n·m).</summary>
    private const int FuzzyLimit = 5_000;

    public List<SearchResult<T>> Search<T>(string query, IReadOnlyList<T> items, Func<T, string> titleOf, SearchMode mode)
    {
        if (string.IsNullOrEmpty(query))
        {
            return [.. items.Select(item => new SearchResult<T>(item, 0, NoRanges))];
        }

        return mode switch
        {
            SearchMode.Fuzzy => Fuzzy(query, items, titleOf),
            SearchMode.Regex => Simple(query, items, titleOf, regex: true),
            SearchMode.Mixed => Mixed(query, items, titleOf),
            _ => Simple(query, items, titleOf, regex: false)
        };
    }

    private List<SearchResult<T>> Mixed<T>(string query, IReadOnlyList<T> items, Func<T, string> titleOf)
    {
        var results = Simple(query, items, titleOf, regex: false);
        if (results.Count > 0)
        {
            return results;
        }

        results = Simple(query, items, titleOf, regex: true);
        if (results.Count > 0)
        {
            return results;
        }

        return Fuzzy(query, items, titleOf);
    }

    private static List<SearchResult<T>> Simple<T>(
        string query, IReadOnlyList<T> items, Func<T, string> titleOf, bool regex)
    {
        var results = new List<SearchResult<T>>();

        Regex? compiled = null;
        if (regex)
        {
            try
            {
                compiled = new Regex(query, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                // A half-typed pattern is not an error; it just matches nothing yet.
                return results;
            }
        }

        foreach (var item in items)
        {
            var title = titleOf(item);
            if (title.Length == 0)
            {
                continue;
            }

            if (compiled is not null)
            {
                Match match;
                try
                {
                    match = compiled.Match(title);
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }

                if (match.Success)
                {
                    results.Add(new SearchResult<T>(item, 0, [new TextRange(match.Index, match.Length)]));
                }
            }
            else
            {
                var index = title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
                if (index >= 0)
                {
                    results.Add(new SearchResult<T>(item, 0, [new TextRange(index, query.Length)]));
                }
            }
        }

        return results;
    }

    private static List<SearchResult<T>> Fuzzy<T>(string query, IReadOnlyList<T> items, Func<T, string> titleOf)
    {
        var results = new List<SearchResult<T>>();

        foreach (var item in items)
        {
            var title = titleOf(item);
            if (title.Length == 0)
            {
                continue;
            }

            if (title.Length > FuzzyLimit)
            {
                title = title[..FuzzyLimit];
            }

            if (TryFuzzyMatch(query, title, out var score, out var ranges))
            {
                results.Add(new SearchResult<T>(item, score, ranges));
            }
        }

        results.Sort((a, b) => a.Score.CompareTo(b.Score));
        return results;
    }

    /// <summary>
    /// Greedy subsequence match with positional bonuses, in the spirit of Fuse.
    /// Consecutive characters and matches at word boundaries score better; a match
    /// spread thinly across a long title scores worse.
    /// </summary>
    internal static bool TryFuzzyMatch(string query, string text, out double score, out IReadOnlyList<TextRange> ranges)
    {
        score = double.MaxValue;
        ranges = NoRanges;

        if (query.Length == 0 || query.Length > text.Length)
        {
            return false;
        }

        var matches = new List<int>(query.Length);
        var textIndex = 0;

        foreach (var target in query)
        {
            if (char.IsWhiteSpace(target))
            {
                continue;
            }

            var found = -1;
            for (var i = textIndex; i < text.Length; i++)
            {
                if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(target))
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                return false;
            }

            matches.Add(found);
            textIndex = found + 1;
        }

        if (matches.Count == 0)
        {
            return false;
        }

        // Penalise gaps between matched characters and a late first match; reward
        // matches that begin a word.
        var penalty = 0.0;
        for (var i = 1; i < matches.Count; i++)
        {
            var gap = matches[i] - matches[i - 1] - 1;
            penalty += gap == 0 ? 0 : 1 + (gap * 0.05);
        }

        var first = matches[0];
        var startsWord = first == 0 || !char.IsLetterOrDigit(text[first - 1]);
        penalty += first * 0.02;
        if (!startsWord)
        {
            penalty += 0.5;
        }

        // Normalise so that short, tight matches in short titles win.
        score = penalty / Math.Max(1, matches.Count) + (text.Length * 0.0005);

        ranges = ToRanges(matches);
        return true;
    }

    private static List<TextRange> ToRanges(List<int> indices)
    {
        var ranges = new List<TextRange>();
        var start = indices[0];
        var length = 1;

        for (var i = 1; i < indices.Count; i++)
        {
            if (indices[i] == indices[i - 1] + 1)
            {
                length++;
            }
            else
            {
                ranges.Add(new TextRange(start, length));
                start = indices[i];
                length = 1;
            }
        }

        ranges.Add(new TextRange(start, length));
        return ranges;
    }
}

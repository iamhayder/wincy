using Wincy.Models;

namespace Wincy.Services;

/// <summary>Orders history by the chosen criterion, then floats pins to the top or bottom.</summary>
public sealed class Sorter(AppSettings settings)
{
    public List<ClipItem> Sort(IEnumerable<ClipItem> items) => Sort(items, settings.SortBy, settings.PinTo);

    public List<ClipItem> Sort(IEnumerable<ClipItem> items, SortBy by, PinsPosition pinTo)
    {
        var ordered = by switch
        {
            SortBy.FirstCopiedAt => items.OrderByDescending(i => i.FirstCopiedAt),
            SortBy.NumberOfCopies => items.OrderByDescending(i => i.NumberOfCopies),
            _ => items.OrderByDescending(i => i.LastCopiedAt)
        };

        // A stable secondary ordering keeps pinned rows grouped without disturbing the
        // primary sort within each group.
        return pinTo == PinsPosition.Bottom
            ? [.. ordered.OrderBy(i => i.IsPinned ? 1 : 0)]
            : [.. ordered.OrderBy(i => i.IsPinned ? 0 : 1)];
    }

    /// <summary>Where a new or newly-pinned item belongs in an already-sorted list.</summary>
    public int IndexFor(ClipItem item, IReadOnlyList<ClipItem> sorted)
    {
        var combined = Sort(sorted.Where(i => i != item).Append(item));
        var index = combined.IndexOf(item);
        return index < 0 ? sorted.Count : index;
    }
}

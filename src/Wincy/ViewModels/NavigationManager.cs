using Wincy.Services;

namespace Wincy.ViewModels;

/// <summary>
/// Keyboard navigation over the popup's rows.
///
/// Pinned rows, unpinned rows and footer rows form a single chain, so Down from the
/// last history row lands on "Clear" rather than dead-ending — the same feel as Maccy.
/// </summary>
public sealed class NavigationManager(HistoryViewModel history, FooterViewModel footer, AppSettings settings)
    : ObservableObject
{
    private ClipItemViewModel? _selectedItem;
    private FooterItem? _selectedFooterItem;
    private readonly List<ClipItemViewModel> _multiSelection = [];

    /// <summary>Raised when a row should be scrolled into view.</summary>
    public event Action<ClipItemViewModel>? ScrollRequested;

    /// <summary>Raised when the lead selection changes, so the preview can follow.</summary>
    public event Action<ClipItemViewModel?>? SelectionChanged;

    /// <summary>
    /// True while the user is driving with the keyboard. Hover selection is suppressed
    /// until the mouse actually moves, so scrolling with the arrow keys does not
    /// hand the selection to whatever row happens to slide under the pointer.
    /// </summary>
    public bool IsKeyboardNavigating { get; set; } = true;

    /// <summary>Multi-selection is off by default, exactly as in Maccy.</summary>
    public bool MultiSelectionEnabled { get; set; }

    public ClipItemViewModel? SelectedItem => _selectedItem;

    public FooterItem? SelectedFooterItem => _selectedFooterItem;

    public IReadOnlyList<ClipItemViewModel> MultiSelection => _multiSelection;

    public bool IsMultiSelectInProgress => MultiSelectionEnabled && _multiSelection.Count > 1;

    public bool IsFirstItemHighlighted => Chain().FirstOrDefault() is ClipItemViewModel first &&
                                          ReferenceEquals(first, _selectedItem);

    /// <summary>Rows in visual order: pins and history in the configured order, then the footer.</summary>
    private IEnumerable<object> Chain()
    {
        if (settings.PinTo == PinsPosition.Top)
        {
            foreach (var item in history.PinnedItems) yield return item;
            foreach (var item in history.UnpinnedItems) yield return item;
        }
        else
        {
            foreach (var item in history.UnpinnedItems) yield return item;
            foreach (var item in history.PinnedItems) yield return item;
        }

        foreach (var item in footer.VisibleItems) yield return item;
    }

    private IEnumerable<ClipItemViewModel> HistoryChain() => Chain().OfType<ClipItemViewModel>();

    // ------------------------------------------------------------------ select

    public void Select(ClipItemViewModel? item, bool scroll = true)
    {
        ClearMultiSelection();

        if (_selectedItem is not null)
        {
            _selectedItem.SelectionIndex = -1;
        }

        footer.ClearSelection();
        _selectedFooterItem = null;

        _selectedItem = item;

        if (item is not null)
        {
            item.SelectionIndex = 0;
            _multiSelection.Add(item);

            if (scroll)
            {
                ScrollRequested?.Invoke(item);
            }
        }

        OnPropertyChanged(nameof(SelectedItem), nameof(SelectedFooterItem));
        SelectionChanged?.Invoke(item);
    }

    public void Select(FooterItem? item)
    {
        if (_selectedItem is not null)
        {
            _selectedItem.SelectionIndex = -1;
            _selectedItem = null;
        }

        ClearMultiSelection();
        footer.ClearSelection();

        _selectedFooterItem = item;
        if (item is not null)
        {
            item.IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedItem), nameof(SelectedFooterItem));
        SelectionChanged?.Invoke(null);
    }

    /// <summary>Ctrl+click behaviour: extends or shrinks the multi-selection.</summary>
    public void AddToSelection(ClipItemViewModel item)
    {
        if (!MultiSelectionEnabled)
        {
            Select(item);
            return;
        }

        if (_multiSelection.Contains(item))
        {
            if (_multiSelection.Count > 1)
            {
                _multiSelection.Remove(item);
                item.SelectionIndex = -1;
            }
        }
        else
        {
            _multiSelection.Add(item);
        }

        for (var i = 0; i < _multiSelection.Count; i++)
        {
            _multiSelection[i].SelectionIndex = i;
            _multiSelection[i].SetMultiSelectionBadge(
                IsMultiSelectInProgress ? (i + 1).ToString() : null);
        }

        _selectedItem = _multiSelection.LastOrDefault();
        OnPropertyChanged(nameof(SelectedItem));
        SelectionChanged?.Invoke(_selectedItem);
    }

    private void ClearMultiSelection()
    {
        foreach (var item in _multiSelection)
        {
            item.SelectionIndex = -1;
            item.SetMultiSelectionBadge(null);
        }

        _multiSelection.Clear();
    }

    // -------------------------------------------------------------- navigation

    public void HighlightFirst()
    {
        var first = HistoryChain().FirstOrDefault();
        if (first is not null)
        {
            Select(first);
        }
        else
        {
            Select(footer.VisibleItems.FirstOrDefault());
        }
    }

    public void HighlightLast()
    {
        var last = HistoryChain().LastOrDefault();
        if (last is not null)
        {
            Select(last);
        }
    }

    public void HighlightNext(bool allowCycle = false) => Move(1, allowCycle);

    public void HighlightPrevious(bool allowCycle = false) => Move(-1, allowCycle);

    private void Move(int delta, bool allowCycle)
    {
        var chain = Chain().ToList();
        if (chain.Count == 0)
        {
            return;
        }

        var current = (object?)_selectedItem ?? _selectedFooterItem;
        var index = current is null ? -1 : chain.FindIndex(row => ReferenceEquals(row, current));

        int next;
        if (index < 0)
        {
            next = delta > 0 ? 0 : chain.Count - 1;
        }
        else
        {
            next = index + delta;

            if (next < 0 || next >= chain.Count)
            {
                if (!allowCycle)
                {
                    return;
                }

                next = (next + chain.Count) % chain.Count;
            }
        }

        Apply(chain[next]);
    }

    private void Apply(object row)
    {
        switch (row)
        {
            case ClipItemViewModel item:
                Select(item);
                break;
            case FooterItem item:
                Select(item);
                break;
        }
    }

    /// <summary>
    /// Called after the visible set changes. Keeps the selection on the same row when
    /// possible, otherwise falls back to the top of the list.
    /// </summary>
    public void Reanchor()
    {
        if (_selectedItem is not null && _selectedItem.IsVisible &&
            Chain().Any(row => ReferenceEquals(row, _selectedItem)))
        {
            return;
        }

        if (_selectedFooterItem is { IsVisible: true })
        {
            return;
        }

        HighlightFirst();
    }

    /// <summary>Picks the row nearest to a deleted one, so deleting repeatedly stays fluid.</summary>
    public ClipItemViewModel? NearestTo(ClipItemViewModel item)
    {
        var chain = HistoryChain().ToList();
        var index = chain.FindIndex(row => ReferenceEquals(row, item));
        if (index < 0)
        {
            return null;
        }

        if (index + 1 < chain.Count)
        {
            return chain[index + 1];
        }

        return index > 0 ? chain[index - 1] : null;
    }
}

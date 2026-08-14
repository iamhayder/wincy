using System.Collections.ObjectModel;
using System.Windows.Threading;
using Wincy.Interop;
using Wincy.Models;
using Wincy.Services;

namespace Wincy.ViewModels;

/// <summary>
/// The clipboard history: loading, deduplicating, searching, pinning and re-copying.
/// This is the Windows counterpart of Maccy's History observable.
/// </summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly HistoryStore _store;
    private readonly ClipboardService _clipboard;
    private readonly AppIconCache _icons;
    private readonly Notifier _notifier;
    private readonly Sorter _sorter;
    private readonly SearchService _search = new();
    private readonly DispatcherTimer _searchThrottle;

    private string _searchQuery = string.Empty;
    private bool _isLoaded;

    /// <summary>Every item, including ones hidden by the current search.</summary>
    public List<ClipItemViewModel> All { get; } = [];

    public ObservableCollection<ClipItemViewModel> PinnedItems { get; } = [];

    public ObservableCollection<ClipItemViewModel> UnpinnedItems { get; } = [];

    public PasteStack? PasteStack { get; private set; }

    /// <summary>Asks the popup to close, e.g. after an item is chosen.</summary>
    public event Action? CloseRequested;

    /// <summary>Asks the popup to recompute its height.</summary>
    public event Action? ResizeRequested;

    /// <summary>Asks the list to bring an item into view.</summary>
    public event Action<ClipItemViewModel>? ScrollRequested;

    /// <summary>Raised after the visible set changes, so navigation can re-anchor.</summary>
    public event Action? ItemsChanged;

    /// <summary>
    /// Performs the actual paste. Supplied by <see cref="AppState"/>, which knows which
    /// window to hand focus back to — synthesising Ctrl+V without that would deliver the
    /// keystroke to whatever happens to be in front.
    /// </summary>
    public Action? PasteAction { get; set; }

    public HistoryViewModel(
        AppSettings settings,
        HistoryStore store,
        ClipboardService clipboard,
        AppIconCache icons,
        Notifier notifier)
    {
        _settings = settings;
        _store = store;
        _clipboard = clipboard;
        _icons = icons;
        _notifier = notifier;
        _sorter = new Sorter(settings);

        _searchThrottle = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _searchThrottle.Tick += (_, _) =>
        {
            _searchThrottle.Stop();
            RunSearch();
        };
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value))
            {
                return;
            }

            _searchThrottle.Stop();
            _searchThrottle.Start();
        }
    }

    public bool IsEmpty => All.Count == 0;

    // -------------------------------------------------------------------- load

    public void Load()
    {
        All.Clear();

        var items = _sorter.Sort(_store.LoadAll());
        foreach (var item in items)
        {
            All.Add(new ClipItemViewModel(item, _settings, _icons));
        }

        _isLoaded = true;

        LimitSize(_settings.Size);
        Rebuild();

        Log.Info($"Loaded {All.Count} history items");
    }

    /// <summary>Re-sorts everything, for when the sort or pin-position setting changes.</summary>
    public void Resort()
    {
        if (!_isLoaded)
        {
            return;
        }

        var ordered = _sorter.Sort(All.Select(vm => vm.Item));
        var byItem = All.ToDictionary(vm => vm.Item);

        All.Clear();
        foreach (var item in ordered)
        {
            All.Add(byItem[item]);
        }

        RunSearch();
    }

    // --------------------------------------------------------------- recording

    public void Add(ClipItem item)
    {
        var existing = FindDuplicate(item);

        if (existing is not null)
        {
            // The stored copy already carries every representation of this one, so keep
            // its contents and just refresh the bookkeeping — this is what moves a
            // re-copied item back to the top of the list.
            existing.Item.LastCopiedAt = DateTime.UtcNow;
            existing.Item.NumberOfCopies++;

            if (!item.Has(ClipFormats.WincySource) && item.Application is not null)
            {
                existing.Item.Application = item.Application;
            }

            _store.UpdateMetadata(existing.Item);

            All.Remove(existing);
            All.Insert(Math.Min(_sorter.IndexFor(existing.Item, [.. All.Select(v => v.Item)]), All.Count), existing);
            existing.RefreshAppearance();

            RunSearch();
            ResizeRequested?.Invoke();
            return;
        }

        // Leave room for the item about to be added.
        LimitSize(_settings.Size - 1);

        _store.Insert(item);

        var viewModel = new ClipItemViewModel(item, _settings, _icons);
        var index = Math.Clamp(_sorter.IndexFor(item, [.. All.Select(v => v.Item)]), 0, All.Count);
        All.Insert(index, viewModel);

        _notifier.Recorded(item.Title);

        RunSearch();
        ResizeRequested?.Invoke();
    }

    /// <summary>
    /// An existing item that contains everything the new copy does. Bookkeeping formats
    /// are excluded from the comparison, so re-pasting through Wincy still matches.
    /// </summary>
    private ClipItemViewModel? FindDuplicate(ClipItem item) =>
        All.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate.Item, item) && candidate.Item.Supersedes(item));

    private void LimitSize(int maxSize)
    {
        if (maxSize < 1)
        {
            maxSize = 1;
        }

        var unpinned = All.Where(vm => vm.IsUnpinned).ToList();
        if (unpinned.Count <= maxSize)
        {
            return;
        }

        foreach (var extra in unpinned.Skip(maxSize).ToList())
        {
            _store.Delete(extra.Item);
            All.Remove(extra);
        }
    }

    // ------------------------------------------------------------------ search

    private void RunSearch()
    {
        var results = _search.Search(SearchQuery, All, vm => vm.Title, _settings.SearchMode);
        var matched = new HashSet<Guid>();

        foreach (var result in results)
        {
            result.Item.ApplyHighlight(SearchQuery, result.Ranges);
            matched.Add(result.Item.Id);
        }

        // Fuzzy search reorders by score. Preserve that order for the visible list while
        // leaving All in its stored order.
        var order = results.Select(r => r.Item).ToList();

        foreach (var item in All)
        {
            item.IsVisible = matched.Contains(item.Id);
            if (!item.IsVisible)
            {
                item.ApplyHighlight(string.Empty, []);
            }
        }

        Rebuild(order);
    }

    private void Rebuild(List<ClipItemViewModel>? visibleOrder = null)
    {
        List<ClipItemViewModel> visible = visibleOrder is { Count: > 0 }
            ? visibleOrder
            : [.. All.Where(vm => vm.IsVisible)];

        PinnedItems.Clear();
        UnpinnedItems.Clear();

        // Pins keep their stored order regardless of search scoring.
        foreach (var item in All.Where(vm => vm.IsPinned && vm.IsVisible))
        {
            PinnedItems.Add(item);
        }

        foreach (var item in visible.Where(vm => vm.IsUnpinned))
        {
            UnpinnedItems.Add(item);
        }

        UpdateShortcuts();

        OnPropertyChanged(nameof(IsEmpty));
        ItemsChanged?.Invoke();
    }

    public void UpdateShortcuts()
    {
        foreach (var item in PinnedItems)
        {
            item.Shortcuts = item.Item.Pin is { Length: > 0 } pin
                ? KeyShortcut.Create(pin, _settings.PasteByDefault)
                : [];
        }

        UpdateUnpinnedShortcuts();
    }

    private void UpdateUnpinnedShortcuts()
    {
        foreach (var item in UnpinnedItems)
        {
            item.Shortcuts = [];
        }

        // The first nine visible unpinned rows get Ctrl+1 … Ctrl+9.
        var index = 1;
        foreach (var item in UnpinnedItems.Take(9))
        {
            item.Shortcuts = KeyShortcut.Create(index.ToString(), _settings.PasteByDefault);
            index++;
        }
    }

    /// <summary>Propagates the held modifiers so every visible row can show the right badge.</summary>
    public void SetActiveModifiers(HotKeyModifiers modifiers)
    {
        foreach (var item in PinnedItems)
        {
            item.ActiveModifiers = modifiers;
        }

        foreach (var item in UnpinnedItems)
        {
            item.ActiveModifiers = modifiers;
        }
    }

    /// <summary>Finds the row bound to a shortcut character, for Ctrl+3 / Alt+B style activation.</summary>
    public ClipItemViewModel? FindByShortcut(string character) =>
        PinnedItems.Concat(UnpinnedItems)
            .FirstOrDefault(item => item.Shortcuts.Any(s =>
                string.Equals(s.Character, character, StringComparison.OrdinalIgnoreCase)));

    // ------------------------------------------------------------------ actions

    /// <summary>
    /// Copies (and optionally pastes) an item, following the modifier matrix.
    /// Mirrors Maccy's History.select.
    /// </summary>
    public void Select(ClipItemViewModel? item, HotKeyModifiers modifiers)
    {
        if (item is null)
        {
            return;
        }

        bool paste;
        bool removeFormatting;

        if (modifiers == HotKeyModifiers.None)
        {
            // Plain Enter or click: the two "by default" settings decide.
            paste = _settings.PasteByDefault;
            removeFormatting = _settings.RemoveFormattingByDefault;
        }
        else
        {
            switch (ItemActions.FromModifiers(modifiers, _settings))
            {
                case ItemAction.Copy:
                    paste = false;
                    removeFormatting = false;
                    break;
                case ItemAction.Paste:
                    paste = true;
                    removeFormatting = false;
                    break;
                case ItemAction.PasteWithoutFormatting:
                    paste = true;
                    removeFormatting = true;
                    break;
                default:
                    return; // Unrecognised combination: do nothing, as Maccy does.
            }
        }

        CloseRequested?.Invoke();
        _clipboard.Copy(item.Item, removeFormatting);
        _notifier.Reused(item.Title);

        if (paste)
        {
            PasteAction?.Invoke();
        }

        SearchQuery = string.Empty;
    }

    public void TogglePin(ClipItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Item.IsPinned)
        {
            item.Item.Pin = null;
        }
        else
        {
            var taken = All
                .Where(vm => vm.Item.Pin is { Length: > 0 })
                .Select(vm => vm.Item.Pin!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var available = ClipItem.SupportedPins.Where(p => !taken.Contains(p)).ToList();
            if (available.Count == 0)
            {
                Log.Warn("No pin shortcuts left; unpin something first");
                return;
            }

            item.Item.Pin = available[Random.Shared.Next(available.Count)];
        }

        _store.UpdateMetadata(item.Item);

        // Re-sort so the row jumps to the pinned section.
        var ordered = _sorter.Sort(All.Select(vm => vm.Item));
        var byItem = All.ToDictionary(vm => vm.Item);
        All.Clear();
        foreach (var stored in ordered)
        {
            All.Add(byItem[stored]);
        }

        item.RefreshAppearance();
        SearchQuery = string.Empty;
        RunSearch();

        if (item.IsUnpinned)
        {
            ScrollRequested?.Invoke(item);
        }

        ResizeRequested?.Invoke();
    }

    /// <summary>Reassigns an item's pin letter, used by the Pins settings pane.</summary>
    public void SetPin(ClipItemViewModel item, string pin)
    {
        if (string.IsNullOrEmpty(pin) || pin == item.Item.Pin)
        {
            return;
        }

        item.Item.Pin = pin;
        _store.UpdateMetadata(item.Item);
        UpdateShortcuts();
    }

    public void Delete(ClipItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _store.Delete(item.Item);
        All.Remove(item);

        RunSearch();
        ResizeRequested?.Invoke();
    }

    /// <summary>Clears unpinned items only.</summary>
    public void Clear()
    {
        _store.DeleteUnpinned();
        All.RemoveAll(vm => vm.IsUnpinned);

        _clipboard.ClearSystem();
        CloseRequested?.Invoke();

        RunSearch();
        ResizeRequested?.Invoke();
        Log.Info("Cleared unpinned history");
    }

    /// <summary>Clears everything, pins included.</summary>
    public void ClearAll()
    {
        _store.DeleteAll();
        All.Clear();

        _clipboard.ClearSystem();
        CloseRequested?.Invoke();

        RunSearch();
        ResizeRequested?.Invoke();
        Log.Info("Cleared all history");
    }

    /// <summary>Re-renders titles after the special-symbols setting changes.</summary>
    public void RefreshTitles()
    {
        foreach (var item in All)
        {
            item.RefreshTitle();
            // Persist, or the old rendering would come back on the next launch.
            _store.UpdateMetadata(item.Item);
        }
    }

    public void RefreshAppearance()
    {
        foreach (var item in All)
        {
            item.RefreshAppearance();
        }
    }

    // -------------------------------------------------------------- paste stack

    public void StartPasteStack(IReadOnlyList<ClipItemViewModel> items, HotKeyModifiers modifiers)
    {
        if (items.Count == 0)
        {
            return;
        }

        PasteStack = new PasteStack([.. items], modifiers);
        OnPropertyChanged(nameof(PasteStack));

        Log.Info($"Started a paste stack of {items.Count} items");
        Select(items[0], modifiers);
    }

    /// <summary>Advances the stack after a paste, putting the next item on the clipboard.</summary>
    public void AdvancePasteStack()
    {
        if (PasteStack is null)
        {
            return;
        }

        PasteStack.Items.RemoveAt(0);

        if (PasteStack.Items.Count == 0)
        {
            InterruptPasteStack();
            return;
        }

        var next = PasteStack.Items[0];
        var removeFormatting =
            ItemActions.FromModifiers(PasteStack.Modifiers, _settings) == ItemAction.PasteWithoutFormatting ||
            (PasteStack.Modifiers == HotKeyModifiers.None && _settings.RemoveFormattingByDefault);

        _clipboard.Copy(next.Item, removeFormatting);
        OnPropertyChanged(nameof(PasteStack));
    }

    public void InterruptPasteStack()
    {
        if (PasteStack is null)
        {
            return;
        }

        PasteStack = null;
        OnPropertyChanged(nameof(PasteStack));
        Log.Info("Paste stack interrupted");
    }

    /// <summary>Text of the most recent copy, for the tray tooltip.</summary>
    public string MostRecentText
    {
        get
        {
            var first = UnpinnedItems.FirstOrDefault() ?? PinnedItems.FirstOrDefault();
            if (first is null)
            {
                return string.Empty;
            }

            var text = first.Title.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 60 ? text : text[..60] + "…";
        }
    }
}

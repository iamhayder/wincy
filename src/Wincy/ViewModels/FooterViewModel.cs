using System.Collections.ObjectModel;
using Wincy.Interop;
using Wincy.Services;

namespace Wincy.ViewModels;

/// <summary>One row in the footer: Clear, Clear all, Preferences, About, Quit.</summary>
public sealed class FooterItem : ObservableObject
{
    private bool _isSelected;
    private bool _isVisible = true;
    private bool _showConfirmation;

    public Guid Id { get; } = Guid.NewGuid();

    public string Title { get; }

    public string? Tooltip { get; }

    public List<KeyShortcut> Shortcuts { get; }

    /// <summary>
    /// The footer's shortcut as individual keys, so the row can draw keycaps without a
    /// nested ItemsControl. Footer items never carry more than one shortcut.
    /// </summary>
    public IReadOnlyList<string> ShortcutParts =>
        Shortcuts.Count > 0 ? Shortcuts[0].Parts : [];

    public string? ConfirmationMessage { get; }

    public string? ConfirmationDetail { get; }

    public Action Action { get; }

    public FooterItem(
        string title,
        Action action,
        List<KeyShortcut>? shortcuts = null,
        string? tooltip = null,
        string? confirmationMessage = null,
        string? confirmationDetail = null)
    {
        Title = title;
        Action = action;
        Shortcuts = shortcuts ?? [];
        Tooltip = tooltip;
        ConfirmationMessage = confirmationMessage;
        ConfirmationDetail = confirmationDetail;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool ShowConfirmation
    {
        get => _showConfirmation;
        set => SetProperty(ref _showConfirmation, value);
    }

    public bool NeedsConfirmation => ConfirmationMessage is not null;
}

/// <summary>The footer, and the swap between "Clear" and "Clear all" when Shift is held.</summary>
public sealed class FooterViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public ObservableCollection<FooterItem> Items { get; } = [];

    public FooterItem Clear => Items[0];

    public FooterItem ClearAll => Items[1];

    public FooterViewModel(AppSettings settings, Action clear, Action clearAll, Action preferences, Action about, Action quit)
    {
        _settings = settings;

        Items.Add(new FooterItem(
            "Clear",
            clear,
            [new KeyShortcut("Backspace", HotKeyModifiers.Control | HotKeyModifiers.Alt)],
            "Clear unpinned items. Hold Shift to clear everything.",
            "Are you sure you want to clear the history?",
            "You can't undo this action."));

        Items.Add(new FooterItem(
            "Clear all",
            clearAll,
            [new KeyShortcut("Backspace", HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift)],
            "Clear all items, including pinned ones.",
            "Are you sure you want to clear all history?",
            "Pinned items will be removed too. You can't undo this action.")
        {
            IsVisible = false
        });

        Items.Add(new FooterItem(
            "Preferences…",
            preferences,
            [new KeyShortcut(",", HotKeyModifiers.Control)]));

        Items.Add(new FooterItem("About", about, tooltip: "Read more about Wincy."));

        Items.Add(new FooterItem(
            "Quit",
            quit,
            [new KeyShortcut("Q", HotKeyModifiers.Control)],
            "Quit Wincy."));
    }

    public IEnumerable<FooterItem> VisibleItems =>
        _settings.ShowFooter ? Items.Where(i => i.IsVisible) : [];

    /// <summary>
    /// Holding Shift turns Clear into Clear all, matching Maccy's footer. The selection
    /// follows the swap so keyboard focus is never left on a hidden row.
    /// </summary>
    public void ApplyModifiers(HotKeyModifiers modifiers)
    {
        var shiftOnly = modifiers.HasFlag(HotKeyModifiers.Shift);

        if (Clear.IsVisible == !shiftOnly)
        {
            return;
        }

        var selectionWasOnClearRow = Clear.IsSelected || ClearAll.IsSelected;

        Clear.IsVisible = !shiftOnly;
        ClearAll.IsVisible = shiftOnly;

        if (!selectionWasOnClearRow)
        {
            return;
        }

        Clear.IsSelected = !shiftOnly;
        ClearAll.IsSelected = shiftOnly;
    }

    public void ClearSelection()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
            item.ShowConfirmation = false;
        }
    }
}

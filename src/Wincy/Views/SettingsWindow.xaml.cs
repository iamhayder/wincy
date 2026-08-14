using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using Wincy.Interop;
using Wincy.Models;
using Wincy.Services;
using Wincy.ViewModels;

namespace Wincy.Views;

/// <summary>A label plus the value it stands for, so combo boxes can show friendly text.</summary>
public sealed record Option<T>(T Value, string Label);

/// <summary>A pinned item as shown in the Pins pane, with its letter editable in place.</summary>
public sealed class PinRow(ClipItemViewModel item, HistoryViewModel history, Action refresh) : ObservableObject
{
    public ClipItemViewModel Item => item;

    public string Title => string.IsNullOrWhiteSpace(item.Title) ? "(image)" : item.Title;

    /// <summary>Letters not already claimed by another pin, plus this row's own.</summary>
    public List<string> AvailablePins
    {
        get
        {
            var taken = history.All
                .Where(vm => vm != item && vm.Item.Pin is { Length: > 0 })
                .Select(vm => vm.Item.Pin!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return [.. ClipItem.SupportedPins.Where(p => !taken.Contains(p))];
        }
    }

    public string? Pin
    {
        get => item.Item.Pin;
        set
        {
            if (string.IsNullOrEmpty(value) || value == item.Item.Pin)
            {
                return;
            }

            history.SetPin(item, value);
            OnPropertyChanged();
            refresh();
        }
    }
}

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private readonly AppState _state;

    public SettingsWindow(AppState state)
    {
        _state = state;
        Settings = state.Settings;
        Screens = BuildScreenOptions();

        InitializeComponent();
        DataContext = this;

        AppsList.ItemsSource = IgnoredApps;
        RegexList.ItemsSource = IgnoreRegexes;
        FormatsList.ItemsSource = IgnoredFormats;
        PinsList.ItemsSource = Pins;

        foreach (var value in state.Settings.IgnoredApps) IgnoredApps.Add(value);
        foreach (var value in state.Settings.IgnoreRegexes) IgnoreRegexes.Add(value);
        foreach (var value in state.Settings.IgnoredFormats) IgnoredFormats.Add(value);

        RefreshPins();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public AppSettings Settings { get; }

    public ObservableCollection<string> IgnoredApps { get; } = [];

    public ObservableCollection<string> IgnoreRegexes { get; } = [];

    public ObservableCollection<string> IgnoredFormats { get; } = [];

    public ObservableCollection<PinRow> Pins { get; } = [];

    public long StorageSize => _state.Store.DatabaseSizeBytes();

    public string ItemCountText =>
        _state.History.All.Count == 1 ? "1 item" : $"{_state.History.All.Count} items";

    public string PinCountText => $"{Pins.Count} of {ClipItem.SupportedPins.Count} letters in use";

    // ------------------------------------------------------------------ options

    public IReadOnlyList<Option<SearchMode>> SearchModes { get; } =
    [
        new(SearchMode.Exact, "Exact — plain substring"),
        new(SearchMode.Fuzzy, "Fuzzy — characters in order"),
        new(SearchMode.Regex, "Regular expression"),
        new(SearchMode.Mixed, "Mixed — exact, then regex, then fuzzy")
    ];

    public IReadOnlyList<Option<SortBy>> SortOptions { get; } =
    [
        new(SortBy.LastCopiedAt, "Last copied"),
        new(SortBy.FirstCopiedAt, "First copied"),
        new(SortBy.NumberOfCopies, "Number of copies")
    ];

    public IReadOnlyList<Option<PopupPosition>> PopupPositions { get; } =
    [
        new(PopupPosition.Cursor, "The mouse cursor"),
        new(PopupPosition.TrayIcon, "The notification area"),
        new(PopupPosition.ActiveWindow, "The centre of the active window"),
        new(PopupPosition.ScreenCenter, "The centre of the screen"),
        new(PopupPosition.LastPosition, "Where it was last time")
    ];

    public IReadOnlyList<Option<PinsPosition>> PinPositions { get; } =
    [
        new(PinsPosition.Top, "The top of the list"),
        new(PinsPosition.Bottom, "The bottom of the list")
    ];

    public IReadOnlyList<Option<HighlightMatch>> HighlightStyles { get; } =
    [
        new(HighlightMatch.Bold, "Bold"),
        new(HighlightMatch.Italic, "Italic"),
        new(HighlightMatch.Underline, "Underline"),
        new(HighlightMatch.Highlight, "Colour")
    ];

    public IReadOnlyList<Option<SearchVisibility>> SearchVisibilities { get; } =
    [
        new(SearchVisibility.Always, "Always"),
        new(SearchVisibility.DuringSearch, "Only while searching")
    ];

    public IReadOnlyList<Option<TrayIconStyle>> TrayIcons { get; } =
    [
        new(TrayIconStyle.Wincy, "Wincy"),
        new(TrayIconStyle.Clipboard, "Clipboard"),
        new(TrayIconStyle.Scissors, "Scissors")
    ];

    public IReadOnlyList<Option<int>> Screens { get; }

    private static List<Option<int>> BuildScreenOptions()
    {
        var options = new List<Option<int>> { new(0, "Wherever the popup is summoned") };

        var monitors = ScreenHelper.All();
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            options.Add(new Option<int>(
                i + 1, $"{monitor.DisplayName} — {monitor.Bounds.Width}×{monitor.Bounds.Height}"));
        }

        return options;
    }

    // --------------------------------------------------------------------- pins

    private void RefreshPins()
    {
        Pins.Clear();

        foreach (var item in _state.History.All.Where(vm => vm.IsPinned))
        {
            Pins.Add(new PinRow(item, _state.History, RefreshPins));
        }

        Raise(nameof(PinCountText));
    }

    private void OnUnpin(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PinRow row })
        {
            _state.History.TogglePin(row.Item);
            RefreshPins();
        }
    }

    // ------------------------------------------------------------------- ignore

    private void OnAddApp(object sender, RoutedEventArgs e)
    {
        var value = AppEntry.Text.Trim();
        if (value.Length == 0 || IgnoredApps.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        IgnoredApps.Add(value);
        AppEntry.Clear();
        CommitList(IgnoredApps, _state.Settings.IgnoredApps, nameof(AppSettings.IgnoredApps));
    }

    private void OnBrowseApp(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an application to ignore",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            AppEntry.Text = Path.GetFileName(dialog.FileName);
            OnAddApp(sender, e);
        }
    }

    private void OnRemoveApp(object sender, RoutedEventArgs e)
    {
        if (AppsList.SelectedItem is string value)
        {
            IgnoredApps.Remove(value);
            CommitList(IgnoredApps, _state.Settings.IgnoredApps, nameof(AppSettings.IgnoredApps));
        }
    }

    private void OnAddRegex(object sender, RoutedEventArgs e)
    {
        var value = RegexEntry.Text.Trim();
        if (value.Length == 0)
        {
            return;
        }

        try
        {
            _ = new Regex(value);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, "That is not a valid regular expression:\n\n" + ex.Message,
                "Wincy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (IgnoreRegexes.Contains(value, StringComparer.Ordinal))
        {
            return;
        }

        IgnoreRegexes.Add(value);
        RegexEntry.Clear();
        CommitList(IgnoreRegexes, _state.Settings.IgnoreRegexes, nameof(AppSettings.IgnoreRegexes));
    }

    private void OnRemoveRegex(object sender, RoutedEventArgs e)
    {
        if (RegexList.SelectedItem is string value)
        {
            IgnoreRegexes.Remove(value);
            CommitList(IgnoreRegexes, _state.Settings.IgnoreRegexes, nameof(AppSettings.IgnoreRegexes));
        }
    }

    private void OnAddFormat(object sender, RoutedEventArgs e)
    {
        var value = FormatEntry.Text.Trim();
        if (value.Length == 0 || IgnoredFormats.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        IgnoredFormats.Add(value);
        FormatEntry.Clear();
        CommitList(IgnoredFormats, _state.Settings.IgnoredFormats, nameof(AppSettings.IgnoredFormats));
    }

    private void OnRemoveFormat(object sender, RoutedEventArgs e)
    {
        if (FormatsList.SelectedItem is string value)
        {
            IgnoredFormats.Remove(value);
            CommitList(IgnoredFormats, _state.Settings.IgnoredFormats, nameof(AppSettings.IgnoredFormats));
        }
    }

    /// <summary>
    /// Lists cannot raise change notifications of their own, so edits are copied into
    /// the settings object and the save is triggered explicitly.
    /// </summary>
    private void CommitList(IEnumerable<string> source, List<string> target, string name)
    {
        target.Clear();
        target.AddRange(source);
        _state.SettingsService.NotifyListChanged(name);
    }

    // -------------------------------------------------------------- diagnostics

    private void OnCompact(object sender, RoutedEventArgs e)
    {
        _state.Store.CleanupOrphanedContents();
        _state.Store.Compact();
        Raise(nameof(StorageSize));
        Raise(nameof(ItemCountText));
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => Open(_state.DataDirectory);

    private void OnOpenLog(object sender, RoutedEventArgs e) =>
        Open(Path.Combine(_state.DataDirectory, "wincy.log"));

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {path}: {ex.Message}");
        }
    }
}

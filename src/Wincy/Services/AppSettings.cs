using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Wincy.Interop;
using Wincy.Models;

namespace Wincy.Services;

public enum SearchMode
{
    Exact,
    Fuzzy,
    Regex,
    Mixed
}

public enum SortBy
{
    LastCopiedAt,
    FirstCopiedAt,
    NumberOfCopies
}

public enum PinsPosition
{
    Top,
    Bottom
}

public enum PopupPosition
{
    Cursor,
    TrayIcon,
    ActiveWindow,
    ScreenCenter,
    LastPosition
}

public enum HighlightMatch
{
    Bold,
    Italic,
    Underline,
    Highlight
}

public enum SearchVisibility
{
    Always,
    DuringSearch
}

public enum TrayIconStyle
{
    Wincy,
    Clipboard,
    Scissors
}

public enum PreviewPlacement
{
    Right,
    Left
}

/// <summary>
/// Every user-visible preference, matching Maccy's Defaults keys one for one where
/// the concept carries over to Windows.
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ------------------------------------------------------------------ general

    private HotKey _popupHotKey = new(HotKeyModifiers.Control | HotKeyModifiers.Shift, Key.V);
    /// <summary>
    /// Ctrl+Shift+V by default. Win+V is owned by the built-in Windows clipboard
    /// history and cannot be taken over by an ordinary process.
    /// </summary>
    public HotKey PopupHotKey { get => _popupHotKey; set => Set(ref _popupHotKey, value); }

    private HotKey _pinHotKey = new(HotKeyModifiers.Alt, Key.P);
    public HotKey PinHotKey { get => _pinHotKey; set => Set(ref _pinHotKey, value); }

    private HotKey _deleteHotKey = new(HotKeyModifiers.Alt, Key.Back);
    public HotKey DeleteHotKey { get => _deleteHotKey; set => Set(ref _deleteHotKey, value); }

    private HotKey _previewHotKey = new(HotKeyModifiers.Alt, Key.Space);
    public HotKey PreviewHotKey { get => _previewHotKey; set => Set(ref _previewHotKey, value); }

    private SearchMode _searchMode = SearchMode.Exact;
    public SearchMode SearchMode { get => _searchMode; set => Set(ref _searchMode, value); }

    private bool _pasteByDefault;
    public bool PasteByDefault { get => _pasteByDefault; set => Set(ref _pasteByDefault, value); }

    private bool _removeFormattingByDefault;
    public bool RemoveFormattingByDefault
    {
        get => _removeFormattingByDefault;
        set => Set(ref _removeFormattingByDefault, value);
    }

    private bool _playSounds;
    /// <summary>
    /// Off by default, unlike Maccy. A system sound on every single copy reads as a
    /// malfunction on Windows, where the alert sounds are much more assertive.
    /// </summary>
    public bool PlaySounds { get => _playSounds; set => Set(ref _playSounds, value); }

    private bool _showNotifications;
    public bool ShowNotifications { get => _showNotifications; set => Set(ref _showNotifications, value); }

    // ------------------------------------------------------------------ storage

    private int _size = 200;
    public int Size { get => _size; set => Set(ref _size, Math.Clamp(value, 1, 999)); }

    private SortBy _sortBy = SortBy.LastCopiedAt;
    public SortBy SortBy { get => _sortBy; set => Set(ref _sortBy, value); }

    private bool _storeText = true;
    public bool StoreText { get => _storeText; set => Set(ref _storeText, value); }

    private bool _storeImages = true;
    public bool StoreImages { get => _storeImages; set => Set(ref _storeImages, value); }

    private bool _storeFiles = true;
    public bool StoreFiles { get => _storeFiles; set => Set(ref _storeFiles, value); }

    [JsonIgnore]
    public IEnumerable<string> EnabledFormats
    {
        get
        {
            if (StoreText) foreach (var f in ClipFormats.TextFormats) yield return f;
            if (StoreImages) foreach (var f in ClipFormats.ImageFormats) yield return f;
            if (StoreFiles) foreach (var f in ClipFormats.FileFormats) yield return f;
        }
    }

    // --------------------------------------------------------------- appearance

    private PopupPosition _popupPosition = PopupPosition.Cursor;
    public PopupPosition PopupPosition { get => _popupPosition; set => Set(ref _popupPosition, value); }

    private int _popupScreen;
    /// <summary>0 = the screen the popup is being summoned on; otherwise a 1-based monitor index.</summary>
    public int PopupScreen { get => _popupScreen; set => Set(ref _popupScreen, value); }

    private PinsPosition _pinTo = PinsPosition.Top;
    public PinsPosition PinTo { get => _pinTo; set => Set(ref _pinTo, value); }

    private int _imageMaxHeight = 40;
    public int ImageMaxHeight { get => _imageMaxHeight; set => Set(ref _imageMaxHeight, Math.Clamp(value, 1, 200)); }

    private bool _openPreviewAutomatically;
    /// <summary>
    /// Off by default, unlike Maccy. The preview widens the window while you are
    /// reading the list, which is disruptive unless you asked for it; Alt+Space opens
    /// it on demand.
    /// </summary>
    public bool OpenPreviewAutomatically
    {
        get => _openPreviewAutomatically;
        set => Set(ref _openPreviewAutomatically, value);
    }

    private int _previewDelay = 1500;
    public int PreviewDelay { get => _previewDelay; set => Set(ref _previewDelay, Math.Clamp(value, 200, 100_000)); }

    private PreviewPlacement _previewPlacement = PreviewPlacement.Right;
    public PreviewPlacement PreviewPlacement { get => _previewPlacement; set => Set(ref _previewPlacement, value); }

    private HighlightMatch _highlightMatch = HighlightMatch.Bold;
    public HighlightMatch HighlightMatch { get => _highlightMatch; set => Set(ref _highlightMatch, value); }

    private TrayIconStyle _trayIcon = TrayIconStyle.Wincy;
    public TrayIconStyle TrayIcon { get => _trayIcon; set => Set(ref _trayIcon, value); }

    private bool _showInTray = true;
    public bool ShowInTray { get => _showInTray; set => Set(ref _showInTray, value); }

    private bool _showRecentCopyInTooltip;
    public bool ShowRecentCopyInTooltip
    {
        get => _showRecentCopyInTooltip;
        set => Set(ref _showRecentCopyInTooltip, value);
    }

    private bool _showSearch = true;
    public bool ShowSearch { get => _showSearch; set => Set(ref _showSearch, value); }

    private SearchVisibility _searchVisibility = SearchVisibility.Always;
    public SearchVisibility SearchVisibility { get => _searchVisibility; set => Set(ref _searchVisibility, value); }

    private bool _showFooter = true;
    public bool ShowFooter { get => _showFooter; set => Set(ref _showFooter, value); }

    private bool _showTitle = true;
    public bool ShowTitle { get => _showTitle; set => Set(ref _showTitle, value); }

    private bool _showApplicationIcons;
    public bool ShowApplicationIcons { get => _showApplicationIcons; set => Set(ref _showApplicationIcons, value); }

    private bool _showHexColorSwatch = true;
    public bool ShowHexColorSwatch { get => _showHexColorSwatch; set => Set(ref _showHexColorSwatch, value); }

    private bool _showSpecialSymbols = true;
    public bool ShowSpecialSymbols { get => _showSpecialSymbols; set => Set(ref _showSpecialSymbols, value); }

    private bool _useBackdrop = true;
    /// <summary>Mica/Acrylic on Windows 11. Turning it off gives a plain opaque window.</summary>
    public bool UseBackdrop { get => _useBackdrop; set => Set(ref _useBackdrop, value); }

    private double _windowWidth = 450;
    public double WindowWidth { get => _windowWidth; set => Set(ref _windowWidth, Math.Clamp(value, 280, 1400)); }

    private double _windowHeight = 800;
    public double WindowHeight { get => _windowHeight; set => Set(ref _windowHeight, Math.Clamp(value, 200, 2000)); }

    private double _previewWidth = 400;
    public double PreviewWidth { get => _previewWidth; set => Set(ref _previewWidth, Math.Clamp(value, 200, 1200)); }

    /// <summary>Last popup position as a fraction of the work area, for PopupPosition.LastPosition.</summary>
    private double _windowPositionX = 0.5;
    public double WindowPositionX { get => _windowPositionX; set => Set(ref _windowPositionX, value); }

    private double _windowPositionY = 0.2;
    public double WindowPositionY { get => _windowPositionY; set => Set(ref _windowPositionY, value); }

    // ------------------------------------------------------------------- ignore

    private bool _ignoreEvents;
    public bool IgnoreEvents { get => _ignoreEvents; set => Set(ref _ignoreEvents, value); }

    private bool _ignoreOnlyNextEvent;
    public bool IgnoreOnlyNextEvent { get => _ignoreOnlyNextEvent; set => Set(ref _ignoreOnlyNextEvent, value); }

    private bool _ignoreAllAppsExceptListed;
    public bool IgnoreAllAppsExceptListed
    {
        get => _ignoreAllAppsExceptListed;
        set => Set(ref _ignoreAllAppsExceptListed, value);
    }

    /// <summary>Executable names, e.g. "keepass.exe". Matched case-insensitively.</summary>
    public List<string> IgnoredApps { get; set; } = [];

    public List<string> IgnoreRegexes { get; set; } = [];

    public List<string> IgnoredFormats { get; set; } = [.. ClipFormats.DefaultIgnoredFormats];

    // ----------------------------------------------------------------- advanced

    private bool _clearOnQuit;
    public bool ClearOnQuit { get => _clearOnQuit; set => Set(ref _clearOnQuit, value); }

    private bool _clearSystemClipboard;
    public bool ClearSystemClipboard { get => _clearSystemClipboard; set => Set(ref _clearSystemClipboard, value); }

    private bool _suppressClearAlert;
    public bool SuppressClearAlert { get => _suppressClearAlert; set => Set(ref _suppressClearAlert, value); }

    private int _clipboardDebounceMs = 120;
    /// <summary>
    /// Wincy is event-driven (WM_CLIPBOARDUPDATE) rather than polled, so this is a
    /// settle delay rather than Maccy's poll interval: apps often publish formats in
    /// several passes, and reading too early captures only the first one.
    /// </summary>
    public int ClipboardDebounceMs
    {
        get => _clipboardDebounceMs;
        set => Set(ref _clipboardDebounceMs, Math.Clamp(value, 0, 2000));
    }

    private bool _launchAtLogin;
    public bool LaunchAtLogin { get => _launchAtLogin; set => Set(ref _launchAtLogin, value); }
}

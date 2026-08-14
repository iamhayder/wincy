using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wincy.Interop;
using Wincy.Models;
using Wincy.Services;

namespace Wincy.ViewModels;

/// <summary>
/// The presentation wrapper around a <see cref="ClipItem"/> — Maccy's
/// HistoryItemDecorator. Holds everything the row needs: the display title, the match
/// ranges to highlight, the thumbnail, the shortcut badges and the selection state.
/// </summary>
public sealed class ClipItemViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly AppIconCache _icons;

    private BitmapSource? _thumbnail;
    private bool _thumbnailRequested;
    private string _title = string.Empty;
    private IReadOnlyList<TextRange> _highlightRanges = [];
    private bool _isVisible = true;
    private int _selectionIndex = -1;
    private List<KeyShortcut> _shortcuts = [];

    public Guid Id { get; } = Guid.NewGuid();

    public ClipItem Item { get; }

    public ClipItemViewModel(ClipItem item, AppSettings settings, AppIconCache icons)
    {
        Item = item;
        _settings = settings;
        _icons = icons;
        _title = item.Title;
    }

    // ------------------------------------------------------------------ display

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    /// <summary>Ranges of <see cref="Title"/> that matched the search query.</summary>
    public IReadOnlyList<TextRange> HighlightRanges
    {
        get => _highlightRanges;
        private set => SetProperty(ref _highlightRanges, value);
    }

    public HighlightMatch HighlightStyle => _settings.HighlightMatch;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsPinned => Item.IsPinned;

    public bool IsUnpinned => !Item.IsPinned;

    public bool HasImage => Item.HasImage;

    public string? ApplicationName => Item.ApplicationName;

    public BitmapSource? ApplicationIcon =>
        _settings.ShowApplicationIcons ? _icons.Get(Item.Application) : null;

    /// <summary>
    /// The colour swatch shown for copied hex/rgb colours. Null unless the setting is
    /// on and the title parses as a colour.
    /// </summary>
    public Color? Swatch => _settings.ShowHexColorSwatch ? ColorSwatch.Parse(Item.Title) : null;

    public bool HasSwatch => Swatch is not null && !HasImage;

    /// <summary>Row thumbnail, generated on first render and capped by the image-height setting.</summary>
    public BitmapSource? Thumbnail
    {
        get
        {
            EnsureThumbnail();
            return _thumbnail;
        }
    }

    public string Tooltip
    {
        get
        {
            var parts = new List<string>();

            if (HasImage)
            {
                var image = FullImage;
                parts.Add(image is null ? "Image" : $"Image, {image.PixelWidth}×{image.PixelHeight}");
            }
            else
            {
                var text = Item.PreviewableText;
                parts.Add(text.Length > 400 ? text[..400] + "…" : text);
            }

            if (ApplicationName is { Length: > 0 } app)
            {
                parts.Add(app);
            }

            if (IsPinned)
            {
                parts.Add("Pinned");
            }

            return string.Join("\n", parts);
        }
    }

    /// <summary>Full-size image for the preview pane. Decoded on demand, never cached in the row.</summary>
    public BitmapSource? FullImage => ImageHelper.Decode(Item.ImageData, isDib: false);

    public string PreviewText
    {
        get
        {
            var text = Item.PreviewableText;
            // 10k characters is plenty for any display and keeps the preview responsive.
            return text.Length > 10_000 ? text[..10_000] : text;
        }
    }

    // ---------------------------------------------------------------- selection

    /// <summary>-1 when unselected; otherwise the position within a multi-selection.</summary>
    public int SelectionIndex
    {
        get => _selectionIndex;
        set
        {
            if (SetProperty(ref _selectionIndex, value))
            {
                OnPropertyChanged(nameof(IsSelected), nameof(MultiSelectionBadge));
            }
        }
    }

    public bool IsSelected => _selectionIndex != -1;

    /// <summary>The "2 of 5" badge, shown only while a multi-selection is in progress.</summary>
    public string? MultiSelectionBadge { get; private set; }

    public void SetMultiSelectionBadge(string? badge)
    {
        MultiSelectionBadge = badge;
        OnPropertyChanged(nameof(MultiSelectionBadge));
    }

    public List<KeyShortcut> Shortcuts
    {
        get => _shortcuts;
        set
        {
            if (SetProperty(ref _shortcuts, value))
            {
                OnPropertyChanged(nameof(VisibleShortcut), nameof(HasVisibleShortcut));
            }
        }
    }

    private HotKeyModifiers _activeModifiers = HotKeyModifiers.None;

    /// <summary>
    /// The modifiers currently held. Rows show the one badge that matches, so the
    /// list always advertises what the next keypress will actually do.
    /// </summary>
    public HotKeyModifiers ActiveModifiers
    {
        get => _activeModifiers;
        set
        {
            if (SetProperty(ref _activeModifiers, value))
            {
                OnPropertyChanged(nameof(VisibleShortcut), nameof(HasVisibleShortcut));
            }
        }
    }

    public KeyShortcut? VisibleShortcut =>
        _shortcuts.FirstOrDefault(s => s.IsVisible(_shortcuts, _activeModifiers));

    public bool HasVisibleShortcut => VisibleShortcut is not null;

    // ----------------------------------------------------------------- updating

    public void ApplyHighlight(string query, IReadOnlyList<TextRange> ranges)
    {
        if (string.IsNullOrEmpty(query) || Title.Length == 0)
        {
            HighlightRanges = [];
            return;
        }

        // Clamp to the displayed title: fuzzy ranges are computed against a truncated copy.
        HighlightRanges =
        [
            .. ranges
                .Where(r => r.Start >= 0 && r.Start < Title.Length)
                .Select(r => new TextRange(r.Start, Math.Min(r.Length, Title.Length - r.Start)))
        ];
    }

    public void RefreshTitle()
    {
        Item.Title = Item.GenerateTitle(_settings.ShowSpecialSymbols);
        Title = Item.Title;
        OnPropertyChanged(nameof(Swatch), nameof(HasSwatch));
    }

    public void RefreshAppearance()
    {
        _thumbnail = null;
        _thumbnailRequested = false;
        OnPropertyChanged(
            nameof(Thumbnail), nameof(ApplicationIcon), nameof(Swatch),
            nameof(HasSwatch), nameof(HighlightStyle), nameof(IsPinned), nameof(IsUnpinned));
    }

    private void EnsureThumbnail()
    {
        if (_thumbnailRequested)
        {
            return;
        }

        _thumbnailRequested = true;

        if (!Item.HasImage)
        {
            return;
        }

        var decoded = ImageHelper.Decode(Item.ImageData, isDib: false);
        _thumbnail = ImageHelper.Resize(decoded, 340, _settings.ImageMaxHeight);
    }
}

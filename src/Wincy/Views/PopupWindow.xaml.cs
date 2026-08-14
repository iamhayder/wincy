using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wincy.Interop;
using Wincy.Services;
using Wincy.ViewModels;

namespace Wincy.Views;

public partial class PopupWindow : Window
{
    private const double RowHeight = 24;
    private const double SeparatorHeight = 13;
    // 5px top margin plus the 30px search field. Kept in step with PopupWindow.xaml,
    // since the popup sizes itself from these numbers rather than from a layout pass.
    private const double HeaderHeight = 35;
    private const double BannerHeight = 34;
    private const double VerticalPadding = 10;

    private readonly AppState _state;
    private readonly DispatcherTimer _previewTimer;

    private IntPtr _handle = IntPtr.Zero;
    private bool _backdropApplied;
    private bool _previewOpen;
    private bool _suppressSearchEvents;

    public PopupWindow(AppState state)
    {
        _state = state;
        InitializeComponent();

        ActivateCommand = new RelayCommand(p => ActivateItem(p as ClipItemViewModel));
        ActivateFooterCommand = new RelayCommand(p => ActivateFooter(p as FooterItem));
        ConfirmCommand = new RelayCommand(p => Confirm(p as FooterItem));
        CancelConfirmationCommand = new RelayCommand(p =>
        {
            if (p is FooterItem item)
            {
                item.ShowConfirmation = false;
            }
        });

        DataContext = this;

        HistoryList.ItemsSource = _state.History.UnpinnedItems;
        TopPinsList.ItemsSource = _state.History.PinnedItems;
        BottomPinsList.ItemsSource = _state.History.PinnedItems;
        FooterList.ItemsSource = _state.Footer.Items;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(state.Settings.PreviewDelay) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            if (_state.Settings.OpenPreviewAutomatically && _state.Navigator.SelectedItem is not null)
            {
                SetPreviewOpen(true);
            }
        };

        SearchBox.TextChanged += OnSearchTextChanged;
        ClearSearchButton.Click += (_, _) => SearchBox.Clear();
        PreviewToggleButton.Click += (_, _) => SetPreviewOpen(!_previewOpen);
        StopPasteStackButton.Click += (_, _) =>
        {
            _state.History.InterruptPasteStack();
            SyncPasteStack();
        };

        _state.History.CloseRequested += HidePopup;
        _state.History.ResizeRequested += () => Dispatcher.InvokeAsync(SyncLayout);
        _state.History.ScrollRequested += ScrollTo;
        _state.History.ItemsChanged += () => Dispatcher.InvokeAsync(SyncLayout);
        _state.History.PropertyChanged += OnHistoryPropertyChanged;
        _state.Navigator.ScrollRequested += ScrollTo;
        _state.Navigator.SelectionChanged += OnSelectionChanged;
        _state.Modifiers.ModifiersChanged += OnModifiersChanged;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseMove += (_, _) => _state.Navigator.IsKeyboardNavigating = false;
        Deactivated += (_, _) => HidePopup();
        SizeChanged += (_, _) => RememberPreviewWidth();

        // Create the HWND up front so the DWM effects and the tool-window style are in
        // place before the first Show, rather than flashing an unstyled frame.
        new WindowInteropHelper(this).EnsureHandle();
    }

    public bool IsOpen { get; private set; }

    public ICommand ActivateCommand { get; }

    public ICommand ActivateFooterCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelConfirmationCommand { get; }

    /// <summary>Bound by the inline confirmation's "Don't ask again" checkbox.</summary>
    public bool SuppressClearAlert
    {
        get => _state.Settings.SuppressClearAlert;
        set => _state.Settings.SuppressClearAlert = value;
    }

    // ------------------------------------------------------------------ window

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;

        WindowEffects.MakeToolWindow(_handle);
        RefreshTheme();

        // Keep the popup out of the way until the hotkey summons it.
        Hide();
    }

    public void RefreshTheme()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var dark = SystemTheme.IsDark();
        WindowEffects.ApplyDarkMode(_handle, dark);
        WindowEffects.ApplyRoundedCorners(_handle);

        _backdropApplied = _state.Settings.UseBackdrop &&
                           WindowEffects.ApplyBackdrop(_handle, BackdropKind.Acrylic);

        // With a system backdrop the window must not paint its own opaque background,
        // or the acrylic material would be hidden behind it. Windows 10 has no backdrop,
        // so there the themed brush is what actually draws the window.
        if (_backdropApplied)
        {
            Background = Brushes.Transparent;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        }
    }

    public void ShowPopup()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;

        _suppressSearchEvents = true;
        SearchBox.Clear();
        _suppressSearchEvents = false;
        _state.History.SearchQuery = string.Empty;

        SyncSearchVisibility();
        SyncPasteStack();
        SyncLayout();

        _state.Navigator.IsKeyboardNavigating = true;
        _state.Navigator.HighlightFirst();

        Show();
        Reposition();

        // Window.Activate() is not enough: Windows refuses foreground changes from a
        // process that does not already own it, so the same thread-attach dance the
        // paste path uses is needed to actually take focus.
        Paster.RestoreForeground(_handle);
        Activate();

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        RestartPreviewTimer();
    }

    public void HidePopup()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        _previewTimer.Stop();

        RememberPosition();
        _state.Footer.ClearSelection();
        _state.Modifiers.Stop();

        Hide();
    }

    /// <summary>
    /// Sizes the window to its content — capped by the configured maximum height and
    /// floored at three rows — then places it according to the popup-position setting.
    /// </summary>
    private void Reposition()
    {
        var monitor = TargetMonitor();
        var scale = monitor.Scale;

        var widthDip = _state.Settings.WindowWidth + (_previewOpen ? _state.Settings.PreviewWidth + 4 : 0);
        var heightDip = DesiredHeight();

        var width = (int)Math.Round(widthDip * scale);
        var height = (int)Math.Round(heightDip * scale);

        var (x, y) = Origin(width, height, monitor);
        WindowEffects.SetBounds(_handle, x, y, width, height);
    }

    private double DesiredHeight()
    {
        var height = VerticalPadding;

        if (_state.IsSearchVisible)
        {
            height += HeaderHeight;
        }

        if (_state.History.PasteStack is not null)
        {
            height += BannerHeight;
        }

        var pins = _state.History.PinnedItems.Count;
        if (pins > 0)
        {
            height += (pins * RowHeight) + SeparatorHeight;
        }

        var rows = _state.History.UnpinnedItems.Count;
        height += rows * RowHeight;

        if (_state.Settings.ShowFooter)
        {
            height += (_state.Footer.VisibleItems.Count() * RowHeight) + SeparatorHeight + 5;
        }

        // Always leave room for three rows so an empty history is not a sliver.
        var minimum = (3 * RowHeight) + VerticalPadding +
                      (_state.IsSearchVisible ? HeaderHeight : 0) +
                      (_state.Settings.ShowFooter ? (_state.Footer.VisibleItems.Count() * RowHeight) + SeparatorHeight : 0);

        return Math.Clamp(height, minimum, _state.Settings.WindowHeight);
    }

    private MonitorInfo TargetMonitor()
    {
        var configured = _state.Settings.PopupScreen;
        if (configured > 0)
        {
            var all = ScreenHelper.All();
            if (configured <= all.Count)
            {
                return all[configured - 1];
            }
        }

        return _state.Settings.PopupPosition switch
        {
            PopupPosition.ActiveWindow when _state.PreviousForegroundWindow != IntPtr.Zero =>
                ScreenHelper.FromWindow(_state.PreviousForegroundWindow),
            PopupPosition.ScreenCenter => ScreenHelper.FromPoint(ScreenHelper.CursorPosition()),
            _ => ScreenHelper.FromPoint(ScreenHelper.CursorPosition())
        };
    }

    private (int X, int Y) Origin(int width, int height, MonitorInfo monitor)
    {
        var work = monitor.WorkArea;

        switch (_state.Settings.PopupPosition)
        {
            case PopupPosition.ScreenCenter:
                return ScreenHelper.Constrain(
                    work.Left + ((work.Width - width) / 2),
                    work.Top + ((work.Height - height) / 2),
                    width, height, monitor);

            case PopupPosition.ActiveWindow when _state.PreviousForegroundWindow != IntPtr.Zero:
            {
                var target = WindowEffects.GetBounds(_state.PreviousForegroundWindow);
                return ScreenHelper.Constrain(
                    target.Left + ((target.Width - width) / 2),
                    target.Top + ((target.Height - height) / 2),
                    width, height, monitor);
            }

            case PopupPosition.TrayIcon:
                // The tray sits at the end of the taskbar; anchoring to the cursor's
                // corner of the work area is both simpler and more predictable than
                // querying the notification area's rectangle.
                return ScreenHelper.Constrain(work.Right - width - 12, work.Bottom - height - 12, width, height, monitor);

            case PopupPosition.LastPosition:
                return ScreenHelper.Constrain(
                    work.Left + (int)(work.Width * _state.Settings.WindowPositionX) - (width / 2),
                    work.Top + (int)(work.Height * _state.Settings.WindowPositionY),
                    width, height, monitor);

            default:
            {
                var cursor = ScreenHelper.CursorPosition();
                return ScreenHelper.Constrain(cursor.X, cursor.Y, width, height, monitor);
            }
        }
    }

    private void RememberPosition()
    {
        if (_state.Settings.PopupPosition != PopupPosition.LastPosition || _handle == IntPtr.Zero)
        {
            return;
        }

        var bounds = WindowEffects.GetBounds(_handle);
        var monitor = ScreenHelper.FromWindow(_handle);
        var work = monitor.WorkArea;

        if (work.Width <= 0 || work.Height <= 0)
        {
            return;
        }

        _state.Settings.WindowPositionX = (bounds.Left + (bounds.Width / 2.0) - work.Left) / work.Width;
        _state.Settings.WindowPositionY = (bounds.Top - work.Top) / (double)work.Height;
    }

    private void RememberPreviewWidth()
    {
        if (_previewOpen && PreviewColumn.ActualWidth > 100)
        {
            _state.Settings.PreviewWidth = PreviewColumn.ActualWidth;
        }
    }

    // ------------------------------------------------------------------ layout

    private void SyncLayout()
    {
        var pinsAtTop = _state.Settings.PinTo == PinsPosition.Top;
        var hasPins = _state.History.PinnedItems.Count > 0;

        TopPins.Visibility = hasPins && pinsAtTop ? Visibility.Visible : Visibility.Collapsed;
        BottomPins.Visibility = hasPins && !pinsAtTop ? Visibility.Visible : Visibility.Collapsed;
        TopPinsSeparator.Visibility = _state.History.UnpinnedItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        Footer.Visibility = _state.Settings.ShowFooter ? Visibility.Visible : Visibility.Collapsed;
        AppTitle.Visibility = _state.Settings.ShowTitle ? Visibility.Visible : Visibility.Collapsed;

        SyncSearchVisibility();
        SyncPasteStack();

        if (IsOpen)
        {
            Reposition();
        }
    }

    private void SyncSearchVisibility()
    {
        Header.Visibility = _state.IsSearchVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncPasteStack()
    {
        var stack = _state.History.PasteStack;

        if (stack is null)
        {
            PasteStackBanner.Visibility = Visibility.Collapsed;
            return;
        }

        PasteStackBanner.Visibility = Visibility.Visible;
        PasteStackLabel.Text = stack.Summary;
    }

    private void OnHistoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.PasteStack))
        {
            Dispatcher.InvokeAsync(SyncPasteStack);
        }
    }

    private void ScrollTo(ClipItemViewModel item)
    {
        if (!IsOpen || item.IsPinned)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (_state.History.UnpinnedItems.Contains(item))
            {
                HistoryList.ScrollIntoView(item);
            }
        }, DispatcherPriority.Background);
    }

    // ----------------------------------------------------------------- preview

    private void OnSelectionChanged(ClipItemViewModel? item)
    {
        UpdatePreview(item);

        if (item is null)
        {
            _previewTimer.Stop();
        }
        else
        {
            RestartPreviewTimer();
        }
    }

    private void RestartPreviewTimer()
    {
        _previewTimer.Stop();

        if (!_state.Settings.OpenPreviewAutomatically || _previewOpen)
        {
            return;
        }

        _previewTimer.Interval = TimeSpan.FromMilliseconds(_state.Settings.PreviewDelay);
        _previewTimer.Start();
    }

    private void SetPreviewOpen(bool open)
    {
        _previewOpen = open;
        _previewTimer.Stop();

        if (open)
        {
            PreviewColumn.Width = new GridLength(_state.Settings.PreviewWidth);
            PreviewPane.Visibility = Visibility.Visible;
            PreviewSplitter.Visibility = Visibility.Visible;
            UpdatePreview(_state.Navigator.SelectedItem);
        }
        else
        {
            PreviewColumn.Width = GridLength.Auto;
            PreviewPane.Visibility = Visibility.Collapsed;
            PreviewSplitter.Visibility = Visibility.Collapsed;
        }

        if (IsOpen)
        {
            Reposition();
        }
    }

    private void UpdatePreview(ClipItemViewModel? item)
    {
        if (!_previewOpen)
        {
            return;
        }

        if (item is null)
        {
            PreviewText.Text = string.Empty;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewApplication.Text = string.Empty;
            PreviewFirstCopied.Text = string.Empty;
            PreviewLastCopied.Text = string.Empty;
            PreviewCopyCount.Text = string.Empty;
            return;
        }

        if (item.HasImage)
        {
            PreviewImage.Source = item.FullImage;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewText.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Visible;
            PreviewText.Text = item.PreviewText;
        }

        PreviewApplication.Text = item.ApplicationName is { Length: > 0 } app ? $"Copied from {app}" : string.Empty;
        PreviewFirstCopied.Text = $"First copied {item.Item.FirstCopiedAt.ToLocalTime():g}";
        PreviewLastCopied.Text = $"Last copied {item.Item.LastCopiedAt.ToLocalTime():g}";
        PreviewCopyCount.Text = item.Item.NumberOfCopies == 1
            ? "Copied once"
            : $"Copied {item.Item.NumberOfCopies} times";
    }

    // ------------------------------------------------------------------ search

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchEvents)
        {
            return;
        }

        _state.History.SearchQuery = SearchBox.Text;
        SyncSearchVisibility();
    }

    // ---------------------------------------------------------------- activation

    private void ActivateItem(ClipItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var modifiers = ModifierWatcher.Read();

        if (modifiers.HasFlag(HotKeyModifiers.Control) && _state.Navigator.MultiSelectionEnabled)
        {
            _state.Navigator.AddToSelection(item);
            return;
        }

        _state.Navigator.Select(item, scroll: false);
        _state.History.Select(item, modifiers);
    }

    /// <summary>
    /// Hover selection.
    ///
    /// The guard is what stops the list stealing the selection while you drive with the
    /// keyboard: scrolling moves rows under a stationary pointer and would otherwise
    /// fire this. Only a genuine mouse movement clears the keyboard-navigating flag —
    /// the window's PreviewMouseMove tunnels through first, so by the time this runs
    /// the flag already reflects reality.
    /// </summary>
    private void OnRowHover(object sender, MouseEventArgs e)
    {
        if (_state.Navigator.IsKeyboardNavigating)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: ClipItemViewModel item } &&
            !ReferenceEquals(_state.Navigator.SelectedItem, item))
        {
            _state.Navigator.Select(item, scroll: false);
        }
    }

    private void OnFooterRowHover(object sender, MouseEventArgs e)
    {
        if (_state.Navigator.IsKeyboardNavigating)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: FooterItem item } &&
            !ReferenceEquals(_state.Navigator.SelectedFooterItem, item))
        {
            _state.Navigator.Select(item);
        }
    }

    private void ActivateFooter(FooterItem? item)
    {
        if (item is null)
        {
            return;
        }

        _state.Navigator.Select(item);

        if (item.NeedsConfirmation && !_state.Settings.SuppressClearAlert)
        {
            item.ShowConfirmation = true;
            return;
        }

        item.Action();
    }

    private void Confirm(FooterItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.ShowConfirmation = false;
        item.Action();
    }

    // ---------------------------------------------------------------- modifiers

    private void OnModifiersChanged(HotKeyModifiers modifiers)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _state.History.SetActiveModifiers(modifiers);
            _state.Footer.ApplyModifiers(modifiers);
        });
    }

    // ------------------------------------------------------------ key handling

    /// <summary>
    /// The whole keyboard model, ported from Maccy's KeyChord. Command maps to Ctrl and
    /// Option to Alt; the one combination that could not carry over is Ctrl+Alt+Delete,
    /// which Windows reserves, so Clear is bound to Ctrl+Alt+Backspace instead.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt combinations arrive as Key.System with the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);

        _state.Navigator.IsKeyboardNavigating = true;

        // ---- configurable shortcuts first, so a user rebind always wins
        if (Matches(_state.Settings.PreviewHotKey, key, modifiers))
        {
            SetPreviewOpen(!_previewOpen);
            e.Handled = true;
            return;
        }

        if (Matches(_state.Settings.PinHotKey, key, modifiers))
        {
            _state.History.TogglePin(_state.Navigator.SelectedItem);
            e.Handled = true;
            return;
        }

        if (Matches(_state.Settings.DeleteHotKey, key, modifiers))
        {
            DeleteSelected();
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Escape:
                HidePopup();
                e.Handled = true;
                return;

            case Key.Return:
                _state.Select(ToHotKeyModifiers(modifiers));
                e.Handled = true;
                return;

            case Key.Down when ctrl || alt:
            case Key.Next:
                _state.Navigator.HighlightLast();
                e.Handled = true;
                return;

            case Key.Down:
                _state.Navigator.HighlightNext();
                e.Handled = true;
                return;

            case Key.Up when ctrl || alt:
            case Key.Prior:
                _state.Navigator.HighlightFirst();
                e.Handled = true;
                return;

            case Key.Up:
                _state.Navigator.HighlightPrevious();
                e.Handled = true;
                return;

            // Readline-style bindings, carried over from Maccy.
            case Key.N when ctrl && alt:
                _state.Navigator.HighlightLast();
                e.Handled = true;
                return;

            case Key.N when ctrl:
            case Key.J when ctrl:
                _state.Navigator.HighlightNext();
                e.Handled = true;
                return;

            case Key.P when ctrl && alt:
                _state.Navigator.HighlightFirst();
                e.Handled = true;
                return;

            case Key.P when ctrl:
                _state.Navigator.HighlightPrevious();
                e.Handled = true;
                return;

            case Key.K when ctrl && !_state.Navigator.IsFirstItemHighlighted:
                _state.Navigator.HighlightPrevious();
                e.Handled = true;
                return;

            case Key.U when ctrl:
                SearchBox.Clear();
                e.Handled = true;
                return;

            case Key.H when ctrl:
                DeleteCharacterFromSearch();
                e.Handled = true;
                return;

            case Key.W when ctrl:
                DeleteWordFromSearch();
                e.Handled = true;
                return;

            // Ctrl+Alt+Backspace clears; adding Shift clears pinned items too.
            case Key.Back when ctrl && alt:
                InvokeFooter(shift ? _state.Footer.ClearAll : _state.Footer.Clear);
                e.Handled = true;
                return;

            case Key.OemComma when ctrl:
                _state.OpenPreferences();
                e.Handled = true;
                return;

            case Key.Q when ctrl:
                _state.Quit();
                e.Handled = true;
                return;
        }

        // ---- direct row shortcuts: Ctrl/Alt + digit, or Ctrl/Alt + pinned letter
        if ((ctrl || alt) && TryActivateShortcut(key, ToHotKeyModifiers(modifiers)))
        {
            e.Handled = true;
        }
    }

    private static bool Matches(HotKey hotKey, Key key, ModifierKeys modifiers) =>
        hotKey.IsValid && hotKey.Key == key && hotKey.Modifiers == ToHotKeyModifiers(modifiers);

    private static HotKeyModifiers ToHotKeyModifiers(ModifierKeys modifiers)
    {
        var result = HotKeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= HotKeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= HotKeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= HotKeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= HotKeyModifiers.Windows;
        return result;
    }

    private bool TryActivateShortcut(Key key, HotKeyModifiers modifiers)
    {
        var character = key switch
        {
            >= Key.D1 and <= Key.D9 => ((char)('1' + (key - Key.D1))).ToString(),
            >= Key.NumPad1 and <= Key.NumPad9 => ((char)('1' + (key - Key.NumPad1))).ToString(),
            >= Key.A and <= Key.Z => key.ToString().ToLowerInvariant(),
            _ => null
        };

        if (character is null)
        {
            return false;
        }

        var item = _state.History.FindByShortcut(character);
        if (item is null)
        {
            return false;
        }

        // Only act when the modifiers actually spell out one of the three actions.
        if (ItemActions.FromModifiers(modifiers, _state.Settings) == ItemAction.Unknown)
        {
            return false;
        }

        _state.Navigator.Select(item, scroll: false);
        _state.History.Select(item, modifiers);
        return true;
    }

    private void DeleteSelected()
    {
        var item = _state.Navigator.SelectedItem;
        if (item is null)
        {
            return;
        }

        var next = _state.Navigator.NearestTo(item);
        _state.History.Delete(item);
        _state.Navigator.Select(next);
    }

    private void InvokeFooter(FooterItem item)
    {
        _state.Navigator.Select(item);

        if (item.NeedsConfirmation && !_state.Settings.SuppressClearAlert)
        {
            item.ShowConfirmation = true;
            return;
        }

        item.Action();
    }

    private void DeleteCharacterFromSearch()
    {
        if (SearchBox.Text.Length == 0)
        {
            return;
        }

        SearchBox.Text = SearchBox.Text[..^1];
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private void DeleteWordFromSearch()
    {
        var text = SearchBox.Text.TrimEnd();
        var lastSpace = text.LastIndexOf(' ');

        SearchBox.Text = lastSpace < 0 ? string.Empty : text[..lastSpace];
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The popup lives for the whole session; closing it just hides it. On a real
        // shutdown the flag lets it go, otherwise Application.Shutdown would stall.
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            HidePopup();
        }
    }
}

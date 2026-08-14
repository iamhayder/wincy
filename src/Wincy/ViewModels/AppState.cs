using System.IO;
using System.Windows;
using Wincy.Interop;
using Wincy.Services;
using Wincy.Views;

namespace Wincy.ViewModels;

/// <summary>How the popup should behave for the current press of the global hotkey.</summary>
internal enum PopupState
{
    /// <summary>Default: the hotkey toggles the popup open and closed.</summary>
    Toggle,

    /// <summary>The hotkey was just pressed and we do not yet know which gesture it is.</summary>
    Opening,

    /// <summary>Modifiers are held; each further press of the key steps down the list.</summary>
    Cycle
}

/// <summary>
/// Application-wide wiring: services, the tray icon, the global hotkey and the popup
/// lifecycle. Everything the views need hangs off here.
/// </summary>
public sealed class AppState : IDisposable
{
    public static AppState Current { get; private set; } = null!;

    public SettingsService SettingsService { get; }

    public AppSettings Settings => SettingsService.Current;

    public HistoryStore Store { get; }

    public ClipboardService Clipboard { get; }

    public HistoryViewModel History { get; }

    public NavigationManager Navigator { get; }

    public FooterViewModel Footer { get; }

    public Notifier Notifier { get; }

    public AppIconCache Icons { get; } = new();

    public MessageWindow MessageWindow { get; }

    public ModifierWatcher Modifiers { get; } = new();

    public string DataDirectory { get; }

    private readonly HotKeyManager _hotKeys;
    private TrayIcon? _tray;
    private PopupWindow? _popup;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;

    private PopupState _popupState = PopupState.Toggle;
    private IntPtr _previousForeground = IntPtr.Zero;
    private bool _disposed;

    /// <summary>Whether the search field should be on screen right now.</summary>
    public bool IsSearchVisible => Settings.ShowSearch &&
                                   (Settings.SearchVisibility == SearchVisibility.Always ||
                                    !string.IsNullOrEmpty(History.SearchQuery));

    public AppState()
    {
        Current = this;

        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wincy");

        Log.Initialize(DataDirectory);
        Log.Info("Wincy starting");

        SettingsService = new SettingsService(Path.Combine(DataDirectory, "settings.json"));
        Store = new HistoryStore(Path.Combine(DataDirectory, "history.db"));

        MessageWindow = new MessageWindow();
        SystemTheme.Attach(MessageWindow);

        Notifier = new Notifier(Settings);
        Clipboard = new ClipboardService(MessageWindow, Settings);
        History = new HistoryViewModel(Settings, Store, Clipboard, Icons, Notifier);

        Footer = new FooterViewModel(
            Settings,
            clear: History.Clear,
            clearAll: History.ClearAll,
            preferences: OpenPreferences,
            about: OpenAbout,
            quit: Quit);

        Navigator = new NavigationManager(History, Footer, Settings);

        _hotKeys = new HotKeyManager(MessageWindow);

        Store.CleanupOrphanedContents();
        History.Load();

        History.PasteAction = PasteToPreviousWindow;

        Clipboard.NewCopy += OnNewCopy;
        Clipboard.ExternalChange += OnExternalClipboardChange;
        History.ItemsChanged += Navigator.Reanchor;
        SettingsService.Changed += OnSettingChanged;
        Modifiers.AllModifiersReleased += OnAllModifiersReleased;
    }

    public void Start(PopupWindow popup)
    {
        _popup = popup;

        Clipboard.Start();
        RegisterHotKeys();
        SetUpTray();

        // Reconcile the Run key with the stored preference in case the user removed it
        // outside the app.
        if (Settings.LaunchAtLogin != StartupManager.IsEnabled())
        {
            StartupManager.SetEnabled(Settings.LaunchAtLogin);
        }
    }

    // ------------------------------------------------------------------ hotkeys

    private void RegisterHotKeys()
    {
        var registered = _hotKeys.Register("popup", Settings.PopupHotKey, OnPopupHotKey);

        if (!registered)
        {
            MessageBox.Show(
                $"Wincy could not register {Settings.PopupHotKey}. Another application is already using it.\n\n" +
                "Pick a different shortcut in Preferences → General.",
                "Wincy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnPopupHotKey()
    {
        if (_popup is null)
        {
            return;
        }

        if (!_popup.IsOpen)
        {
            _popupState = PopupState.Opening;
            ShowPopup();
            return;
        }

        switch (_popupState)
        {
            case PopupState.Opening:
                // The modifiers are still held and the key was tapped again: this is the
                // cycle gesture, not a toggle.
                _popupState = PopupState.Cycle;
                Navigator.IsKeyboardNavigating = true;
                Navigator.HighlightNext(allowCycle: true);
                break;

            case PopupState.Cycle:
                Navigator.IsKeyboardNavigating = true;
                Navigator.HighlightNext(allowCycle: true);
                break;

            default:
                HidePopup();
                break;
        }
    }

    private void OnAllModifiersReleased()
    {
        // The watcher runs on the hook thread; hop back to the UI.
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            switch (_popupState)
            {
                case PopupState.Cycle:
                    _popupState = PopupState.Toggle;
                    Select(HotKeyModifiers.None);
                    break;

                case PopupState.Opening:
                    _popupState = PopupState.Toggle;
                    break;
            }
        });
    }

    // -------------------------------------------------------------------- popup

    public void ShowPopup()
    {
        if (_popup is null)
        {
            return;
        }

        // Remember who had focus so a paste can be delivered back to them.
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != WindowEffects.HandleOf(_popup))
        {
            _previousForeground = foreground;
        }

        _popup.ShowPopup();
        Modifiers.Start();
    }

    public void HidePopup()
    {
        _popupState = PopupState.Toggle;
        Modifiers.Stop();
        _popup?.HidePopup();
    }

    public void TogglePopup()
    {
        if (_popup?.IsOpen == true)
        {
            HidePopup();
        }
        else
        {
            ShowPopup();
        }
    }

    public IntPtr PreviousForegroundWindow => _previousForeground;

    /// <summary>Restores focus to the app the user came from. Called just before pasting.</summary>
    public void RestorePreviousFocus() => Paster.RestoreForeground(_previousForeground);

    /// <summary>
    /// Hands focus back to the window the user came from and sends Ctrl+V there.
    ///
    /// The short delay matters: the popup has only just been hidden, and firing
    /// SendInput before Windows has finished moving the foreground would deliver the
    /// keystroke into the void — or worse, into the wrong application.
    /// </summary>
    private void PasteToPreviousWindow()
    {
        var target = _previousForeground;

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            Paster.RestoreForeground(target);
            Paster.SendPaste();

            // Each paste consumes one entry from a queued stack.
            History.AdvancePasteStack();
        };

        timer.Start();
    }

    /// <summary>
    /// Activates whatever is selected. With no selection at all, the typed search text
    /// itself is copied — the same fallback Maccy offers.
    /// </summary>
    public void Select(HotKeyModifiers modifiers)
    {
        if (Navigator.SelectedItem is { } item)
        {
            if (Navigator.IsMultiSelectInProgress)
            {
                History.StartPasteStack(Navigator.MultiSelection, modifiers);
            }
            else
            {
                History.Select(item, modifiers);
            }

            return;
        }

        if (Navigator.SelectedFooterItem is { } footerItem)
        {
            if (footerItem.NeedsConfirmation && !Settings.SuppressClearAlert)
            {
                footerItem.ShowConfirmation = true;
            }
            else
            {
                footerItem.Action();
            }

            return;
        }

        if (!string.IsNullOrEmpty(History.SearchQuery))
        {
            HidePopup();
            Clipboard.CopyText(History.SearchQuery);
            History.SearchQuery = string.Empty;
        }
    }

    // ------------------------------------------------------------------ capture

    private void OnNewCopy(Models.ClipItem item)
    {
        History.Add(item);
        UpdateTrayTooltip();
    }

    private void OnExternalClipboardChange()
    {
        // A copy from another app ends any queued paste stack, exactly as in Maccy.
        History.InterruptPasteStack();
    }

    // ----------------------------------------------------------------- settings

    private void OnSettingChanged(string? name)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            switch (name)
            {
                case nameof(AppSettings.PopupHotKey):
                    RegisterHotKeys();
                    break;

                case nameof(AppSettings.SortBy):
                case nameof(AppSettings.PinTo):
                    History.Resort();
                    break;

                case nameof(AppSettings.ShowSpecialSymbols):
                    History.RefreshTitles();
                    break;

                case nameof(AppSettings.ImageMaxHeight):
                case nameof(AppSettings.ShowApplicationIcons):
                case nameof(AppSettings.ShowHexColorSwatch):
                case nameof(AppSettings.HighlightMatch):
                    History.RefreshAppearance();
                    break;

                case nameof(AppSettings.PasteByDefault):
                    History.UpdateShortcuts();
                    break;

                case nameof(AppSettings.LaunchAtLogin):
                    StartupManager.SetEnabled(Settings.LaunchAtLogin);
                    break;

                case nameof(AppSettings.ShowInTray):
                    if (Settings.ShowInTray)
                    {
                        _tray?.Show();
                    }
                    else
                    {
                        _tray?.Hide();
                    }

                    break;

                case nameof(AppSettings.TrayIcon):
                    _tray?.SetIcon(TrayIconFactory.Create(Settings.TrayIcon));
                    break;

                case nameof(AppSettings.IgnoreEvents):
                    UpdateTrayTooltip();
                    break;
            }
        });
    }

    // --------------------------------------------------------------------- tray

    private void SetUpTray()
    {
        _tray = new TrayIcon(MessageWindow, TrayIconFactory.Create(Settings.TrayIcon), "Wincy — clipboard history");
        Notifier.Attach(_tray);

        _tray.Clicked += modifiers =>
        {
            // Alt-click pauses recording; Alt+Shift-click skips only the next copy.
            // Both mirror Maccy's option-click on the menu bar icon.
            if (modifiers.HasFlag(HotKeyModifiers.Alt) && modifiers.HasFlag(HotKeyModifiers.Shift))
            {
                Settings.IgnoreOnlyNextEvent = true;
                Settings.IgnoreEvents = true;
                return;
            }

            if (modifiers.HasFlag(HotKeyModifiers.Alt))
            {
                Settings.IgnoreOnlyNextEvent = false;
                Settings.IgnoreEvents = !Settings.IgnoreEvents;
                return;
            }

            TogglePopup();
        };

        _tray.ContextMenuRequested += TrayMenu.Show;

        if (Settings.ShowInTray)
        {
            _tray.Show();
        }

        UpdateTrayTooltip();
    }

    public void UpdateTrayTooltip()
    {
        if (_tray is null)
        {
            return;
        }

        var tooltip = "Wincy — clipboard history";

        if (Settings.IgnoreEvents)
        {
            tooltip += " (paused)";
        }
        else if (Settings.ShowRecentCopyInTooltip && History.MostRecentText is { Length: > 0 } recent)
        {
            tooltip += "\n" + recent;
        }

        _tray.SetTooltip(tooltip);
    }

    // ------------------------------------------------------------------ windows

    public void OpenPreferences()
    {
        HidePopup();

        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void OpenAbout()
    {
        HidePopup();

        if (_aboutWindow is null || !_aboutWindow.IsLoaded)
        {
            _aboutWindow = new AboutWindow();
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        }

        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    public void Quit()
    {
        if (Settings.ClearOnQuit)
        {
            History.ClearAll();
        }

        App.IsShuttingDown = true;
        Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Modifiers.Dispose();
        _hotKeys.Dispose();
        _tray?.Dispose();
        Clipboard.Dispose();
        SettingsService.Dispose();
        Store.Dispose();
        MessageWindow.Dispose();

        Log.Info("Wincy stopped");
    }
}

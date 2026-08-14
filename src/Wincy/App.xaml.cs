using System.Windows;
using System.Windows.Media;
using Wincy.Interop;
using Wincy.ViewModels;
using Wincy.Views;

namespace Wincy;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Wincy.SingleInstance";

    /// <summary>Set just before Application.Shutdown so windows stop cancelling their close.</summary>
    public static bool IsShuttingDown { get; set; }

    private Mutex? _instanceMutex;
    private AppState? _state;
    private PopupWindow? _popup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second copy would fight the first over the clipboard listener and the hotkey.
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Wincy is already running. Look for it in the notification area.",
                "Wincy", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception on the UI thread", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);

        ApplyTheme();
        SystemTheme.Changed += () => Dispatcher.InvokeAsync(ApplyTheme);

        _state = new AppState();

        _popup = new PopupWindow(_state);
        _state.Start(_popup);

        SystemTheme.Changed += () => Dispatcher.InvokeAsync(() => _popup?.RefreshTheme());
    }

    /// <summary>
    /// Rewrites the colour resources from the live system theme. Everything in the UI
    /// binds these with DynamicResource, so switching Windows to dark mode restyles the
    /// app without a restart.
    /// </summary>
    public void ApplyTheme()
    {
        var dark = SystemTheme.IsDark();
        var accent = SystemTheme.Accent();
        var onAccent = SystemTheme.ForegroundFor(accent);

        void SetColor(string key, Color value) => Resources[key] = value;

        if (dark)
        {
            SetColor("WindowBackgroundColor", Color.FromRgb(0x20, 0x20, 0x20));
            SetColor("TextColor", Color.FromRgb(0xF2, 0xF2, 0xF2));
            SetColor("SecondaryTextColor", Color.FromRgb(0x9A, 0x9A, 0x9A));
            SetColor("SeparatorColor", Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
            SetColor("FieldBackgroundColor", Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            SetColor("HoverBackgroundColor", Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
            SetColor("BadgeBackgroundColor", Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
            SetColor("HighlightColor", Color.FromRgb(0xFF, 0xD5, 0x4F));
        }
        else
        {
            SetColor("WindowBackgroundColor", Color.FromRgb(0xF6, 0xF6, 0xF6));
            SetColor("TextColor", Color.FromRgb(0x1A, 0x1A, 0x1A));
            SetColor("SecondaryTextColor", Color.FromRgb(0x66, 0x66, 0x66));
            SetColor("SeparatorColor", Color.FromArgb(0x24, 0x00, 0x00, 0x00));
            SetColor("FieldBackgroundColor", Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            SetColor("HoverBackgroundColor", Color.FromArgb(0x10, 0x00, 0x00, 0x00));
            SetColor("BadgeBackgroundColor", Color.FromArgb(0x20, 0x00, 0x00, 0x00));
            SetColor("HighlightColor", Color.FromRgb(0xB4, 0x6A, 0x00));
        }

        SetColor("AccentColor", accent);
        SetColor("AccentTextColor", onAccent);

        // SolidColorBrush.Color does not follow a DynamicResource once the brush is
        // built, so the brushes are replaced outright.
        void SetBrush(string key, Color value)
        {
            var brush = new SolidColorBrush(value);
            brush.Freeze();
            Resources[key] = brush;
        }

        SetBrush("WindowBackgroundBrush", (Color)Resources["WindowBackgroundColor"]);
        SetBrush("TextBrush", (Color)Resources["TextColor"]);
        SetBrush("SecondaryTextBrush", (Color)Resources["SecondaryTextColor"]);
        SetBrush("AccentBrush", accent);
        SetBrush("AccentTextBrush", onAccent);
        SetBrush("SeparatorBrush", (Color)Resources["SeparatorColor"]);
        SetBrush("FieldBackgroundBrush", (Color)Resources["FieldBackgroundColor"]);
        SetBrush("HoverBackgroundBrush", (Color)Resources["HoverBackgroundColor"]);
        SetBrush("BadgeBackgroundBrush", (Color)Resources["BadgeBackgroundColor"]);
        SetBrush("HighlightBrush", (Color)Resources["HighlightColor"]);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _state?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

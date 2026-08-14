using System.Windows;
using System.Windows.Controls;
using Wincy.Interop;
using Wincy.ViewModels;

namespace Wincy.Views;

/// <summary>The right-click menu on the tray icon.</summary>
public static class TrayMenu
{
    private static ContextMenu? _menu;

    public static void Show(POINT screenPoint)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var state = AppState.Current;

            if (_menu is not null)
            {
                _menu.IsOpen = false;
            }

            var menu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint,
                StaysOpen = false
            };

            menu.Items.Add(Item("Open Wincy", state.TogglePopup));
            menu.Items.Add(new Separator());

            menu.Items.Add(Item(
                state.Settings.IgnoreEvents ? "Resume recording" : "Pause recording",
                () =>
                {
                    state.Settings.IgnoreOnlyNextEvent = false;
                    state.Settings.IgnoreEvents = !state.Settings.IgnoreEvents;
                }));

            menu.Items.Add(Item("Ignore the next copy", () =>
            {
                state.Settings.IgnoreOnlyNextEvent = true;
                state.Settings.IgnoreEvents = true;
            }));

            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Clear unpinned…", state.History.Clear));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Preferences…", state.OpenPreferences));
            menu.Items.Add(Item("About Wincy", state.OpenAbout));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Quit", state.Quit));

            // The tray reports physical pixels; WPF popups are placed in DIPs.
            var monitor = ScreenHelper.FromPoint(screenPoint);
            menu.HorizontalOffset = screenPoint.X / monitor.Scale;
            menu.VerticalOffset = screenPoint.Y / monitor.Scale;

            _menu = menu;
            menu.IsOpen = true;
        });
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}

using System.Diagnostics;
using System.Reflection;
using System.Windows;
using Wincy.ViewModels;

namespace Wincy.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        Mark.Data = TrayIconFactory.Mark();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "Version 1.0" : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppState.Current.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open the data folder: " + ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

using System.Diagnostics;
using Microsoft.Win32;

namespace Wincy.Interop;

/// <summary>
/// "Launch at login", via the per-user Run key. No scheduled task and no admin
/// rights, which keeps Wincy installable by copying a single executable.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Wincy";

    public static string ExecutablePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "Wincy.exe";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains("Wincy", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the Run key: " + ex.Message);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            Log.Info($"Launch at login {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the Run key", ex);
        }
    }
}

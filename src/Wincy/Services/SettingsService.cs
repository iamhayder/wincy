using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wincy.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON next to the database. Saves are
/// debounced because settings controls fire on every keystroke.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly System.Timers.Timer _saveTimer;
    private bool _disposed;

    public AppSettings Current { get; }

    /// <summary>Raised (on the UI thread's timer thread) after any setting changes.</summary>
    public event Action<string?>? Changed;

    public SettingsService(string path)
    {
        _path = path;
        Current = Load(path);

        _saveTimer = new System.Timers.Timer(400) { AutoReset = false };
        _saveTimer.Elapsed += (_, _) => Save();

        Current.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed?.Invoke(e.PropertyName);
        ScheduleSave();
    }

    /// <summary>Call after mutating one of the list properties, which cannot raise change events.</summary>
    public void NotifyListChanged(string name)
    {
        Changed?.Invoke(name);
        ScheduleSave();
    }

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Write to a temp file first so a crash mid-write cannot leave a truncated
            // settings file behind.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings; falling back to defaults", ex);
        }

        return new AppSettings();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Current.PropertyChanged -= OnPropertyChanged;
        _saveTimer.Stop();
        _saveTimer.Dispose();
        Save();
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;

namespace Wincy;

/// <summary>
/// Deliberately tiny: a rolling text log next to the database, plus Debug output.
/// A clipboard manager that crashes silently is impossible to support.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;
    private const long MaxBytes = 1024 * 1024;

    public static void Initialize(string directory)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "wincy.log");

            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var previous = Path.Combine(directory, "wincy.previous.log");
                    File.Delete(previous);
                    File.Move(_path, previous);
                }
            }
            catch
            {
                // Log rotation is best effort.
            }
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        Debug.WriteLine(line);

        lock (Gate)
        {
            if (_path is null)
            {
                return;
            }

            try
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Never let logging take the app down.
            }
        }
    }
}

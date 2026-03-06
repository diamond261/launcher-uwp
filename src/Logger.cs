using System;
using System.IO;
using Flarial.Launcher.Managers;

namespace Flarial.Launcher;

static class Logger
{
    static readonly object s_lock = new();

    static string LogPath => Path.Combine(VersionManagement.launcherPath, "Logs", "launcher.log");

    static void Write(string level, string message)
    {
        try
        {
            lock (s_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    internal static void Info(string message) => Write("INFO", message);

    internal static void Error(string message, Exception exception)
        => Write("ERROR", $"{message}{Environment.NewLine}{exception}");
}

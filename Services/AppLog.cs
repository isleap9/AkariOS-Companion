using System;
using System.IO;
using System.Text;

namespace AkariOSCompanion.Services;

/// <summary>
/// Writes every operation to a log file next to the app so there is a durable
/// record of what was applied (and what failed) after the window closes.
/// Also mirrors to stdout when one is attached, which makes the file tailable
/// from a terminal.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string LogPath =>
        _path ??= Path.Combine(AppContext.BaseDirectory, "akari-operations.log");

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (Gate)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* logging must never break the app */ }

            try { Console.WriteLine(line); } catch { /* no console attached */ }
        }
    }

    public static void Exception(string tag, string? detail)
    {
        try
        {
            Write($"!! {tag}");
            if (!string.IsNullOrWhiteSpace(detail))
                File.AppendAllText(LogPath, detail + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }
}

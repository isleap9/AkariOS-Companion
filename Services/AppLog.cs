using System;
using System.IO;
using System.Runtime.InteropServices;
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
    private const int ATTACH_PARENT_PROCESS = -1;

    private static readonly object Gate = new();
    private static string? _path;
    private static bool _consoleChecked;
    private static Stream? _console;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

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

            WriteToConsole(line);
        }
    }

    /// <summary>
    /// Mirrors to an attached terminal. OutputType is WinExe, so the process starts
    /// with no console — we attach to the parent one on first use so the log is
    /// visible when launched from a terminal, and silently do nothing otherwise.
    /// </summary>
    private static void WriteToConsole(string line)
    {
        try
        {
            if (!_consoleChecked)
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) return;
                _console = Console.OpenStandardOutput();
                _consoleChecked = true;
            }
            if (_console is null) return;
            var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
            _console.Write(bytes, 0, bytes.Length);
            _console.Flush();
        }
        catch { /* console unavailable — the file log is the record of truth */ }
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

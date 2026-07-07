using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AkariOSCompanion.Services;

/// <summary>
/// Shared service — ported 1:1 from the old Akari Tool Companion.
/// Handles script execution, winget installs, URL opening, logging, and progress bar.
/// </summary>
public class ToolService
{
    private readonly TextBox _log;
    private readonly ProgressBar _progress;
    private readonly TextBlock _progressStatus;
    private int _activeProcessCount;

    private static readonly Assembly AppAssembly = typeof(ToolService).Assembly;

    public ToolService(TextBox log, ProgressBar progress, TextBlock progressStatus)
    {
        _log = log;
        _progress = progress;
        _progressStatus = progressStatus;
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    public void Log(string message) =>
        _log.Dispatcher.Invoke(() =>
        {
            _log.AppendText(message + Environment.NewLine);
            _log.ScrollToEnd();
        });

    // ── Progress bar ──────────────────────────────────────────────────────────

    public void StartProgress(string processName)
    {
        _progress.Dispatcher.Invoke(() =>
        {
            _activeProcessCount++;
            _progressStatus.Text = $"Running {processName}...";
            _progressStatus.Visibility = Visibility.Visible;
            _progress.Value = 0;
            _progress.IsIndeterminate = false;
            _progress.Visibility = Visibility.Visible;
        });
    }

    public void StopProgress()
    {
        _progress.Dispatcher.Invoke(() =>
        {
            _activeProcessCount = Math.Max(0, _activeProcessCount - 1);
            if (_activeProcessCount > 0) return;
            _progress.Value = 0;
            _progress.Visibility = Visibility.Collapsed;
            _progressStatus.Visibility = Visibility.Collapsed;
        });
    }

    // ── Action routing ────────────────────────────────────────────────────────

    public async Task RunAction(RunAction action)
    {
        switch (action)
        {
            case ScriptAction script:   await RunScript(script.FileName); break;
            case CommandAction command: await RunWinget(command.Command, command.AppName); break;
            case UrlAction url:         OpenUrl(url.Url); break;
        }
    }

    public async Task RunWithTracking(RunAction action, string title, List<string> applied)
    {
        applied.Add(title);
        Log($"[APPLIED] {title}");
        await RunAction(action);
    }

    // ── Script execution (embedded resource) ─────────────────────────────────

    public async Task RunScript(string scriptName)
    {
        var resourceName = FindEmbeddedScriptResource(scriptName);
        if (resourceName is null)
        {
            Log($"[ERROR] Embedded script not found: {scriptName}");
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"AkariOS-{Guid.NewGuid():N}-{scriptName}");
        try
        {
            await ExtractEmbeddedScript(resourceName, temp);
            Log($"[RUN] {scriptName}");
            await RunProcess("powershell.exe", $"-ExecutionPolicy Bypass -File \"{temp}\"", timeout: null);
            Log("[DONE]");
        }
        finally
        {
            TryDeleteTempScript(temp);
        }
    }

    // ── Winget install ────────────────────────────────────────────────────────

    public async Task RunWinget(string command, string? appName)
    {
        Log($"[INSTALLING] {appName ?? "Application"}...");
        int exit = await RunProcess("powershell.exe",
            $"-NoProfile -Command \"{command}\"", timeout: 600_000);
        Log(exit == 0 ? "[DONE]" : $"[ERROR] Exit code {exit}");
    }

    // ── URL opener ────────────────────────────────────────────────────────────

    public void OpenUrl(string url)
    {
        try
        {
            Log($"[OPENING] {url}");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { Log($"[EXCEPTION] {ex.Message}"); }
    }

    // ── Process runner ────────────────────────────────────────────────────────

    public async Task<int> RunProcess(string fileName, string arguments, int? timeout)
    {
        StartProgress(fileName);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };
            process.ErrorDataReceived  += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[ERROR] {e.Data}"); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var waitTask = process.WaitForExitAsync();
            if (timeout is null)
                await waitTask;
            else if (await Task.WhenAny(waitTask, Task.Delay(timeout.Value)) != waitTask)
            {
                process.Kill(entireProcessTree: true);
                Log("[TIMEOUT]");
                return -1;
            }
            return process.ExitCode;
        }
        catch (Exception ex) { Log($"[EXCEPTION] {ex.Message}"); return -1; }
        finally { StopProgress(); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static SolidColorBrush BrushFrom(string color) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;

    private static string? FindEmbeddedScriptResource(string scriptName) =>
        AppAssembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".Scripts.{scriptName}", StringComparison.OrdinalIgnoreCase));

    private static async Task ExtractEmbeddedScript(string resourceName, string dest)
    {
        await using var src  = AppAssembly.GetManifestResourceStream(resourceName)!;
        await using var file = File.Create(dest);
        await src.CopyToAsync(file);
    }

    private void TryDeleteTempScript(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log($"[WARNING] Could not delete temp script: {ex.Message}"); }
    }
}
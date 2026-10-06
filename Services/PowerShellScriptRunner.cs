using System;
using System.Diagnostics;
using System.Text;

namespace AkariOSCompanion.Services;

/// <summary>
/// Runs an in-memory PowerShell snippet, ported from Akari-Tool's
/// PowerShellRunner (base64 -EncodedCommand via Windows PowerShell).
/// Synchronous: callers run selections on the UI thread, so this caps
/// the wait and kills on timeout instead of hanging the app.
/// </summary>
public static class PowerShellScriptRunner
{
    private const string PowerShellPath =
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    private const int TimeoutMs = 120_000;

    public static bool Run(string script, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(script)) return true;

        // Silence the progress stream: with stdout redirected, progress records
        // arrive as CLIXML noise ("Preparing modules for first use").
        script = "$ProgressPreference='SilentlyContinue';" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo(PowerShellPath,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                log?.Invoke("[SCRIPT] Failed to start PowerShell.");
                return false;
            }

            var outBuilder = new StringBuilder();
            var errBuilder = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) outBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errBuilder.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(TimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                log?.Invoke("[SCRIPT] Timed out after 120s, killed.");
                return false;
            }

            var stdout = outBuilder.ToString().Trim();
            var stderr = errBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(stdout)) log?.Invoke($"[SCRIPT] {stdout}");
            if (!string.IsNullOrWhiteSpace(stderr)) log?.Invoke($"[SCRIPT] {stderr}");
            if (process.ExitCode != 0)
            {
                log?.Invoke($"[SCRIPT] Exited with code {process.ExitCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[SCRIPT] Failed: {ex.Message}");
            return false;
        }
    }
}

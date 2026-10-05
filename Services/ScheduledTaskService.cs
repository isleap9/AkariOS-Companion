using System;
using System.Runtime.InteropServices;

namespace AkariOSCompanion.Services;

/// <summary>
/// Enables / disables Windows scheduled tasks through the Task Scheduler COM API
/// (Schedule.Service), which is the only supported way to flip a task's Enabled flag.
///
/// schtasks /create would rewrite the whole task definition and lose its triggers,
/// so it is deliberately not used here.
/// </summary>
public interface IScheduledTaskService
{
    /// <summary>True when the task exists, regardless of whether it is enabled.</summary>
    bool TaskExists(string taskPath);

    /// <summary>The task's current Enabled flag. Throws if the task does not exist.</summary>
    bool IsTaskEnabled(string taskPath);

    bool SetTaskEnabled(string taskPath, bool enabled, out string? error);
}

public sealed class ScheduledTaskService : IScheduledTaskService
{
    public bool TaskExists(string taskPath)
    {
        object? service = null;
        object? folder = null;
        try
        {
            service = CreateTaskService();
            (folder, _) = GetFolder(service, taskPath);
            return true;
        }
        catch { return false; }
        finally { ReleaseComObject(folder); ReleaseComObject(service); }
    }

    public bool IsTaskEnabled(string taskPath)
    {
        object? service = null;
        object? folder = null;
        object? task = null;
        try
        {
            service = CreateTaskService();
            var (f, name) = GetFolder(service, taskPath);
            folder = f;
            task = GetTask(folder, name);
            return Convert.ToBoolean(task!.GetType()
                .InvokeMember("Enabled", System.Reflection.BindingFlags.GetProperty, null, task, null));
        }
        catch { return false; }
        finally
        {
            ReleaseComObject(task);
            ReleaseComObject(folder);
            ReleaseComObject(service);
        }
    }

    public bool SetTaskEnabled(string taskPath, bool enabled, out string? error)
    {
        object? service = null;
        object? folder = null;
        object? task = null;
        try
        {
            service = CreateTaskService();
            var (f, name) = GetFolder(service, taskPath);
            folder = f;
            task = GetTask(folder, name);

            task!.GetType()
                .InvokeMember("Enabled", System.Reflection.BindingFlags.SetProperty,
                              null, task, new object[] { enabled });

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            ReleaseComObject(task);
            ReleaseComObject(folder);
            ReleaseComObject(service);
        }
    }

    // ── COM plumbing ─────────────────────────────────────────────────────────

    private static object CreateTaskService()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
                   ?? throw new InvalidOperationException("Task Scheduler service unavailable.");
        var svc = Activator.CreateInstance(type)
                  ?? throw new InvalidOperationException("Could not create Task Scheduler service.");
        svc.GetType().InvokeMember("Connect", System.Reflection.BindingFlags.InvokeMethod,
                                   null, svc, null);
        return svc;
    }

    private static (object? Folder, string Name) GetFolder(object service, string taskPath)
    {
        // Task paths look like: \Microsoft\Windows\Application Experience\Some Task
        // The last segment is the task; everything before it is the folder.
        var normalised = taskPath.TrimStart('\\');
        var last = normalised.LastIndexOf('\\');
        var folderPath = last <= 0 ? "\\" : "\\" + normalised[..last];
        var name = last <= 0 ? normalised : normalised[(last + 1)..];

        var folder = service.GetType()
            .InvokeMember("GetFolder", System.Reflection.BindingFlags.InvokeMethod,
                          null, service, new object[] { folderPath });
        return (folder, name);
    }

    private static object? GetTask(object? folder, string name)
    {
        if (folder is null) return null;
        return folder.GetType()
            .InvokeMember("GetTask", System.Reflection.BindingFlags.InvokeMethod,
                          null, folder, new object[] { name });
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is null) return;
        try { Marshal.ReleaseComObject(comObject); } catch { /* best effort */ }
    }
}

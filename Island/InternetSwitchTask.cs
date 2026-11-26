using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Island;

/// <summary>
/// Internet on/off without a UAC prompt on every click. Windows needs administrator rights to disable adapters,
/// so Moku asks once: an elevated PowerShell registers two Task Scheduler tasks for the current user with
/// "run with highest privileges" (\Moku\InternetOff-…, \Moku\InternetOn-…). Their scripts are fixed and contain
/// no user data. Afterwards Moku only starts the matching task: no UAC prompt, no PowerShell window.
/// </summary>
internal static class InternetSwitchTask
{
    enum RunOutcome { Ok, Failed, Missing }

    static string TaskName(bool on) => SystemPanelPolicy.InternetTaskName(on, Environment.UserName);
    static string Account => string.IsNullOrWhiteSpace(Environment.UserDomainName)
        ? Environment.UserName : Environment.UserDomainName + "\\" + Environment.UserName;

    /// <summary>Done = adapters switched; Cancelled = the one-time UAC prompt was declined; Failed = use the fallback.</summary>
    internal static ToggleResult Toggle(bool on)
    {
        var first = RunTask(on);
        if (first == RunOutcome.Ok) return ToggleResult.Done;
        if (first == RunOutcome.Failed) return ToggleResult.Failed;
        var install = Install();               // the only UAC prompt
        if (install != ToggleResult.Done) return install;
        return RunTask(on) == RunOutcome.Ok ? ToggleResult.Done : ToggleResult.Failed;
    }

    internal static bool IsInstalled()
    {
        try
        {
            dynamic folder = Folder();
            folder.GetTask(TaskName(false)); folder.GetTask(TaskName(true));
            return true;
        }
        catch { return false; }
    }

    static dynamic Folder()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new COMException("Task Scheduler is unavailable");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service.GetFolder(SystemPanelPolicy.InternetTaskFolder);
    }

    static RunOutcome RunTask(bool on)
    {
        string name = TaskName(on);
        dynamic task;
        try { task = Folder().GetTask(name); }
        catch { return RunOutcome.Missing; }      // not set up yet (or removed)
        try
        {
            DateTime before = LastRun(task);
            try { task.Run(null); }
            catch (COMException ex) when ((uint)ex.HResult == 0x80070005) { return RunOutcome.Missing; } // no rights: set up again
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(45))
            {
                Thread.Sleep(250);
                dynamic fresh = Folder().GetTask(name);
                int state = (int)fresh.State;               // 2 = queued, 4 = running
                if (state is 2 or 4 || LastRun(fresh) == before) continue;
                int result = (int)fresh.LastTaskResult;
                if (result == 0) return RunOutcome.Ok;
                Log.Error($"Internet task {name} finished with 0x{result:X8}", null);
                return RunOutcome.Failed;
            }
            Log.Error($"Internet task {name} did not finish in time", null);
            return RunOutcome.Failed;
        }
        catch (Exception ex) { Log.Error("Internet task run failed", ex); return RunOutcome.Failed; }
    }

    static DateTime LastRun(dynamic task)
    {
        try { return (DateTime)task.LastRunTime; } catch { return DateTime.MinValue; }
    }

    /// <summary>One elevated PowerShell registers both tasks. Windows shows its UAC prompt here, and only here.</summary>
    internal static ToggleResult Install()
    {
        try
        {
            string script = SystemPanelPolicy.InternetTaskInstallScript(Account, Environment.UserName);
            string ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            using var process = Process.Start(new ProcessStartInfo(ps,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + SystemPanelPolicy.EncodePowerShell(script))
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (process == null || !process.WaitForExit(60000)) return ToggleResult.Failed;
            if (process.ExitCode != 0) { Log.Error("Internet task setup exited with " + process.ExitCode, null); return ToggleResult.Failed; }
            return IsInstalled() ? ToggleResult.Done : ToggleResult.Failed;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return ToggleResult.Cancelled; }
        catch (Exception ex) { Log.Error("Internet task setup failed", ex); return ToggleResult.Failed; }
    }

    /// <summary>Test hook: start only the harmless "on" task and report whether it completed successfully.</summary>
    internal static bool? RunOnTaskForTest()
    {
        var outcome = RunTask(true);
        return outcome == RunOutcome.Missing ? null : outcome == RunOutcome.Ok;
    }
}

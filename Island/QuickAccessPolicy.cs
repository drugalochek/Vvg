namespace Island;

/// <summary>Home shortcuts refer to apps, not just the launcher executable's PID.</summary>
internal static class QuickAccessPolicy
{
    public static bool IsTerminal(string command) => FileName(command) == "powershell.exe";
    static string FileName(string value) => value.Replace('\\', '/').Split('/').Last().ToLowerInvariant();

    public static bool MatchesWindow(string command, string processName, string windowClass)
    {
        string process = processName.ToLowerInvariant();
        return FileName(command) switch
        {
            // explorer.exe also owns the desktop: only reuse real folder windows.
            "explorer.exe" => process == "explorer" && windowClass is "CabinetWClass" or "ExploreWClass",
            "notepad.exe" => process == "notepad",
            "calc.exe" => process is "calculatorapp" or "calculator" or "calc",
            // Windows 11 can host a PowerShell session in Windows Terminal.
            "powershell.exe" => process is "windowsterminal" or "windowsterminalpreview" or "powershell" or "pwsh",
            "taskmgr.exe" => process == "taskmgr",
            _ => false
        };
    }
}

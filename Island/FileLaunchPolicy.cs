using System.Diagnostics;
using System.IO;

namespace Island;

internal static class FileLaunchPolicy
{
    public static bool CanElevate(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".com" or ".lnk" or ".bat" or ".cmd" or ".ps1" or ".msi";
    public static bool IsNativeApplication(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".com" or ".lnk";
    public static ProcessStartInfo Create(string path, bool elevated, string systemDirectory)
    {
        if (elevated && !CanElevate(path)) throw new NotSupportedException("This file type has no administrator launch command.");
        string extension = Path.GetExtension(path).ToLowerInvariant();
        var info = new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? "" };
        if (extension is ".bat" or ".cmd")
        {
            info.FileName = Path.Combine(systemDirectory, "cmd.exe");
            info.Arguments = "/d /s /c \"\"" + path + "\"\"";
        }
        else if (extension == ".ps1")
        {
            info.FileName = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            info.Arguments = "-NoProfile -File \"" + path + "\"";
        }
        else if (extension == ".msi" && elevated)
        {
            info.FileName = Path.Combine(systemDirectory, "msiexec.exe");
            info.Arguments = "/i \"" + path + "\"";
        }
        if (elevated) info.Verb = "runas";
        return info;
    }
}

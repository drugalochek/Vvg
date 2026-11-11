using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace MokuInstaller;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstaller();
            return;
        }
        new MainWindow().Show();
    }

    private static void RunUninstaller()
    {
        string installDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string appPath = Path.Combine(installDir, "Moku.exe");
        try
        {
            foreach (var process in Process.GetProcessesByName("Moku"))
            {
                try
                {
                    string? processPath = process.MainModule?.FileName;
                    if (processPath != null &&
                        string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(appPath), StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch { /* Не завершаем процессы, путь которых не удалось проверить. */ }
            }

            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                run?.DeleteValue("Moku", throwOnMissingValue: false);
            }
            catch { }
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Moku", throwOnMissingSubKey: false);
            }
            catch { }

            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Moku");
            string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Moku.lnk");
            try { File.Delete(Path.Combine(startMenu, "Moku.lnk")); } catch { }
            try { File.Delete(Path.Combine(startMenu, "Uninstall Moku.lnk")); } catch { }
            try { File.Delete(desktopShortcut); } catch { }
            try { if (Directory.Exists(startMenu)) Directory.Delete(startMenu, recursive: true); } catch { }

            string script = Path.Combine(Path.GetTempPath(), "Moku-Uninstall-" + Guid.NewGuid().ToString("N") + ".cmd");
            string safeDir = installDir.Replace("%", "%%");
            File.WriteAllText(script,
                "@echo off\r\ntimeout /t 3 /nobreak >nul\r\nrmdir /s /q \"" + safeDir + "\"\r\ndel \"%~f0\"\r\n",
                Encoding.Default);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"\"" + script + "\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            MessageBox.Show("Moku has been removed.", "Moku", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not complete uninstall: " + ex.Message, "Moku",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Shutdown(); }
    }
}

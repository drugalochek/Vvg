using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace MokuInstaller;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The uninstaller is a renamed copy of this executable. Detect its mode
        // from the executable name as well as command-line switches, because
        // Windows shortcut / registry launchers may omit or alter arguments.
        string executableName = Path.GetFileName(Environment.ProcessPath ?? "");
        bool launchedAsUninstaller = string.Equals(
            executableName,
            "Moku-Uninstall.exe",
            StringComparison.OrdinalIgnoreCase);

        bool uninstall = launchedAsUninstaller || e.Args.Any(a =>
            string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase));

        new MainWindow(uninstall).Show();
    }
}

using System;
using System.Linq;
using System.Windows;

namespace MokuInstaller;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool uninstall = e.Args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));
        new MainWindow(uninstall).Show();
    }
}

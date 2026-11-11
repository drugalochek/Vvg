using System.Windows;
using System.Diagnostics;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace Island;

public partial class App : Application
{
    const string MutexName = "Moku.SingleInstance.Mutex";
    const string ShowEventName = "Moku.ShowRequest";
    const string AckEventName = "Moku.ShowAck";
    bool _failShown;

    // Окно не создалось: раньше ошибка глоталась и процесс висел без окна и трея. Теперь — видно и завершаем.
    void Fail(string what, Exception? ex = null)
    {
        if (ex != null) Log.Error(what, ex); else Log.Error(what);
        if (_failShown) return;
        _failShown = true;
        var text = what + (ex == null ? "" : "\n\n" + ex.GetType().Name + ": " + ex.Message + (ex.InnerException != null ? "\n→ " + ex.InnerException.Message : "") +
                           "\n\n" + (ex.StackTrace ?? "").Split('\n').Take(6).Aggregate("", (a, b) => a + b.Trim() + "\n")) +
                   "\nЛог: %APPDATA%\\Moku\\moku.log\n(Ctrl+C копирует этот текст)";
        System.Windows.MessageBox.Show(text, "Moku — ошибка запуска", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        Shutdown();
    }
    Mutex? _mutex;
    CancellationTokenSource? _cts;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, a) =>
        {
            // Ошибка пишется в лог, а приложение не падает из-за одного неудачного обработчика.
            a.Handled = true;
            if (MainWindow == null || !MainWindow.IsLoaded) Fail("Остров не смог запуститься.", a.Exception);
            else Log.Error("Необработанное исключение UI-потока", a.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            Log.Error("Фатальное исключение", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Log.Error("Необработанное исключение задачи", a.Exception);
            a.SetObserved();
        };

        _mutex = new Mutex(true, MutexName, out bool created);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!created)
        {
            // Уже запущен: просим первый экземпляр показать окно и выходим.
            var ack = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName);
            ack.Reset();
            showEvent.Set();
            bool shown = ack.WaitOne(2500);
            if (!shown)
            {
                // Первая копия висит без окна — убиваем её и стартуем заново.
                var me = Environment.ProcessId;
                foreach (var p in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
                    if (p.Id != me) { try { p.Kill(true); p.WaitForExit(3000); } catch { } }
                try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); } catch { }
            }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Create the window explicitly so constructor/XAML failures are logged with
        // their original exception instead of being hidden inside StartupUri reflection.
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Fail("Не удалось создать главное окно Moku.", ex);
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        new Thread(() =>
        {
            var handles = new WaitHandle[] { showEvent, token.WaitHandle };
            var ackEv = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName);
            while (WaitHandle.WaitAny(handles) == 0)
            {
                try
                {
                    bool vis = false;
                    Dispatcher.Invoke(() => { if (MainWindow is MainWindow w) { w.ShowFromTray(); vis = w.IsVisible; } },
                        DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(2));
                    if (vis) ackEv.Set();
                }
                catch { }
            }
        }) { IsBackground = true, Name = "Moku.ShowListener" }.Start();
        Log.Info("Moku запущен");
        var wd = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        wd.Tick += (_, _) =>
        {
            wd.Stop();
            if (MainWindow == null) Fail("Окно острова не было создано.");
            else if (!MainWindow.IsVisible) (MainWindow as MainWindow)?.ShowFromTray();
        };
        wd.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _mutex?.Dispose();
        Log.Info("Moku завершён");
        base.OnExit(e);
    }
}

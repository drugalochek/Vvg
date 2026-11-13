using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace MokuInstaller;

public partial class MainWindow : Window
{
    bool _en, _installing, _done, _uninstallMode;
    int _page;
    FrameworkElement? _anchor;
    DispatcherTimer? _hop;

    // глаза, эмоции и подсказки — в MainWindow.Mood.cs

    string T(string ru, string en) => _en ? en : ru;

    public MainWindow(bool uninstallMode = false)
    {
        _uninstallMode = uninstallMode;
        InitializeComponent();
        var wa = SystemParameters.WorkArea;
        double k = Math.Min(1.0, Math.Min(wa.Width * 0.92 / 600, wa.Height * 0.92 / 440));
        Width = 600 * k; Height = 440 * k;   // окно всегда целиком помещается на экран
        FolderBox.Text = _uninstallMode ? Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Moku");
        if (_uninstallMode)
        {
            FolderBox.IsReadOnly = true;
            BrowseButton.Visibility = Visibility.Collapsed;
            LaunchCheck.Visibility = Visibility.Collapsed;
            KeepSettingsCheck.IsChecked = true;
        }
        _en = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ru";
        AppLanguageEnglish.IsChecked = _en;
        AppLanguageRussian.IsChecked = !_en;
        if (_uninstallMode) AppLanguagePanel.Visibility = Visibility.Collapsed;
        InitMood();
        CompositionTarget.Rendering += Frame;
        Closed += (_, _) => CompositionTarget.Rendering -= Frame;
        Refresh();
        SetAnchor(Step0);
    }

    // ---------- тексты и состояние ----------
    void Refresh()
    {
        Title = _uninstallMode ? T("Удаление Moku", "Moku Uninstaller") : T("Установка Moku", "Moku Setup");
        ModeLabel.Text = _uninstallMode ? "  /  UNINSTALL" : "  /  SETUP";
        var names = _uninstallMode ? new[] { T("Подтверждение", "Confirm"), T("Параметры", "Options"), T("Удаление", "Uninstall"), T("Готово", "Done") } : new[] { T("Лицензия", "License"), T("Папка", "Location"), T("Установка", "Install"), T("Готово", "Done") };
        var steps = new[] { Step0, Step1, Step2, Step3 };
        for (int i = 0; i < 4; i++)
        {
            steps[i].Text = (i < _page || _done ? "✓  " : "") + names[i];
            steps[i].Foreground = new SolidColorBrush(i == _page && !_done ? Color.FromRgb(247, 248, 252) : i < _page || _done ? Color.FromRgb(140, 146, 160) : Color.FromRgb(90, 96, 110));
            steps[i].FontWeight = i == _page && !_done ? FontWeights.SemiBold : FontWeights.Normal;
        }
        Page0.Visibility = _page == 0 ? Visibility.Visible : Visibility.Collapsed;
        Page1.Visibility = _page == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = _page == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = _page == 3 ? Visibility.Visible : Visibility.Collapsed;
        RuButton.Opacity = _en ? .5 : 1; EnButton.Opacity = _en ? 1 : .5;
        BackButton.Content = T("← Назад", "← Back");
        BackButton.Visibility = _page == 1 ? Visibility.Visible : Visibility.Hidden;
        LicenseBox.Text = _uninstallMode ? T("Будет удалена программа Moku и её ярлыки. Настройки и пользовательские данные сохранятся, если оставить соответствующий пункт включённым. Для завершения удаления Moku будет закрыта.", "Moku and its shortcuts will be removed. Settings and user data will be kept if the corresponding option remains enabled. Moku will be closed to complete the removal.") : T(
            "Moku — бесплатная программа «как есть», без гарантий.\r\n\r\n1. Moku работает локально и не требует аккаунта. История буфера обмена и настройки хранятся только на этом компьютере.\r\n2. Для погоды приложение может обращаться к сервису прогноза, для медиа — к системным данным Windows.\r\n3. Вы можете пользоваться, копировать и удалять Moku свободно. Автор не отвечает за возможный ущерб от использования.\r\n4. Moku можно удалить в любой момент: закройте его в трее и удалите папку установки.",
            "Moku is a free program provided \"as is\", without warranty.\r\n\r\n1. Moku works locally and needs no account. Clipboard history and settings stay on this computer.\r\n2. The app may contact a weather service for the forecast and uses Windows system data for media.\r\n3. You may use, copy and remove Moku freely. The author is not liable for any damage from its use.\r\n4. To uninstall, exit Moku from the tray and delete the install folder.");
        AgreeCheck.Content = _uninstallMode ? T("Я действительно хочу удалить Moku", "I want to uninstall Moku") : T("Я принимаю условия лицензии", "I accept the license terms");
        FolderLabel.Text = _uninstallMode ? T("ТЕКУЩАЯ ПАПКА ПРОГРАММЫ", "CURRENT INSTALL LOCATION") : T("ПАПКА УСТАНОВКИ", "INSTALL LOCATION");
        AppLanguageLabel.Text = T("ЯЗЫК ПРИЛОЖЕНИЯ ПРИ ПЕРВОМ ЗАПУСКЕ", "APP LANGUAGE ON FIRST LAUNCH");
        BrowseButton.Content = T("Выбрать", "Choose");
        DesktopCheck.Content = _uninstallMode ? T("Удалить ярлык с рабочего стола", "Remove desktop shortcut") : T("Ярлык на рабочем столе", "Desktop shortcut");
        AutoCheck.Content = _uninstallMode ? T("Отключить запуск вместе с Windows", "Disable startup with Windows") : T("Запускать вместе с Windows", "Start with Windows");
        KeepSettingsCheck.Content = T("Сохранить настройки и данные пользователя", "Keep settings and user data");
        KeepSettingsCheck.Visibility = _uninstallMode ? Visibility.Visible : Visibility.Collapsed;
        LaunchCheck.Content = T("Запустить Moku сейчас", "Launch Moku now");
        string hint = "";
        switch (_page)
        {
            case 0:
                Headline.Text = _uninstallMode ? T("Удалить Moku?", "Remove Moku?") : T("Лицензия", "License");
                hint = _uninstallMode
                    ? (AgreeCheck.IsChecked == true ? T("Подтверждение принято. Переходи к параметрам удаления.", "Confirmation accepted. Continue to uninstall options.") : T("Подтверди удаление, чтобы продолжить.", "Confirm removal to continue."))
                    : AgreeCheck.IsChecked == true ? T("Отлично. Жми «Далее».", "Great. Press Next.") : T("Сначала согласись с условиями, потом пойдём дальше.", "First accept the terms, then we'll move on.");
                break;
            case 1:
                Headline.Text = _uninstallMode ? T("Что удалить?", "What should be removed?") : T("Куда поставим?", "Where to install?");
                hint = _uninstallMode
                    ? T("Выбери, удалять ли ярлык и автозапуск. Настройки сохраняются по умолчанию.", "Choose whether to remove the shortcut and startup entry. Settings are kept by default.")
                    : string.IsNullOrWhiteSpace(FolderBox.Text) ? T("Выбери папку установки.", "Choose an install folder.") : T("Можно оставить как есть и жать «Установить».", "Keep the default and press Install if you like.");
                break;
            case 2:
                Headline.Text = _uninstallMode ? T("Удаляю…", "Uninstalling…") : T("Устанавливаю…", "Installing…");
                hint = T("Не закрывай окно, это быстро.", "Keep this window open, it's quick.");
                break;
            default:
                Headline.Text = T("Готово!", "All set!");
                hint = _uninstallMode ? T("Moku удалена. Можно закрыть это окно.", "Moku has been removed. You can close this window.") : T("Остров появится сверху экрана, а значок — в трее (возможно, за стрелкой «^»).", "The island appears at the top of the screen; the icon is in the tray (maybe behind the \"^\" arrow).");
                break;
        }
        HintText.Text = hint;
        NextButton.Content = _page switch { 0 => T("Далее →", "Next →"), 1 => _uninstallMode ? T("Удалить", "Uninstall") : T("Установить", "Install"), 2 => T("Подожди…", "Please wait…"), _ => T("Готово", "Finish") };
        NextButton.IsEnabled = _page switch { 0 => AgreeCheck.IsChecked == true, 1 => _uninstallMode || !string.IsNullOrWhiteSpace(FolderBox.Text), 2 => false, _ => true };
        AnimateTexts();
    }

    void GoPage(int p)
    {
        _page = p; Refresh();
        OnPageEntered(p);
        SetAnchor(new[] { Step0, Step1, Step2, Step3 }[p]);
        Hop(900, () => SetAnchor(_page switch
        {
            0 => AgreeCheck.IsChecked == true ? NextButton : AgreeCheck,
            1 => FolderBox,
            2 => Progress,
            _ => NextButton
        }));
    }

    void Hop(int ms, Action a)
    {
        _hop?.Stop();
        _hop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        _hop.Tick += (_, _) => { _hop!.Stop(); a(); };
        _hop.Start();
    }

    void SetAnchor(FrameworkElement el) => _anchor = el;

    void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) => _lastMove = DateTime.Now;
    void Window_Loaded(object sender, RoutedEventArgs e) { Activate(); Hop(700, () => SetAnchor(AgreeCheck)); GreetOnLoad(); }
    void Drag_Down(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

    // ---------- события ----------
    void Ru_Click(object s, RoutedEventArgs e) { if (_en) Wink(); _en = false; Refresh(); }
    void En_Click(object s, RoutedEventArgs e) { if (!_en) Wink(); _en = true; Refresh(); }
    void Close_Click(object s, RoutedEventArgs e)
    {
        if (_installing) { RefuseClose(); return; }
        Feel(Mood.Surprised, 1.0);
        BeginClose();
    }
    void Agree_Changed(object s, RoutedEventArgs e)
    {
        Refresh();
        SetAnchor(AgreeCheck.IsChecked == true ? NextButton : AgreeCheck);
        if (IsLoaded) AgreeReaction(AgreeCheck.IsChecked == true);
    }
    void Folder_Changed(object s, TextChangedEventArgs e) { if (IsLoaded) { Refresh(); FolderReaction(); Hop(1200, () => SetAnchor(NextButton)); } }
    void Back_Click(object s, RoutedEventArgs e) { if (_page == 1 && !_installing) GoPage(0); }

    void Browse_Click(object s, RoutedEventArgs e)
    {
        BrowseStarted();
        var d = new OpenFolderDialog { Title = T("Выбери папку для Moku", "Choose Moku folder"), InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null };
        bool chosen = d.ShowDialog(this) == true;
        if (chosen) { FolderBox.Text = d.FolderName; SetAnchor(NextButton); }
        BrowseFinished(chosen);
    }

    async void Next_Click(object s, RoutedEventArgs e)
    {
        if (_installing) return;
        if (_page == 0) { if (AgreeCheck.IsChecked == true) GoPage(1); return; }
        if (_page == 1) { if (_uninstallMode) await UninstallAsync(); else await InstallAsync(); return; }
        if (_page == 3) Finish();
    }

    async Task UninstallAsync()
    {
        string installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(FolderBox.Text));
        string root = Path.GetPathRoot(installDir) ?? "";
        if (string.Equals(installDir, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
        {
            OnError();
            MessageBox.Show(this, T("Путь установки выглядит небезопасно. Удаление отменено.", "The install path is unsafe. Uninstall was cancelled."), "Moku", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string appPath = Path.Combine(installDir, "Moku.exe");
        try
        {
            _installing = true; GoPage(2);
            Status(T("ШАГ 1 ИЗ 3  ·  ЗАКРЫВАЮ MOKU", "STEP 1 OF 3  ·  CLOSING MOKU"));
            Progress.Value = 12;
            await Task.Delay(260);   // дать глазкам и статусу показаться
            foreach (var process in Process.GetProcessesByName("Moku"))
            {
                try
                {
                    string? processPath = process.MainModule?.FileName;
                    if (processPath != null && string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(appPath), StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch { }
            }

            Progress.Value = 40;
            Status(T("ШАГ 2 ИЗ 3  ·  УДАЛЯЮ ЯРЛЫКИ И АВТОЗАПУСК", "STEP 2 OF 3  ·  REMOVING SHORTCUTS & STARTUP"));
            await Task.Delay(260);
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Moku");
            try { File.Delete(Path.Combine(startMenu, "Moku.lnk")); } catch { }
            try { File.Delete(Path.Combine(startMenu, "Uninstall Moku.lnk")); } catch { }
            try { if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true); } catch { }

            if (DesktopCheck.IsChecked == true)
            {
                string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Moku.lnk");
                try { File.Delete(desktop); } catch { }
            }
            if (AutoCheck.IsChecked == true)
            {
                try
                {
                    using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    run?.DeleteValue("Moku", false);
                }
                catch { }
            }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Moku", false); } catch { }

            if (KeepSettingsCheck.IsChecked != true)
            {
                string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moku");
                try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch { }
            }

            Progress.Value = 75;
            Status(T("ШАГ 3 ИЗ 3  ·  ЗАВЕРШАЮ УДАЛЕНИЕ", "STEP 3 OF 3  ·  FINISHING REMOVAL"));
            await Task.Delay(260);
            string script = Path.Combine(Path.GetTempPath(), "Moku-Remove-" + Guid.NewGuid().ToString("N") + ".cmd");
            string safeDir = installDir.Replace("%", "%%");
            File.WriteAllText(script, "@echo off\r\ntimeout /t 3 /nobreak >nul\r\nrmdir /s /q \"" + safeDir + "\"\r\ndel \"%~f0\"\r\n", System.Text.Encoding.Default);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"\"" + script + "\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Progress.Value = 100;
            await Task.Delay(220);
            _installing = false;
            _done = true;
            GoPage(3);
        }
        catch (Exception ex)
        {
            _installing = false;
            GoPage(1);
            OnError();
            MessageBox.Show(this, T("Не удалось удалить Moku: ", "Uninstall failed: ") + ex.Message, "Moku", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    async Task InstallAsync()
    {
        var target = FolderBox.Text.Trim();
        string exe;
        string tempExe = "";
        try
        {
            target = Path.GetFullPath(target);
            if (target.Length <= Path.GetPathRoot(target)!.Length)
                throw new InvalidOperationException(T("Нельзя установить программу в корень диска.", "The drive root cannot be used as the install folder."));
            exe = Path.Combine(target, "Moku.exe");
            tempExe = exe + ".installing";
            _installing = true; GoPage(2);
            Status(T("ШАГ 1 ИЗ 3  ·  ЗАКРЫВАЮ СТАРУЮ КОПИЮ", "STEP 1 OF 3  ·  STOPPING OLD COPY"));
            // старая копия, висящая в фоне, блокировала файл и не давала острову показаться
            foreach (var pr in Process.GetProcessesByName("Moku"))
            {
                try
                {
                    string? processPath = pr.MainModule?.FileName;
                    if (processPath != null &&
                        string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                    {
                        pr.Kill(entireProcessTree: true);
                        pr.WaitForExit(5000);
                    }
                }
                catch { /* Не трогаем процесс, путь которого не удалось проверить. */ }
            }

            Status(T("ШАГ 2 ИЗ 3  ·  КОПИРУЮ ФАЙЛЫ", "STEP 2 OF 3  ·  COPYING FILES"));
            await Task.Run(() =>
            {
                Directory.CreateDirectory(target);
                using var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("MokuInstaller.Payload.Moku.exe")
                    ?? throw new InvalidOperationException("Payload missing");
                using (var output = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buf = new byte[1 << 16]; long total = src.Length, done = 0; int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        output.Write(buf, 0, n); done += n;
                        int pct = (int)(done * 80 / Math.Max(1, total));
                        Dispatcher.Invoke(() => Progress.Value = pct);
                    }
                    output.Flush(true);
                }
                if (new FileInfo(tempExe).Length < 1024 * 1024)
                    throw new InvalidOperationException("Incomplete file");
                File.Move(tempExe, exe, overwrite: true);

                // Keep the original ICO beside the app so shortcuts use it directly.
                using var iconSource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MokuInstaller.Payload.Moku.ico")
                    ?? throw new InvalidOperationException("Icon payload missing");
                using var iconOutput = new FileStream(Path.Combine(target, "Moku.ico"), FileMode.Create, FileAccess.Write, FileShare.None);
                iconSource.CopyTo(iconOutput);
                iconOutput.Flush(true);
            });

            Status(T("ШАГ 3 ИЗ 3  ·  ЯРЛЫКИ И НАСТРОЙКИ", "STEP 3 OF 3  ·  SHORTCUTS & SETTINGS"));
            SetAnchor(StatusText); Progress.Value = 88;
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Moku");
            Directory.CreateDirectory(startMenu);
            string iconFile = Path.Combine(target, "Moku.ico");
            CreateShortcut(Path.Combine(startMenu, "Moku.lnk"), exe, iconPath: iconFile);

            string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Moku.lnk");
            if (DesktopCheck.IsChecked == true) CreateShortcut(desktopShortcut, exe, iconPath: iconFile);

            string? currentInstaller = Environment.ProcessPath;
            string uninstaller = Path.Combine(target, "Moku-Uninstall.exe");
            if (!string.IsNullOrWhiteSpace(currentInstaller) && File.Exists(currentInstaller) &&
                !string.Equals(Path.GetFullPath(currentInstaller), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentInstaller, uninstaller, overwrite: true);
                CreateShortcut(Path.Combine(startMenu, "Uninstall Moku.lnk"), uninstaller, "--uninstall", iconFile);
                using var uninstallKey = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Moku");
                if (uninstallKey != null)
                {
                    uninstallKey.SetValue("DisplayName", "Moku");
                    uninstallKey.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.5.0");
                    uninstallKey.SetValue("InstallLocation", target);
                    uninstallKey.SetValue("DisplayIcon", iconFile);
                    uninstallKey.SetValue("UninstallString", "\"" + uninstaller + "\" --uninstall");
                    uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }

            try
            {
                using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (run != null)
                {
                    if (AutoCheck.IsChecked == true) run.SetValue("Moku", "\"" + exe + "\"");
                    else run.DeleteValue("Moku", false);
                }
            }
            catch { }

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moku");
            Directory.CreateDirectory(dir);
            var sp = Path.Combine(dir, "settings.json");
            var settings = new Dictionary<string, object>();
            if (File.Exists(sp)) { try { settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(sp)) ?? new(); } catch { settings = new(); } }
            settings["Language"] = AppLanguageEnglish.IsChecked == true ? "en" : "ru";
            File.WriteAllText(sp, System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Progress.Value = 100; _installing = false; _done = true;
            GoPage(3);
        }
        catch (Exception ex)
        {
            try { if (!string.IsNullOrEmpty(tempExe) && File.Exists(tempExe)) File.Delete(tempExe); } catch { }
            _installing = false; GoPage(1);
            OnError();
            System.Windows.MessageBox.Show(this, T("Не удалось установить: ", "Install failed: ") + ex.Message, "Moku", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void Finish()
    {
        try
        {
            var exe = Path.Combine(FolderBox.Text.Trim(), "Moku.exe");
            if (!_uninstallMode && LaunchCheck.IsChecked == true && File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (Exception ex) { OnError(); System.Windows.MessageBox.Show(this, ex.Message, "Moku"); }
        if (!_uninstallMode && LaunchCheck.IsChecked == true) Feel(Mood.Excited, 1.0);
        BeginClose();
    }

    static void CreateShortcut(string path, string target, string arguments = "", string? iconPath = null)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable");
        dynamic shell = Activator.CreateInstance(t)!;
        dynamic sc = shell.CreateShortcut(path);
        sc.TargetPath = target;
        sc.Arguments = arguments;
        sc.WorkingDirectory = Path.GetDirectoryName(target);
        sc.IconLocation = !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath)
            ? iconPath + ",0"
            : target + ",0";
        sc.Description = "Moku";
        sc.Save();
    }
}

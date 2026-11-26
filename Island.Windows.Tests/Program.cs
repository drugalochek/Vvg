using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using Island;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

internal static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static int RunWindowProbe(bool bare = false)
    {
        var app = new Application();
        var window = new Window { Title = bare ? "" : "Moku external restoration probe", Width = 320, Height = 180 };
        if (bare) window.ShowInTaskbar=false;
        window.Loaded += (_, _) => window.WindowState = WindowState.Minimized;
        return app.Run(window);
    }
    static void TestExternalWindowRestore()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (System.IO.Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Program).Assembly.Location);
        info.ArgumentList.Add("--window-probe");
        using var child = Process.Start(info)!;
        try
        {
            IntPtr hwnd = IntPtr.Zero;
            for (int i = 0; i < 80 && !child.HasExited; i++)
            {
                child.Refresh(); hwnd = child.MainWindowHandle;
                if (hwnd != IntPtr.Zero && WindowActivation.IsMinimized(hwnd)) break;
                Pump();
            }
            Check(hwnd != IntPtr.Zero, "external app window found");
            Check(WindowActivation.IsMinimized(hwnd), "external app initially minimized");
            Check(WindowActivation.RestoreAndActivate(hwnd), "restore request targets existing external window");
            for (int i = 0; i < 30 && WindowActivation.IsMinimized(hwnd); i++) Pump();
            Check(!WindowActivation.IsMinimized(hwnd), "minimized external window really restored through Win32");
            child.Refresh();
            Check(!child.HasExited && child.MainWindowHandle == hwnd, "restoration retains the same external window and process");
            Check(WindowActivation.RestoreAndActivate(hwnd) && !child.HasExited, "repeat activation reuses existing external app");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(5000); }
        }
    }

    static void TestQuickAccessLookup()
    {
        // Same production lookup used by the right-hand Calculator button, with a
        // calculator-named process whose minimized window has NO title/taskbar button.
        string host=Path.ChangeExtension(typeof(Program).Assembly.Location,".exe");
        string probe=Path.Combine(Path.GetDirectoryName(host)!,"CalculatorApp.exe");
        File.Copy(host,probe,true);
        using var child=Process.Start(new ProcessStartInfo(probe,"--bare-window-probe") { UseShellExecute=false })!;
        try
        {
            IntPtr hwnd=IntPtr.Zero;
            for(int i=0;i<60 && !child.HasExited;i++)
            {
                hwnd=QuickWindowResolver.Find("calc.exe");
                if(hwnd!=IntPtr.Zero && WindowActivation.IsMinimized(hwnd)) break;
                Pump();
            }
            Check(hwnd!=IntPtr.Zero && WindowActivation.IsMinimized(hwnd),"Home Calculator lookup includes titleless minimized external app");
            Check(WindowActivation.RestoreAndActivate(hwnd),"Home Calculator resolved HWND reaches production activation");
            for(int i=0;i<30 && WindowActivation.IsMinimized(hwnd);i++) Pump();
            Check(!WindowActivation.IsMinimized(hwnd),"Home Calculator lookup really restores the window");
            Check(QuickWindowResolver.Find("calc.exe")==hwnd,"repeat Home Calculator lookup preserves same HWND");
        }
        finally
        {
            if(!child.HasExited) { child.Kill(true); child.WaitForExit(5000); }
            File.Delete(probe);
        }
    }
    static void TestProcessAudioActivation()
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,20348))
        { Console.WriteLine("SKIP process-audio activation: unsupported Windows build"); return; }
        using var capture=new ProcessLoopbackCapture(Environment.ProcessId);
        Exception? failure=null;
        capture.RecordingStopped+=(_,e)=>failure=e.Exception;
        capture.StartRecording();
        for(int i=0;i<50 && !capture.IsRunning && failure==null;i++) Pump();
        if(failure!=null)
        {
            // Only absent Windows audio infrastructure may skip a hardware-dependent test.
            // Interface/ABI/COM errors must fail the build rather than silently pass.
            int code=failure.HResult;
            if(code==unchecked((int)0x88890004) || code==unchecked((int)0x88890010)
                || code==unchecked((int)0x80070424) || code==unchecked((int)0x800706BA))
            { Console.WriteLine("SKIP process-audio activation: runner audio service/device unavailable: "+failure); return; }
            throw new InvalidOperationException("Process audio activation failed",failure);
        }
        Check(capture.IsRunning,"process-specific WASAPI activates and starts with production COM ABI");
        capture.StopRecording();
    }

    static int RunAudioProbe(string eventName, string frequency)
    {
        using var trigger=EventWaitHandle.OpenExisting(eventName);
        using var player=new WaveOutEvent();
        var tone=new SignalGenerator(44100,2) { Frequency=double.Parse(frequency,System.Globalization.CultureInfo.InvariantCulture), Gain=0.2, Type=SignalGeneratorType.Sin };
        player.Init(tone); trigger.WaitOne(); player.Play();
        Thread.Sleep(30000); return 0;
    }
    static void TestProcessAudioIsolation()
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,20348) || WaveOut.DeviceCount==0)
        { Console.WriteLine("SKIP two-process audio signal isolation: runner has no audio render device"); return; }
        string id=Guid.NewGuid().ToString("N");
        using var musicReady=new EventWaitHandle(false,EventResetMode.ManualReset,"MokuMusic"+id);
        using var otherReady=new EventWaitHandle(false,EventResetMode.ManualReset,"MokuOther"+id);
        string host=Path.ChangeExtension(typeof(Program).Assembly.Location,".exe");
        using var music=Process.Start(new ProcessStartInfo(host,"--audio-probe MokuMusic"+id+" 220") { UseShellExecute=false })!;
        using var other=Process.Start(new ProcessStartInfo(host,"--audio-probe MokuOther"+id+" 1000") { UseShellExecute=false })!;
        try
        {
            using var capture=new ProcessLoopbackCapture(music.Id);
            double total=0; long samples=0;
            Exception? failure=null;
            var gate=new object();
            capture.DataAvailable+=(_,e)=>
            {
                double energy=0;
                for(int i=0;i+1<e.BytesRecorded;i+=2) { double v=BitConverter.ToInt16(e.Buffer,i)/32768.0; energy+=v*v; }
                lock(gate) { total+=energy; samples+=e.BytesRecorded/2; }
            };
            capture.RecordingStopped+=(_,e)=>failure=e.Exception;
            capture.StartRecording();
            for(int i=0;i<40 && !capture.IsRunning && failure==null;i++) Pump();
            if(failure!=null) throw new InvalidOperationException("Audio signal test capture",failure);
            Check(capture.IsRunning,"isolated signal test capture started");
            otherReady.Set();
            for(int i=0;i<12;i++) Pump();
            lock(gate)
            {
                Check(samples==0 || Math.Sqrt(total/samples)<0.001,"unrelated voice-like tone is excluded while selected music is silent");
                total=0; samples=0;
            }
            musicReady.Set();
            for(int i=0;i<18;i++) Pump();
            lock(gate) Check(samples>0 && Math.Sqrt(total/samples)>0.01,"selected music tone is captured while unrelated tone also plays");
            Check(!music.HasExited && !other.HasExited,"both audio-source processes stayed alive for isolation test");
            capture.StopRecording();
        }
        finally
        {
            foreach(var p in new[] {music,other}) if(!p.HasExited) {p.Kill(true);p.WaitForExit(5000);}
        }
    }

    static void TestSessionLevelLifecycle()
    {
        // This backend is deliberately exercised even on modern Windows.
        int callbacks = 0;
        bool invalid = false;
        using (var meter = new SessionPeakMonitor(Environment.ProcessId))
        {
            meter.LevelAvailable += level =>
            {
                if (!float.IsFinite(level) || level < 0 || level > 1) invalid = true;
                Interlocked.Increment(ref callbacks);
            };
            meter.Start();
            for (int i = 0; i < 15 && Volatile.Read(ref callbacks) == 0; i++) Pump();
            Check(Volatile.Read(ref callbacks) > 0 && !invalid, "Windows 10 session-level backend publishes finite values or safe silence");
            Check(meter.ProcessId == Environment.ProcessId, "session meter retains selected player identity");
            meter.Dispose();
            for (int i = 0; i < 20 && !meter.IsCompleted; i++) Pump();
            Check(meter.IsCompleted, "session meter worker stops without blocking UI");
            int stopped = Volatile.Read(ref callbacks);
            Pump(); Pump();
            Check(Volatile.Read(ref callbacks) == stopped, "disposed meter cannot publish late player levels");
        }
    }
    static void TestSessionLevelIsolation()
    {
        if (WaveOut.DeviceCount == 0)
        { Console.WriteLine("SKIP Windows 10 session-level two-process test: no audio render device"); return; }
        string id = Guid.NewGuid().ToString("N");
        using var musicReady = new EventWaitHandle(false, EventResetMode.ManualReset, "MokuMeterMusic"+id);
        using var otherReady = new EventWaitHandle(false, EventResetMode.ManualReset, "MokuMeterOther"+id);
        string host = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
        using var music = Process.Start(new ProcessStartInfo(host, "--audio-probe MokuMeterMusic"+id+" 220") { UseShellExecute=false })!;
        using var other = Process.Start(new ProcessStartInfo(host, "--audio-probe MokuMeterOther"+id+" 1000") { UseShellExecute=false })!;
        try
        {
            using var meter = new SessionPeakMonitor(music.Id);
            float peak = 0;
            var gate = new object();
            meter.LevelAvailable += level => { lock (gate) peak = Math.Max(peak, level); };
            meter.Start();
            otherReady.Set();
            for (int i=0; i<18; i++) Pump();
            Check(!other.HasExited && !music.HasExited, "both level-test source processes remain alive");
            lock (gate)
            {
                Check(peak < 0.001f, "Windows 10 meter excludes unrelated audio while player is silent");
                peak = 0;
            }
            musicReady.Set();
            for (int i=0; i<25; i++) Pump();
            lock (gate) Check(peak > 0.01f, "Windows 10 meter detects selected player alongside unrelated audio");
        }
        finally
        {
            foreach (var p in new[] { music, other })
                if (!p.HasExited) { p.Kill(true); p.WaitForExit(5000); }
        }
    }

    static void TestAutomaticAudioLifecycle()
    {
        using var analyzer = new AudioBeatAnalyzer();
        Check(analyzer.ActiveBackend == null, "automatic audio backend is idle before music starts");
        analyzer.SetActive(true, "Moku.Unrecognized.Audio.Source." + Guid.NewGuid().ToString("N"));
        for (int i = 0; i < 4; i++) Pump();
        Check(analyzer.ActiveBackend == null && analyzer.ActivityLevel == 0, "unknown player identity never starts system-mix capture");
        using var me = Process.GetCurrentProcess();
        analyzer.SetActive(true, me.ProcessName + ".exe");
        for (int i = 0; i < 25 && analyzer.ActiveBackend == null; i++) Pump();
        Check(analyzer.ActiveBackend is AudioLevelPolicy.Spectrum or AudioLevelPolicy.SessionLevel,
            "known player automatically starts an available per-player backend without a Windows selector");
        analyzer.SetActive(false, null);
        Check(analyzer.ActiveBackend == null && analyzer.ActivityLevel == 0 && analyzer.BassLevel == 0 && !analyzer.ConsumeBeat(),
            "pause detaches automatic capture and clears audio levels");
        analyzer.Dispose();
        analyzer.SetActive(true, me.ProcessName + ".exe");
        Check(analyzer.ActiveBackend == null, "disposed automatic analyzer ignores later media events");
    }

    /// <summary>
    /// Registers the Internet helper tasks (CI runners are administrators with UAC off), checks that the user can
    /// start the harmless "on" task without elevation, then removes them. The "off" task is never started here.
    /// </summary>
    static int TestInternetTask()
    {
        try
        {
            Check(InternetSwitchTask.Install() == ToggleResult.Done, "internet helper tasks registered with one elevated setup");
            Check(InternetSwitchTask.IsInstalled(), "both internet tasks are visible to the current user");
            var run = InternetSwitchTask.RunOnTaskForTest();
            Console.WriteLine("INFO harmless 'on' task finished successfully: " + (run?.ToString() ?? "not startable"));
            Console.WriteLine("INFO on task: " + InternetSwitchTask.DescribeForTest(true));
            var direct = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + SystemPanelPolicy.EncodePowerShell(SystemPanelPolicy.InternetTaskScript(true)))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (var p = Process.Start(direct)!)
            {
                string err = p.StandardError.ReadToEnd(), outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(60000);
                Console.WriteLine($"INFO direct 'on' script exit={p.ExitCode} out={outp.Trim()} err={err.Trim()}");
            }
            Console.WriteLine("INFO session: " + Process.GetCurrentProcess().SessionId + " user=" + Environment.UserDomainName + "\\" + Environment.UserName);
            Check(run != null, "the current user can start the 'on' task without a UAC prompt");
            Console.WriteLine($"PASS: {checks} internet helper task checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            try
            {
                using var cleanup = Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                    "-NoProfile -NonInteractive -Command \"Get-ScheduledTask -TaskPath '\\Moku\\' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                cleanup?.WaitForExit(30000);
            }
            catch { }
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--audio-probe") return RunAudioProbe(args[1],args[2]);
        if (args.Length == 1 && args[0] == "--window-probe") return RunWindowProbe();
        if (args.Length == 1 && args[0] == "--bare-window-probe") return RunWindowProbe(true);
        if (args.Length == 1 && args[0] == "--internet-task") return TestInternetTask();
        try
        {
            // Load the production compiled resources, without invoking Moku's OnStartup,
            // singleton handling, real MainWindow, network, hotkeys or background services.
            using var watchdog = new System.Threading.Timer(_ =>
            {
                Console.Error.WriteLine("Windows WPF smoke test exceeded 100 seconds."); Environment.Exit(2);
            }, null, TimeSpan.FromSeconds(100), System.Threading.Timeout.InfiniteTimeSpan);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/Moku;component/PopupStyles.xaml", UriKind.Relative) });
            var button = new Button { Content = "Popup smoke test" };
            var window = new Window { Width = 360, Height = 180, Content = button, ShowInTaskbar = false };
            window.Show(); Pump();
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            var submenu = new MenuItem { Header = "Размер плитки" };
            var nested = new MenuItem { Header = "2×2", IsCheckable = true, IsChecked = true };
            submenu.Items.Add(nested); menu.Items.Add(submenu);
            menu.Items.Add(new Separator()); menu.Items.Add(new MenuItem { Header = "От имени администратора", IsEnabled = false });
            button.ContextMenu = menu; menu.IsOpen = true; Pump();
            menu.ApplyTemplate(); submenu.ApplyTemplate();
            var outer = VisualTreeHelper.GetChild(menu, 0) as Border;
            Check(outer != null && menu.Style != null, "production context menu uses custom border template");
            Check(outer!.CornerRadius.TopLeft == 18, "initial popup radius");
            Check(outer.Background is LinearGradientBrush, "production theme brush replaces system menu background");
            submenu.IsSubmenuOpen = true; Pump();
            var popup = (Popup)submenu.Template.FindName("PART_Popup", submenu);
            var inner = popup.Child as Border;
            Check(popup.IsOpen && inner != null, "custom nested popup opens");
            Check(inner!.CornerRadius.TopLeft == 18, "submenu shares popup radius");
            nested.ApplyTemplate();
            Check(nested.Template.FindName("ItemSurface", nested) is Border, "nested item retains custom template");
            var tip = new ToolTip { PlacementTarget = button, Content = "Preview", Style = (Style)app.Resources["MokuWindowTooltip"], IsOpen = true };
            Pump(); tip.ApplyTemplate();
            var tipBorder = VisualTreeHelper.GetChild(tip, 0) as Border;
            Check(tipBorder != null && tipBorder.CornerRadius.TopLeft == 18, "preview uses production rounded template");
            foreach (double radius in new[] { 0.0, 31.0 })
            {
                app.Resources["MokuPopupRadius"] = new CornerRadius(radius); Pump();
                Check(outer.CornerRadius.TopLeft == radius, "context radius updates live: " + radius);
                Check(inner.CornerRadius.TopLeft == radius, "submenu radius updates live: " + radius);
                Check(tipBorder!.CornerRadius.TopLeft == radius, "preview radius updates live: " + radius);
            }
            bool clicked = false;
            nested.Click += (_, _) => clicked = true;
            nested.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, nested));
            Check(clicked, "custom menu preserves click routing");
            tip.IsOpen = false; menu.IsOpen = false;
            SystemDashboardTests.Run(Check);
            TestExternalWindowRestore();
            TestQuickAccessLookup();
            TestProcessAudioActivation();
            TestProcessAudioIsolation();
            TestAutomaticAudioLifecycle();
            MusicIdentityTests.Run(Check);
            TestSessionLevelLifecycle();
            TestSessionLevelIsolation();
            window.Close(); app.Shutdown();
            Console.WriteLine($"PASS: {checks} Windows window-restoration and WPF popup checks. Full application manual testing is separate.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

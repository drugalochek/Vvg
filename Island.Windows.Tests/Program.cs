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
    [STAThread]
    static int Main()
    {
        try
        {
            // Load the production compiled resources, without invoking Moku's OnStartup,
            // singleton handling, real MainWindow, network, hotkeys or background services.
            using var watchdog = new System.Threading.Timer(_ =>
            {
                Console.Error.WriteLine("Windows WPF smoke test exceeded 60 seconds."); Environment.Exit(2);
            }, null, TimeSpan.FromSeconds(60), System.Threading.Timeout.InfiniteTimeSpan);
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
            tip.IsOpen = false; menu.IsOpen = false; window.Close(); app.Shutdown();
            Console.WriteLine($"PASS: {checks} Windows WPF popup checks. Full application manual testing is separate.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

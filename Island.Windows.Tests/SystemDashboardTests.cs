using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Island;

internal static class SystemDashboardTests
{
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    internal static void Run(Action<bool,string> check)
    {
        string previews = Path.Combine(Environment.CurrentDirectory,"ui-preview"); Directory.CreateDirectory(previews);
        // CPU 23 %, RAM 8.6 / 16 GB, GPU 41 %, Wi-Fi on + connected, Bluetooth on, VPN off, microphone on.
        var data = new SystemSnapshot(23,53.75,9234179686,17179869184,41,"GeForce RTX 4060",true,true,true,false,null,false);
        string[] allowed = { "System-internet","System-bluetooth","System-vpn","System-microphone","System-sleep","System-restart","System-shutdown" };
        foreach(bool ru in new[]{true,false})
        {
            var actions = new List<string>();
            var dashboard = new SystemDashboard(ru,18,actions.Add);
            var host = new Border { Background=new SolidColorBrush(Color.FromRgb(12,12,14)),Padding=new Thickness(16),Child=dashboard,Width=652 };
            dashboard.Update(data);
            Layout(host);
            check(dashboard.ActualWidth==620,"system production page uses 620 DIP / "+ru);
            var ids = Descendants(dashboard).OfType<Button>().Select(b=>AutomationProperties.GetAutomationId(b)).ToArray();
            check(ids.OrderBy(i=>i).SequenceEqual(allowed.OrderBy(i=>i)),"System tab has only Wi-Fi, Bluetooth, VPN, microphone and power buttons: "+string.Join(",",ids));
            foreach(var removed in new[]{"taskmanager","camera","notifications","sound","desktop","airplane","lock","microphone-settings","battery","wifi"})
                check(!ids.Contains("System-"+removed),"removed control is gone: "+removed);
            foreach(var button in Descendants(dashboard).OfType<Button>())
            {
                check(button.ActualHeight>=44,"system input target >=44 DIP: "+AutomationProperties.GetAutomationId(button));
                check(button.ActualWidth>60,"system input has usable width: "+AutomationProperties.GetAutomationId(button));
                check(Descendants(button).OfType<TextBlock>().Any(t=>t.FontFamily.Source.Contains("Segoe")),"every system button has an icon: "+AutomationProperties.GetAutomationId(button));
            }
            foreach(var text in Descendants(dashboard).OfType<TextBlock>().Where(t=>t.IsVisible || t.ActualWidth>0))
            {
                var bounds=text.TransformToAncestor(host).TransformBounds(new Rect(0,0,text.ActualWidth,text.ActualHeight));
                check(bounds.Left>=0 && bounds.Right<=652.1,"system label stays in panel: "+text.Text);
            }
            string[] texts() => Descendants(dashboard).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            check(texts().Contains("GEFORCE RTX 4060") && texts().Contains("41%") && texts().Contains("23%"),"GPU model, GPU and CPU load are shown");
            check(texts().Contains(ru?"8,6 / 16 ГБ":"8.6 / 16 GB") || texts().Any(t=>t.StartsWith("8") && t.Contains("16")),"memory used / total is shown");
            check(Descendants(Find(dashboard,"System-internet")).OfType<TextBlock>().Any(t=>t.Text==(ru?"Подключено":"Connected")),"connected internet is reported");
            check(Descendants(Find(dashboard,"System-vpn")).OfType<TextBlock>().Any(t=>t.Text==(ru?"Выкл.":"Off")),"VPN reports its real off state");
            check(Descendants(Find(dashboard,"System-microphone")).OfType<TextBlock>().Any(t=>t.Text=="\uE720"),"live microphone shows the microphone icon");
            Find(dashboard,"System-internet").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.SequenceEqual(new[]{"internet"}),"Internet switch dispatches the real toggle without a settings link");
            var restart=Find(dashboard,"System-restart"); restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(dashboard.ConfirmationVisible && dashboard.PendingAction=="restart" && actions.Count==1,"restart cannot execute on first click");
            Find(dashboard,"System-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!dashboard.ConfirmationVisible && actions.Count==1,"cancel does not dispatch power action");
            restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find(dashboard,"System-confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.SequenceEqual(new[]{"internet","restart"}) && !dashboard.ConfirmationVisible,"power action dispatches exactly once after confirmation");
            var shutdown=Find(dashboard,"System-shutdown");shutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dashboard.CancelConfirmation(); check(!dashboard.ConfirmationVisible && dashboard.PendingAction==null,"leaving System clears armed power action");

            // Pending switch: "turning off…" until Windows reports the new state, then the real state.
            var clock = DateTime.UtcNow; dashboard.Clock = () => clock;
            dashboard.SetPending("bluetooth", false);
            check(Descendants(Find(dashboard,"System-bluetooth")).OfType<TextBlock>().Any(t=>t.Text==(ru?"Выключаю…":"Turning off…")),"pending switch is visible");
            dashboard.Update(data with { Bluetooth = false });
            check(!dashboard.IsPending("bluetooth",out _,out _) && Descendants(Find(dashboard,"System-bluetooth")).OfType<TextBlock>().Any(t=>t.Text==(ru?"Выкл.":"Off")),"pending clears once the radio really changed");
            dashboard.SetPending("vpn", true); clock = clock.AddSeconds(20); dashboard.Update(data);
            check(!dashboard.IsPending("vpn",out _,out _),"VPN that never connected does not stay 'connecting' forever");
            dashboard.SetResult("internet", ToggleResult.Denied);
            check(Descendants(Find(dashboard,"System-internet")).OfType<TextBlock>().Any(t=>t.Text==(ru?"Windows запретила":"Blocked by Windows")),"refused radio switch is reported, not faked");
            dashboard.Clock = () => DateTime.UtcNow;

            dashboard.Update(new(null,null,0,0,null,null,null,false,null,null,null,null));
            check(!Find(dashboard,"System-microphone").IsEnabled,"missing microphone is unavailable, not fake enabled");
            check(!Find(dashboard,"System-internet").IsEnabled && !Find(dashboard,"System-bluetooth").IsEnabled,"missing radios are unavailable, not fake switches");
            Layout(host); Save(host,Path.Combine(previews,ru?"system-ru-unavailable.png":"system-en-unavailable.png"),1);
            dashboard.Update(data with { MicMuted = true, Vpn = true });Layout(host);
            check(Descendants(Find(dashboard,"System-microphone")).OfType<TextBlock>().Any(t=>t.Text=="\uEC54"),"muted microphone shows the crossed-out icon");
            Save(host,Path.Combine(previews,ru?"system-ru-mic-off-vpn-on.png":"system-en-mic-off-vpn-on.png"),1);
            dashboard.Update(data);Layout(host);
            foreach(double scale in new[]{1.0,1.5,2.0}) Save(host,Path.Combine(previews,$"system-{(ru?"ru":"en")}-{scale:0.0}.png"),scale);
            shutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(host);
            Save(host,Path.Combine(previews,ru?"system-ru-confirm.png":"system-en-confirm.png"),1);
            dashboard.CancelConfirmation();
            dashboard.SetRadius(0); Layout(host);
            var metric = Descendants(dashboard).OfType<Border>().First(b=>b.Padding.Left==14);
            check(metric.CornerRadius.TopLeft==0,"system cards honor square corner setting");
            dashboard.SetRadius(36);check(metric.CornerRadius.TopLeft==18,"system small cards cap shared rounding safely");
        }
        // The connector dispatch is injected: CI never establishes a real VPN or touches user profiles.
        var sent = new List<Uri>();
        var connector = new HappVpnConnector(() => true, sent.Add);
        check(connector.RequestConnection() == HappVpnRequestResult.Sent && connector.RequestDisconnection() == HappVpnRequestResult.Sent, "registered Happ receives connect and disconnect");
        check(sent.Select(u=>u.OriginalString).SequenceEqual(new[]{"happ://connect","happ://disconnect"}), "Happ uses its own connect / disconnect links");
        connector.RequestLegacyConnection(); check(sent.Last().OriginalString == "happ://open", "older Happ fallback uses documented happ://open");
        var missing = new HappVpnConnector(() => false, _ => throw new Exception("Must not activate"));
        check(missing.RequestConnection() == HappVpnRequestResult.ClientMissing, "missing Happ does not attempt unknown protocol activation");
        var failed = new HappVpnConnector(() => true, _ => throw new InvalidOperationException("test dispatch failure"));
        check(failed.RequestDisconnection() == HappVpnRequestResult.Failed, "failed protocol dispatch is not reported as done");
        foreach (bool ru in new[]{true,false})
        {
            var clicks = new List<string>();
            var happ = new SystemDashboard(ru,18,clicks.Add,vpnClient:"Happ");
            happ.Update(data);
            var host = new Border { Background=new SolidColorBrush(Color.FromRgb(12,12,14)),Padding=new Thickness(16),Child=happ,Width=652 };
            Layout(host);
            var vpn = Find(happ,"System-vpn");
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text=="Happ VPN"), "selected Happ has explicit client label");
            vpn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(clicks.SequenceEqual(new[]{"vpn"}), "Happ tile retains production VPN action routing");
            happ.SetPending("vpn", true); Layout(host);
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text==(ru?"Подключаю…":"Connecting…")), "Happ request shows connecting, not connected");
            Save(host,Path.Combine(previews,ru?"happ-ru-connecting.png":"happ-en-connecting.png"),1);
            happ.Update(data with { Vpn = true }); Layout(host);
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text==(ru?"Вкл.":"On")), "Happ on only after the tunnel is really up");
            Save(host,Path.Combine(previews,ru?"happ-ru-on.png":"happ-en-on.png"),1);
            happ.SetVpnRequestResult(HappVpnRequestResult.ClientMissing);Layout(host);
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text==(ru?"Happ не найден":"Happ not found")), "Happ missing installation is visible inline");
            happ.SetVpnClient("Windows");
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text=="VPN"), "switching client resets the label");
        }
        var native=new SystemMonitor(); var snapshot=native.Read("Windows");
        check(snapshot.Cpu==null,"first native CPU sample is unavailable, not invented");
        check(snapshot.Memory is null or >=0 and <=100,"native memory is bounded or unavailable");
        check(snapshot.Gpu is null or >=0 and <=100,"native GPU load is bounded or unavailable");
        Thread.Sleep(600); var second=native.Read("Windows");
        check(second.Cpu is null or >=0 and <=100,"second native CPU sample is bounded");
        check(second.Gpu is null or >=0 and <=100,"second native GPU sample is bounded");
        Console.WriteLine($"native sample: cpu={second.Cpu} gpu={second.Gpu} gpuName={second.GpuName} internet={second.Internet} bt={second.Bluetooth} vpn={second.Vpn} mic={second.MicMuted}");
        var xml=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"MainWindow.source.xaml"));
        XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var support=xml.Descendants().Single(e=>(string?)e.Attribute(x+"Name")=="SupportBtn");
        check(support.Ancestors().Any(e=>(string?)e.Attribute(x+"Name")=="CatSupport"),"Boosty lives in its own Support settings section");
        check(!support.Ancestors().Any(e=>(string?)e.Attribute(x+"Name")=="CatGeneral"),"Boosty is absent from General settings");
        check(xml.Descendants().Any(e=>(string?)e.Attribute(x+"Name")=="TabSystem"),"System navigation tab exists in production XAML");
    }
    static Button Find(DependencyObject root,string id)=>Descendants(root).OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)==id);
    static void Layout(FrameworkElement host)
    {
        host.Measure(new Size(652,double.PositiveInfinity));host.Arrange(new Rect(new Point(),host.DesiredSize));host.UpdateLayout();
    }
    static void Save(FrameworkElement host,string path,double scale)
    {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth*scale),(int)Math.Ceiling(host.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
        bitmap.Render(host);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);encoder.Save(file);
    }
}

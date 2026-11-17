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
        var data = new SystemSnapshot(23,53.75,9234179686,17179869184,82,true,true,true,false,65,false);
        foreach(bool ru in new[]{true,false})
        {
            var actions = new List<string>();
            var dashboard = new SystemDashboard(ru,18,actions.Add);
            var host = new Border { Background=new SolidColorBrush(Color.FromRgb(12,12,14)),Padding=new Thickness(16),Child=dashboard,Width=652 };
            dashboard.Update(data);
            Layout(host);
            check(dashboard.ActualWidth==620,"system production page uses 620 DIP / "+ru);
            foreach(var button in Descendants(dashboard).OfType<Button>())
            {
                check(button.ActualHeight>=44,"system input target >=44 DIP: "+AutomationProperties.GetAutomationId(button));
                check(button.ActualWidth>60,"system input has usable width: "+AutomationProperties.GetAutomationId(button));
            }
            foreach(var text in Descendants(dashboard).OfType<TextBlock>().Where(t=>t.IsVisible || t.ActualWidth>0))
            {
                var bounds=text.TransformToAncestor(host).TransformBounds(new Rect(0,0,text.ActualWidth,text.ActualHeight));
                check(bounds.Left>=0 && bounds.Right<=652.1,"system label stays in panel: "+text.Text);
            }
            var restart=Find(dashboard,"System-restart"); restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(dashboard.ConfirmationVisible && dashboard.PendingAction=="restart" && actions.Count==0,"restart cannot execute on first click");
            Find(dashboard,"System-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!dashboard.ConfirmationVisible && actions.Count==0,"cancel does not dispatch power action");
            restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find(dashboard,"System-confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.SequenceEqual(new[]{"restart"}) && !dashboard.ConfirmationVisible,"power action dispatches exactly once after confirmation");
            Find(dashboard,"System-wifi").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.Last()=="wifi","system settings link routes real action");
            var shutdown=Find(dashboard,"System-shutdown");shutdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dashboard.CancelConfirmation(); check(!dashboard.ConfirmationVisible && dashboard.PendingAction==null,"leaving System clears armed power action");
            dashboard.Update(new(null,null,0,0,null,false,true,null,null,null,null));
            check(!Find(dashboard,"System-microphone").IsEnabled,"missing microphone is unavailable, not fake enabled");
            Layout(host); Save(host,Path.Combine(previews,ru?"system-ru-unavailable.png":"system-en-unavailable.png"),1);
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
        check(connector.RequestConnection() == HappVpnRequestResult.Sent, "registered Happ receives connection command");
        check(sent.Count == 1 && sent[0].OriginalString == "happ://open", "Happ uses exact documented start URI, not a guessed connect command");
        var missing = new HappVpnConnector(() => false, _ => throw new Exception("Must not activate"));
        check(missing.RequestConnection() == HappVpnRequestResult.ClientMissing, "missing Happ does not attempt unknown protocol activation");
        var failed = new HappVpnConnector(() => true, _ => throw new InvalidOperationException("test dispatch failure"));
        check(failed.RequestConnection() == HappVpnRequestResult.Failed, "failed protocol dispatch is not reported as connected");
        foreach (bool ru in new[]{true,false})
        {
            var clicks = new List<string>();
            var happ = new SystemDashboard(ru,18,clicks.Add,vpnClient:"Happ");
            happ.Update(data);
            var host = new Border { Background=new SolidColorBrush(Color.FromRgb(12,12,14)),Padding=new Thickness(16),Child=happ,Width=652 };
            Layout(host);
            var vpn = Find(happ,"System-vpn");
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text=="Happ VPN"), "selected Happ has explicit client label");
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text==(ru?"Подключить":"Connect")), "Happ tile offers connection, not just opening a window");
            Save(host,Path.Combine(previews,ru?"happ-ru-ready.png":"happ-en-ready.png"),1);
            vpn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(clicks.SequenceEqual(new[]{"vpn"}), "Happ tile retains production VPN action routing");
            happ.SetVpnRequestResult(HappVpnRequestResult.Sent); Layout(host);
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text==(ru?"Запрос отправлен":"Request sent")), "Happ dispatch does not fabricate connected status");
            Save(host,Path.Combine(previews,ru?"happ-ru-sent.png":"happ-en-sent.png"),1);
            happ.SetVpnRequestResult(HappVpnRequestResult.ClientMissing);Layout(host);
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text==(ru?"Happ не найден":"Happ not found")), "Happ missing installation is visible inline");
            Save(host,Path.Combine(previews,ru?"happ-ru-missing.png":"happ-en-missing.png"),1);
            happ.SetVpnRequestResult(HappVpnRequestResult.Failed);Layout(host);
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text==(ru?"Ошибка команды":"Request failed")), "Happ command failure is visible inline");
            Save(host,Path.Combine(previews,ru?"happ-ru-failed.png":"happ-en-failed.png"),1);
            happ.SetVpnClient("Windows");
            check(Descendants(vpn).OfType<TextBlock>().Any(t=>t.Text==(ru?"Параметры ↗":"Settings ↗")), "switching client resets old command status");
        }
        var native=new SystemMonitor(); var snapshot=native.Read();
        check(snapshot.Cpu==null,"first native CPU sample is unavailable, not invented");
        check(snapshot.Memory is null or >=0 and <=100,"native memory is bounded or unavailable");
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

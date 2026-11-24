using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Control = System.Windows.Controls.Control;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Island;

/// <summary>
/// System tab: real Internet / Bluetooth / VPN / microphone switches, sleep / restart / shut down,
/// CPU / GPU / RAM. Nothing else — the same production surface is rendered by Windows tests.
/// </summary>
internal sealed class SystemDashboard : StackPanel
{
    readonly bool ru;
    readonly Action<string> invoke;
    string vpnClient = "Windows";
    readonly Dictionary<string, Toggle> toggles = new();
    readonly Dictionary<string, Button> buttons = new();
    readonly Dictionary<string, (TextBlock Title, TextBlock Value, ProgressBar Bar)> metrics = new();
    readonly Dictionary<string, (bool Target, DateTime Started)> pending = new();
    readonly Dictionary<string, (string Text, DateTime Until)> notes = new();
    readonly StackPanel confirmation = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    SystemSnapshot? lastSnapshot;
    internal string? PendingAction { get; private set; }
    internal bool ConfirmationVisible => confirmation.Visibility == Visibility.Visible;
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    string L(string r, string e) => ru ? r : e;

    static readonly Brush OnFill = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)));
    static readonly Brush OffFill = Freeze(new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)));
    static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB2)));
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static T Freeze<T>(T b) where T : Freezable { b.Freeze(); return b; }

    sealed record Toggle(Button Button, Border Circle, TextBlock Glyph, TextBlock Title, TextBlock Caption, string OnGlyph, string OffGlyph);

    internal SystemDashboard(bool russian, double radius, Action<string> action, Brush? card = null, Brush? rim = null, string vpnClient = "Windows")
    {
        ru = russian; invoke = action;
        Resources["SystemCard"] = card ?? new LinearGradientBrush(Color.FromArgb(38,255,255,255), Color.FromArgb(13,255,255,255), 90);
        Resources["SystemRim"] = rim ?? new SolidColorBrush(Color.FromArgb(40,255,255,255));
        SetRadius(radius);
        Resources[typeof(Button)] = ButtonStyle("""
 <Border x:Name="surface" CornerRadius="{DynamicResource SystemRadius}" Background="{DynamicResource SystemCard}"
  BorderBrush="{DynamicResource SystemRim}" BorderThickness="1" Padding="{TemplateBinding Padding}">
  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
 </Border>
""", "#26FFFFFF", "#3AFFFFFF");

        // ---- Switches + power, one card
        var panel = new StackPanel();
        panel.Children.Add(Row(
            MakeToggle("internet", "\uE774", "\uE774", L("Интернет", "Internet")),
            MakeToggle("bluetooth", "\uE702", "\uE702", "Bluetooth"),
            MakeToggle("vpn", "\uE705", "\uE705", "VPN"),
            MakeToggle("microphone", "\uE720", "\uEC54", L("Микрофон", "Microphone"))));
        var power = Row(
            Power("sleep", "\uE708", L("Сон", "Sleep")),
            Power("restart", "\uE777", L("Перезагрузка", "Restart")),
            Power("shutdown", "\uE7E8", L("Выключение", "Shut down")));
        power.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(power);
        var controls = new Border { Padding = new Thickness(12, 10, 12, 12), BorderThickness = new Thickness(1), Child = panel, Margin = new Thickness(0, 0, 0, 8) };
        Card(controls);
        Children.Add(controls);

        // ---- Load
        Children.Add(Row(
            Metric("cpu", L("ПРОЦЕССОР", "CPU"), Color.FromRgb(0x0A, 0x84, 0xFF)),
            Metric("gpu", L("ВИДЕОКАРТА", "GPU"), Color.FromRgb(0x30, 0xD1, 0x58)),
            Metric("memory", L("ПАМЯТЬ", "MEMORY"), Color.FromRgb(0xBF, 0x5A, 0xF2))));
        Children.Add(confirmation);
        toggles["internet"].Button.ToolTip = L("Включает и выключает всю сеть: кабель и Wi-Fi. Windows попросит подтверждение администратора.",
                                               "Turns the whole network on and off: cable and Wi-Fi. Windows asks for administrator approval.");
        SetVpnClient(vpnClient);
        foreach (var id in SystemPanelPolicy.Toggles) Render(id);
    }

    Style ButtonStyle(string surface, string hover, string pressed)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 44.0));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse($"""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
{surface}
 <ControlTemplate.Triggers>
  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="{hover}"/></Trigger>
  <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Background" Value="{pressed}"/></Trigger>
  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#99FFFFFF"/></Trigger>
  <Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>
""")));
        return style;
    }

    void Card(Border border)
    {
        border.SetResourceReference(Border.BackgroundProperty, "SystemCard");
        border.SetResourceReference(Border.BorderBrushProperty, "SystemRim");
        border.SetResourceReference(Border.CornerRadiusProperty, "SystemRadius");
    }

    internal void SetVpnClient(string client)
    {
        vpnClient = SystemPanelPolicy.VpnClient(client);
        pending.Remove("vpn"); notes.Remove("vpn");
        var t = toggles["vpn"];
        t.Title.Text = vpnClient == "Happ" ? "Happ VPN" : "VPN";
        t.Button.ToolTip = vpnClient == "Happ"
            ? L("Включает и выключает подключение Happ. Профиль должен быть настроен в Happ.", "Turns the Happ connection on and off. A profile must be set up in Happ.")
            : L("Подключает VPN, добавленный в Параметры Windows (с сохранённым входом).", "Connects the VPN added in Windows Settings (with saved sign-in).");
        AutomationProperties.SetName(t.Button, vpnClient == "Happ" ? "Happ VPN" : "VPN");
        Render("vpn");
    }

    /// <summary>Compatibility with the older Happ request flow: only reports problems, never "connected".</summary>
    internal void SetVpnRequestResult(HappVpnRequestResult result)
    {
        if (result == HappVpnRequestResult.Sent) return;
        pending.Remove("vpn");
        Note("vpn", result == HappVpnRequestResult.ClientMissing ? L("Happ не найден", "Happ not found") : L("Не удалось", "Failed"));
    }

    /// <summary>Show "turning on…" until Windows reports the new state (or the wait expires).</summary>
    internal void SetPending(string id, bool target)
    {
        notes.Remove(id);
        pending[id] = (target, Clock());
        Render(id);
    }
    internal bool IsPending(string id, out bool target, out TimeSpan elapsed)
    {
        target = false; elapsed = TimeSpan.Zero;
        if (!pending.TryGetValue(id, out var p)) return false;
        target = p.Target; elapsed = Clock() - p.Started; return true;
    }
    internal void SetResult(string id, ToggleResult result)
    {
        if (result == ToggleResult.Done) return;
        pending.Remove(id);
        Note(id, result switch
        {
            ToggleResult.Denied => L("Windows запретила", "Blocked by Windows"),
            ToggleResult.Unavailable => L("Нет адаптера", "No adapter"),
            ToggleResult.NoProfile => L("Нет профиля VPN", "No VPN profile"),
            ToggleResult.Cancelled => L("Отменено", "Cancelled"),
            _ => L("Не удалось", "Failed")
        });
    }
    void Note(string id, string text) { notes[id] = (text, Clock().AddSeconds(6)); Render(id); }

    internal void SetRadius(double radius) => Resources["SystemRadius"] = new CornerRadius(Math.Clamp(radius, 0, 18));

    TextBlock Text(string value, double size = 13, bool dim = false, double bottom = 0, bool wrap = false) => new()
    {
        Text = value, Foreground = dim ? Dim : Brushes.White,
        FontSize = size, Margin = new Thickness(0,0,0,bottom),
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis
    };
    Grid Row(params FrameworkElement[] items)
    {
        var grid = new Grid { Margin = new Thickness(0,0,0,8) };
        for (int i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (i > 0) items[i].Margin = new Thickness(8,0,0,0);
            Grid.SetColumn(items[i], i); grid.Children.Add(items[i]);
        }
        return grid;
    }

    FrameworkElement MakeToggle(string id, string onGlyph, string offGlyph, string title)
    {
        var glyph = new TextBlock { Text = onGlyph, FontFamily = Icons, FontSize = 20, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var circle = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Background = OffFill, Child = glyph,
            HorizontalAlignment = HorizontalAlignment.Center };
        var label = Text(title, 12); label.FontWeight = FontWeights.SemiBold; label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0,7,0,0);
        var caption = Text("—", 11, dim: true); caption.HorizontalAlignment = HorizontalAlignment.Center; caption.Margin = new Thickness(0,2,0,0);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(circle); stack.Children.Add(label); stack.Children.Add(caption);
        var button = new Button { Content = stack, Padding = new Thickness(4, 6, 4, 4), HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Style = ButtonStyle("""
 <Border x:Name="surface" CornerRadius="{DynamicResource SystemRadius}" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" Padding="{TemplateBinding Padding}">
  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
 </Border>
""", "#14FFFFFF", "#24FFFFFF");
        toggles[id] = new Toggle(button, circle, glyph, label, caption, onGlyph, offGlyph);
        Register(id, button, title);
        return button;
    }

    Button Power(string id, string glyph, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 15, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) };
        if (id == "shutdown") icon.Foreground = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x60)));
        row.Children.Add(icon);
        var label = Text(title, 12); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
        var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 44 };
        Register(id, button, title);
        return button;
    }

    void Register(string id, Button button, string label)
    {
        buttons[id] = button; AutomationProperties.SetName(button, label); AutomationProperties.SetAutomationId(button, "System-" + id);
        button.Click += (_, _) =>
        {
            if (SystemPanelPolicy.NeedsConfirmation(id)) Confirm(id);
            else { CancelConfirmation(); invoke(id); }
        };
    }

    Border Metric(string id, string title, Color color)
    {
        var panel = new StackPanel();
        var head = Text(title, 10, dim: true, bottom: 5); panel.Children.Add(head);
        var value = Text("—", 20); value.FontWeight = FontWeights.SemiBold; panel.Children.Add(value);
        var track = new Border { Background = new SolidColorBrush(Color.FromArgb(42,255,255,255)), Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0,8,0,0), ClipToBounds = true };
        // ProgressBar uses a minimal, theme-independent template (no system accent or animation).
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 3, Foreground = Freeze(new SolidColorBrush(color)), Background = Brushes.Transparent };
        bar.Template = (ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ProgressBar"><Grid x:Name="PART_Track"><Border x:Name="PART_Indicator" Background="{TemplateBinding Foreground}" HorizontalAlignment="Left" CornerRadius="1.5"/></Grid></ControlTemplate>
""");
        track.Child = bar; panel.Children.Add(track); metrics[id] = (head, value, bar);
        var card = new Border { Padding = new Thickness(14,10,14,12), BorderThickness = new Thickness(1), Child = panel };
        Card(card); return card;
    }

    internal void Update(SystemSnapshot snapshot)
    {
        lastSnapshot = snapshot;
        SetMetric("cpu", snapshot.Cpu is double cpu ? $"{cpu:0}%" : "—", snapshot.Cpu);
        SetMetric("gpu", snapshot.Gpu is double gpu ? $"{gpu:0}%" : "—", snapshot.Gpu);
        metrics["gpu"].Title.Text = snapshot.GpuName is { Length: > 0 } name ? name.ToUpperInvariant() : L("ВИДЕОКАРТА", "GPU");
        metrics["gpu"].Title.ToolTip = snapshot.GpuName;
        SetMetric("memory", snapshot.Memory == null ? "—" : L($"{snapshot.UsedMemory / 1073741824.0:0.0} / {snapshot.TotalMemory / 1073741824.0:0.#} ГБ", $"{snapshot.UsedMemory / 1073741824.0:0.0} / {snapshot.TotalMemory / 1073741824.0:0.#} GB"), snapshot.Memory);
        foreach (var id in SystemPanelPolicy.Toggles) Render(id);
    }

    bool? State(string id) => lastSnapshot == null ? null : id switch
    {
        "internet" => lastSnapshot.Internet, "bluetooth" => lastSnapshot.Bluetooth, "vpn" => lastSnapshot.Vpn,
        "microphone" => lastSnapshot.MicMuted is bool muted ? !muted : null, _ => null
    };

    void Render(string id)
    {
        if (!toggles.TryGetValue(id, out var t)) return;
        bool? on = State(id);
        var now = Clock();
        if (pending.TryGetValue(id, out var p) && (on == p.Target || SystemPanelPolicy.PendingExpired(p.Started, now))) pending.Remove(id);
        bool isPending = pending.TryGetValue(id, out p);
        string? note = notes.TryGetValue(id, out var n) && n.Until > now ? n.Text : null;
        if (note == null) notes.Remove(id);

        bool available = lastSnapshot == null || on != null || id == "vpn";
        t.Button.IsEnabled = available;
        bool lit = isPending ? p.Target : on == true;
        t.Circle.Background = lit ? OnFill : OffFill;
        t.Circle.Opacity = isPending ? 0.72 : 1;
        t.Glyph.Text = id == "microphone" && !lit ? t.OffGlyph : t.OnGlyph;

        string caption;
        if (note != null) caption = note;
        else if (isPending) caption = id == "vpn"
            ? p.Target ? L("Подключаю…", "Connecting…") : L("Отключаю…", "Disconnecting…")
            : p.Target ? L("Включаю…", "Turning on…") : L("Выключаю…", "Turning off…");
        else if (lastSnapshot == null) caption = "—";
        else if (on == null) caption = id switch
        {
            "microphone" => L("Не найден", "Not found"),
            "vpn" => "—",
            _ => L("Нет адаптера", "No adapter")
        };
        else if (id == "internet" && on == true) caption = lastSnapshot.InternetConnected ? L("Подключено", "Connected") : L("Нет сети", "No network");
        else caption = on == true ? L("Вкл.", "On") : L("Выкл.", "Off");
        t.Caption.Text = caption;
        AutomationProperties.SetItemStatus(t.Button, caption);
    }

    void SetMetric(string id, string value, double? percent)
    {
        var metric = metrics[id]; metric.Value.Text = value; metric.Bar.Value = percent ?? 0;
        metric.Bar.Visibility = percent.HasValue ? Visibility.Visible : Visibility.Hidden;
        AutomationProperties.SetName(metric.Value, value);
    }

    void Confirm(string id)
    {
        PendingAction = id; confirmation.Children.Clear();
        string title = id == "sleep" ? L("Перевести компьютер в сон?", "Put this computer to sleep?") : id == "restart" ? L("Перезагрузить компьютер?", "Restart this computer?") : L("Выключить компьютер?", "Shut down this computer?");
        confirmation.Children.Add(Text(title, 13, bottom: 6, wrap: true));
        if (id != "sleep") confirmation.Children.Add(Text(L("Сохрани открытые документы. Принудительное закрытие отключено.", "Save open documents first. Apps will not be force-closed."),11,dim:true,bottom:6,wrap:true));
        var cancel = new Button { Content = Text(L("Отмена", "Cancel"), 12), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_,_) => CancelConfirmation();
        var accept = new Button { Content = Text(L("Подтвердить", "Confirm"),12), HorizontalContentAlignment = HorizontalAlignment.Center };
        accept.Click += (_,_) => { var pendingAction = PendingAction; CancelConfirmation(); if (pendingAction != null) invoke(pendingAction); };
        AutomationProperties.SetAutomationId(cancel,"System-cancel"); AutomationProperties.SetAutomationId(accept,"System-confirm");
        confirmation.Children.Add(Row(cancel,accept)); confirmation.Visibility = Visibility.Visible;
        cancel.Focus(); confirmation.BringIntoView();
    }
    internal void CancelConfirmation() { PendingAction = null; confirmation.Visibility = Visibility.Collapsed; confirmation.Children.Clear(); }
}

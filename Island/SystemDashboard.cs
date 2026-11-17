using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

/// <summary>The same production WPF surface is rendered and inspected by Windows tests.</summary>
internal sealed class SystemDashboard : StackPanel
{
    readonly bool ru;
    TextBlock? vpnTitle;
    readonly Action<string> invoke;
    readonly Dictionary<string, TextBlock> captions = new();
    readonly Dictionary<string, Button> buttons = new();
    readonly Dictionary<string, (TextBlock Value, ProgressBar Bar)> metrics = new();
    readonly StackPanel confirmation = new() { Visibility = Visibility.Collapsed };
    internal string? PendingAction { get; private set; }
    internal bool ConfirmationVisible => confirmation.Visibility == Visibility.Visible;
    string L(string r, string e) => ru ? r : e;

    internal SystemDashboard(bool russian, double radius, Action<string> action, Brush? card = null, Brush? rim = null, string vpnClient = "Windows")
    {
        ru = russian; invoke = action;
        Resources["SystemCard"] = card ?? new LinearGradientBrush(Color.FromArgb(38,255,255,255), Color.FromArgb(13,255,255,255), 90);
        Resources["SystemRim"] = rim ?? new SolidColorBrush(Color.FromArgb(40,255,255,255));
        SetRadius(radius);
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        buttonStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        buttonStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12,8,12,8)));
        buttonStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 44.0));
        buttonStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        buttonStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
 <Border x:Name="surface" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" CornerRadius="{DynamicResource SystemRadius}"
 Background="{DynamicResource SystemCard}" BorderBrush="{DynamicResource SystemRim}" BorderThickness="1" Padding="{TemplateBinding Padding}">
  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="#26FFFFFF"/></Trigger>
  <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Background" Value="#3AFFFFFF"/></Trigger>
  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="White"/></Trigger>
  <Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>
""")));
        Resources[typeof(Button)] = buttonStyle;
        Children.Add(Text(L("ПУНКТ УПРАВЛЕНИЯ", "CONTROL CENTER"), 11, dim: true, bottom: 8));
        Children.Add(Row(
            Tile("wifi", "\uE701", "Wi-Fi", L("Параметры ↗", "Settings ↗")),
            Tile("bluetooth", "\uE702", "Bluetooth", L("Параметры ↗", "Settings ↗")),
            Tile("airplane", "\uE709", L("Авиарежим", "Airplane mode"), L("Параметры ↗", "Settings ↗")),
            Tile("vpn", "\uE83D", "VPN", L("Параметры ↗", "Settings ↗")),
            Tile("desktop", "\uE7F4", L("Рабочий стол", "Desktop"), L("Показать / скрыть", "Show / hide"))));
        Children.Add(Row(Metric("cpu", L("ПРОЦЕССОР", "CPU")), Metric("memory", L("ПАМЯТЬ", "MEMORY")), Metric("battery", L("ПИТАНИЕ", "POWER"))));
        Children.Add(Row(
            Tile("sound", "\uE767", L("Звук", "Sound"), L("Параметры ↗", "Settings ↗"), horizontal: true),
            Tile("microphone", "\uE720", L("Микрофон", "Microphone"), L("Проверяю…", "Checking…"), horizontal: true),
            Tile("notifications", "\uE708", L("Уведомления", "Notifications"), L("Не беспокоить ↗", "Do not disturb ↗"), horizontal: true)));
        buttons["microphone"].IsEnabled = false;
        Children.Add(Row(
            Small("microphone-settings", L("Доступ к микрофону ↗", "Microphone access ↗")),
            Small("camera", L("Камера ↗", "Camera ↗")),
            Small("taskmanager", L("Диспетчер задач ↗", "Task Manager ↗"))));
        Children.Add(Row(
            Small("lock", L("Блокировка", "Lock")), Small("sleep", L("Сон", "Sleep")),
            Small("restart", L("Перезагрузка", "Restart")), Small("shutdown", L("Выключение", "Shut down"))));
        Children.Add(confirmation);
        SetVpnClient(vpnClient);
    }
    internal void SetVpnClient(string client)
    {
        bool happ = SystemPanelPolicy.VpnClient(client) == "Happ";
        if (vpnTitle != null) vpnTitle.Text = happ ? "Happ VPN" : "VPN";
        captions["vpn"].Text = happ ? L("Подключить", "Connect") : L("Параметры ↗", "Settings ↗");
        buttons["vpn"].ToolTip = happ
            ? L("Отправить Happ команду подключения. Профиль должен быть настроен в Happ. Статус туннеля Moku не проверяет.", "Send Happ a connection request. A profile must be configured in Happ. Moku does not verify tunnel status.")
            : L("Открыть параметры VPN Windows", "Open Windows VPN settings");
        AutomationProperties.SetName(buttons["vpn"], happ ? L("Happ — подключить VPN", "Happ — connect VPN") : L("Параметры VPN Windows", "Windows VPN settings"));
    }
    internal void SetVpnRequestResult(HappVpnRequestResult result)
    {
        captions["vpn"].Text = result switch
        {
            HappVpnRequestResult.Sent => L("Запрос отправлен", "Request sent"),
            HappVpnRequestResult.ClientMissing => L("Happ не найден", "Happ not found"),
            _ => L("Ошибка команды", "Request failed")
        };
        buttons["vpn"].ToolTip = result == HappVpnRequestResult.ClientMissing
            ? L("Установи Happ для Windows с поддержкой ссылок happ://. Затем настрой профиль в Happ.", "Install Happ for Windows with happ:// link support. Then configure a profile in Happ.")
            : result == HappVpnRequestResult.Sent
                ? L("Команда передана Windows. Результат подключения смотри в Happ: отправка команды не подтверждает работу VPN.", "The request was handed to Windows. Check Happ for the result: sending a request does not confirm an active VPN.")
                : L("Не удалось отправить команду Happ. Проверь его установку и регистрацию ссылок happ://.", "Could not send the request to Happ. Check its installation and happ:// link registration.");
    }
    internal void SetRadius(double radius) => Resources["SystemRadius"] = new CornerRadius(Math.Clamp(radius, 0, 18));
    TextBlock Text(string value, double size = 13, bool dim = false, double bottom = 0) => new()
    {
        Text = value, Foreground = dim ? new SolidColorBrush(Color.FromRgb(0xB0,0xB0,0xB8)) : Brushes.White,
        FontSize = size, Margin = new Thickness(0,0,0,bottom), TextWrapping = TextWrapping.Wrap
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
    Button Tile(string id, string glyph, string title, string caption, bool horizontal = false)
    {
        var panel = new StackPanel();
        var icon = Text(glyph, 19); icon.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        icon.Margin = new Thickness(0,0,0,6);
        if (!horizontal) panel.Children.Add(icon);
        var label = Text(title, 12); label.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(label);
        if (id == "vpn") vpnTitle = label;
        var sub = Text(caption, 11, dim: true); sub.Margin = new Thickness(0,4,0,0); panel.Children.Add(sub);
        var button = new Button { Content = panel, MinHeight = horizontal ? 65 : 92 };
        captions[id] = sub; Register(id, button, title); return button;
    }
    Button Small(string id, string title)
    {
        var button = new Button { Content = Text(title, 12) }; Register(id, button, title); return button;
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
    Border Metric(string id, string title)
    {
        var panel = new StackPanel(); panel.Children.Add(Text(title, 10, dim: true, bottom: 5));
        var value = Text("—", 20); value.FontWeight = FontWeights.SemiBold; panel.Children.Add(value);
        var track = new Border { Background = new SolidColorBrush(Color.FromArgb(42,255,255,255)), Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0,7,0,0), ClipToBounds = true };
        // ProgressBar uses a minimal, theme-independent template (no system accent or animation).
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 3, Foreground = Brushes.White, Background = Brushes.Transparent };
        bar.Template = (ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ProgressBar"><Grid x:Name="PART_Track"><Border x:Name="PART_Indicator" Background="{TemplateBinding Foreground}" HorizontalAlignment="Left" CornerRadius="1.5"/></Grid></ControlTemplate>
""");
        track.Child = bar; panel.Children.Add(track); metrics[id] = (value, bar);
        var card = new Border { Padding = new Thickness(14,10,14,10), BorderThickness = new Thickness(1), Child = panel };
        card.SetResourceReference(Border.BackgroundProperty,"SystemCard"); card.SetResourceReference(Border.BorderBrushProperty,"SystemRim");
        card.SetResourceReference(Border.CornerRadiusProperty,"SystemRadius"); return card;
    }
    internal void Update(SystemSnapshot snapshot)
    {
        SetMetric("cpu", snapshot.Cpu is double cpu ? $"{cpu:0}%" : "—", snapshot.Cpu);
        SetMetric("memory", snapshot.Memory == null ? "—" : L($"{snapshot.UsedMemory / 1073741824.0:0.0} / {snapshot.TotalMemory / 1073741824.0:0.#} ГБ", $"{snapshot.UsedMemory / 1073741824.0:0.0} / {snapshot.TotalMemory / 1073741824.0:0.#} GB"), snapshot.Memory);
        SetMetric("battery", snapshot.Battery is int percent ? $"{percent}%" + (snapshot.PluggedIn ? " ⚡" : "") : snapshot.HasBattery == false ? L("От сети", "AC power") : "—", snapshot.Battery);
        captions["wifi"].Text = snapshot.Wifi == true ? L("Подключено ↗", "Connected ↗") : snapshot.Wifi == false ? L("Нет связи ↗", "Not connected ↗") : L("Параметры ↗", "Settings ↗");
        captions["sound"].Text = snapshot.Volume is int volume ? snapshot.OutputMuted == true ? L("Звук выключен ↗", "Muted ↗") : L($"Громкость {volume}% ↗", $"Volume {volume}% ↗") : L("Параметры ↗", "Settings ↗");
        captions["microphone"].Text = snapshot.MicMuted == null ? L("Не найден", "Not available") : snapshot.MicMuted == true ? L("Выкл. · включить", "Muted · unmute") : L("Вкл. · выключить", "On · mute");
        buttons["microphone"].IsEnabled = snapshot.MicMuted != null;
        buttons["microphone"].ToolTip = L("Микрофон связи по умолчанию. Приложения могут использовать другой вход.", "Default communications microphone. Apps can use another input.");
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
        confirmation.Children.Add(Text(title, 13, bottom: 6));
        if (id != "sleep") confirmation.Children.Add(Text(L("Сохрани открытые документы. Принудительное закрытие отключено.", "Save open documents first. Apps will not be force-closed."),11,dim:true,bottom:6));
        var cancel = new Button { Content = Text(L("Отмена", "Cancel"), 12) };
        cancel.Click += (_,_) => CancelConfirmation();
        var accept = new Button { Content = Text(L("Подтвердить", "Confirm"),12) };
        accept.Click += (_,_) => { var pending = PendingAction; CancelConfirmation(); if (pending != null) invoke(pending); };
        AutomationProperties.SetAutomationId(cancel,"System-cancel"); AutomationProperties.SetAutomationId(accept,"System-confirm");
        confirmation.Children.Add(Row(cancel,accept)); confirmation.Visibility = Visibility.Visible;
        cancel.Focus(); confirmation.BringIntoView();
    }
    internal void CancelConfirmation() { PendingAction = null; confirmation.Visibility = Visibility.Collapsed; confirmation.Children.Clear(); }
}

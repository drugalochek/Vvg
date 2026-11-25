using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Island;

internal sealed record LinkPhoneView(string Id, string Name, bool Online, int Pending);
internal sealed record LinkItemView(string Name, long Size, bool Incoming, string Status, string? Path, bool Done);
internal sealed record LinkView(
    bool Enabled, string? Error, bool Pairing, byte[]? QrPng, string? PairCode, string? Address,
    IReadOnlyList<LinkPhoneView> Phones, string? Selected, IReadOnlyList<LinkItemView> Recent,
    string Folder, string? Progress);

/// <summary>Вкладка «Телефон»: подключение приложения Moku по QR, отправка файлов перетаскиванием, история передач.</summary>
internal sealed class TransferDashboard : StackPanel
{
    readonly bool ru;
    readonly Action<string, string?> invoke;
    static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)));
    static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)));
    static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB2)));
    static readonly Brush Faint = Freeze(new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)));
    static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static T Freeze<T>(T b) where T : Freezable { b.Freeze(); return b; }
    string L(string r, string e) => ru ? r : e;
    internal Border? DropZone { get; private set; }

    internal TransferDashboard(bool russian, double radius, Action<string, string?> action, Brush? card = null, Brush? rim = null)
    {
        ru = russian; invoke = action;
        Resources["LinkCard"] = card ?? new LinearGradientBrush(Color.FromArgb(38, 255, 255, 255), Color.FromArgb(13, 255, 255, 255), 90);
        Resources["LinkRim"] = rim ?? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        SetRadius(radius);
        Resources[typeof(Button)] = ButtonStyle("#26FFFFFF", "#3AFFFFFF");
        Resources["Primary"] = PrimaryStyle();
    }

    internal void SetRadius(double radius) => Resources["LinkRadius"] = new CornerRadius(Math.Clamp(radius, 0, 18));

    Style ButtonStyle(string hover, string pressed)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 8, 14, 8)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36.0));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse($$"""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
 <Border x:Name="surface" CornerRadius="{DynamicResource LinkRadius}" Background="{DynamicResource LinkCard}"
  BorderBrush="{DynamicResource LinkRim}" BorderThickness="1" Padding="{TemplateBinding Padding}">
  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="{{hover}}"/></Trigger>
  <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Background" Value="{{pressed}}"/></Trigger>
  <Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>
""")));
        return style;
    }

    Style PrimaryStyle()
    {
        var style = new Style(typeof(Button), (Style)Resources[typeof(Button)]);
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
 <Border x:Name="surface" CornerRadius="{DynamicResource LinkRadius}" Background="#FFFFFFFF" Padding="{TemplateBinding Padding}">
  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="#FF000000"/>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="#FFE6E6EA"/></Trigger>
  <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Background" Value="#FFCFCFD6"/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>
""")));
        return style;
    }

    Border Card(UIElement child, Thickness? padding = null)
    {
        var b = new Border { BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8), Child = child };
        b.SetResourceReference(Border.BackgroundProperty, "LinkCard");
        b.SetResourceReference(Border.BorderBrushProperty, "LinkRim");
        b.SetResourceReference(Border.CornerRadiusProperty, "LinkRadius");
        return b;
    }

    static TextBlock Text(string value, double size = 13, Brush? color = null, bool bold = false, bool wrap = false) => new()
    {
        Text = value, FontSize = size, Foreground = color ?? Brushes.White, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    static TextBlock Glyph(string g, double size, Brush? color = null) => new()
    {
        Text = g, FontFamily = Icons, FontSize = size, Foreground = color ?? Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    Button Btn(string label, string action, string? arg = null, string? glyph = null, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph != null) { var g = Glyph(glyph, 13, primary ? Brushes.Black : Brushes.White); g.Margin = new Thickness(0, 0, 8, 0); content.Children.Add(g); }
        var t = Text(label, 12.5, primary ? Brushes.Black : Brushes.White, bold: primary);
        content.Children.Add(t);
        var b = new Button { Content = content };
        if (primary) b.Style = (Style)Resources["Primary"];
        b.Click += (_, _) => invoke(action, arg);
        return b;
    }

    internal void Render(LinkView v)
    {
        Children.Clear(); DropZone = null;

        // ---- Заголовок: телефон + статус + выключатель передачи
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var online = v.Phones.FirstOrDefault(p => p.Id == v.Selected) ?? v.Phones.FirstOrDefault();
        var circle = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = v.Enabled ? Accent : Faint, Child = Glyph("\uE8EA", 20) };
        head.Children.Add(circle);
        var titles = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Text(online?.Name ?? L("Телефон", "Phone"), 14, bold: true));
        string sub = !v.Enabled ? L("Передача выключена — Moku ничего не слушает в сети", "Transfer is off — Moku is not listening on the network")
            : v.Error != null ? v.Error
            : v.Phones.Count == 0 ? L("Телефон не подключён", "No phone connected")
            : online!.Online ? L("В сети · Wi-Fi", "Online · Wi-Fi") : L("Не в сети — открой Moku на телефоне", "Offline — open Moku on the phone");
        var subText = Text(sub, 11.5, v.Error != null ? Red : Dim, wrap: true);
        titles.Children.Add(subText);
        if (v.Enabled && online != null && v.Error == null)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = online.Online ? Green : Faint, Margin = new Thickness(0, 0, 6, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            titles.Children.Remove(subText);
            row.Children.Add(dot); row.Children.Add(subText);
            titles.Children.Add(row);
        }
        Grid.SetColumn(titles, 1); head.Children.Add(titles);
        var toggle = Btn(v.Enabled ? L("Выключить", "Turn off") : L("Включить передачу", "Turn on transfer"), "toggle", null, v.Enabled ? "\uE7E8" : "\uE8AB", primary: !v.Enabled);
        Grid.SetColumn(toggle, 2); head.Children.Add(toggle);
        Children.Add(Card(head));

        if (!v.Enabled)
        {
            var info = new StackPanel();
            info.Children.Add(Text(L("Как это работает", "How it works"), 13, bold: true));
            foreach (var line in new[]
            {
                L("1. Установи приложение Moku на Android-телефон.", "1. Install the Moku app on your Android phone."),
                L("2. Включи передачу и отсканируй QR-код в приложении.", "2. Turn on transfer and scan the QR code in the app."),
                L("3. Перетаскивай файлы сюда, а с телефона жми «Поделиться → Moku».", "3. Drop files here; on the phone tap Share → Moku."),
            })
            { var t = Text(line, 12, Dim, wrap: true); t.Margin = new Thickness(0, 6, 0, 0); info.Children.Add(t); }
            var note = Text(L("Файлы идут напрямую по твоему Wi-Fi, без интернета и облака.", "Files go directly over your Wi-Fi — no internet, no cloud."), 11.5, Dim, wrap: true);
            note.Margin = new Thickness(0, 10, 0, 0); info.Children.Add(note);
            Children.Add(Card(info));
            return;
        }

        if (v.Error != null && v.Phones.Count == 0 && !v.Pairing) return;

        if (v.Pairing || v.Phones.Count == 0)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var qrHost = new Border { Width = 168, Height = 168, Background = Brushes.White, Padding = new Thickness(6), CornerRadius = new CornerRadius(14) };
            if (v.QrPng != null)
            {
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(v.QrPng)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); }
                bmp.Freeze();
                var img = new System.Windows.Controls.Image { Source = bmp, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                qrHost.Child = img;
            }
            else qrHost.Child = Text(L("Нет сети", "No network"), 12, Brushes.Black);
            grid.Children.Add(qrHost);
            var steps = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            steps.Children.Add(Text(L("Подключи телефон", "Connect your phone"), 15, bold: true));
            foreach (var line in new[]
            {
                L("1. Открой Moku на телефоне", "1. Open Moku on the phone"),
                L("2. Нажми «Подключить ПК»", "2. Tap “Connect PC”"),
                L("3. Наведи камеру на QR-код", "3. Point the camera at the QR code"),
            })
            { var t = Text(line, 12.5, Dim); t.Margin = new Thickness(0, 7, 0, 0); steps.Children.Add(t); }
            if (v.PairCode != null)
            {
                var code = Text(L("Код: ", "Code: ") + v.PairCode[..3] + " " + v.PairCode[3..] + (v.Address != null ? "  ·  " + v.Address : ""), 11.5, Dim);
                code.Margin = new Thickness(0, 12, 0, 0); steps.Children.Add(code);
                var exp = Text(L("Код одноразовый и действует 5 минут", "One-time code, valid for 5 minutes"), 11, Dim);
                exp.Margin = new Thickness(0, 2, 0, 0); steps.Children.Add(exp);
            }
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            actions.Children.Add(Btn(L("Новый код", "New code"), "pair", null, "\uE72C"));
            if (v.Phones.Count > 0) { var c = Btn(L("Отмена", "Cancel"), "cancelpair"); c.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(c); }
            steps.Children.Add(actions);
            Grid.SetColumn(steps, 1); grid.Children.Add(steps);
            Children.Add(Card(grid));
            return;
        }

        // ---- Выбор телефона, если их несколько
        if (v.Phones.Count > 1)
        {
            var chips = new WrapPanel();
            foreach (var p in v.Phones)
            {
                var b = Btn((p.Online ? "● " : "○ ") + p.Name, "select", p.Id);
                b.Margin = new Thickness(0, 0, 6, 6);
                if (p.Id == v.Selected) b.BorderBrush = Brushes.White;
                if (p.Id == v.Selected && b.Content is StackPanel sp && sp.Children[^1] is TextBlock tb) tb.FontWeight = FontWeights.SemiBold;
                chips.Children.Add(b);
            }
            Children.Add(chips);
        }

        // ---- Зона перетаскивания
        var dz = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var up = Glyph("\uE898", 26); up.Margin = new Thickness(0, 0, 0, 8); dz.Children.Add(up);
        var dzTitle = Text(L("Перетащи файлы сюда", "Drop files here"), 14, bold: true); dzTitle.HorizontalAlignment = HorizontalAlignment.Center; dz.Children.Add(dzTitle);
        var dzSub = Text(online != null ? L($"Они сразу уйдут на «{online.Name}»", $"They go straight to “{online.Name}”") : "", 12, Dim);
        dzSub.HorizontalAlignment = HorizontalAlignment.Center; dzSub.Margin = new Thickness(0, 3, 0, 10); dz.Children.Add(dzSub);
        var pick = Btn(L("Выбрать файлы", "Choose files"), "pick", null, "\uE710", primary: true);
        pick.HorizontalAlignment = HorizontalAlignment.Center; dz.Children.Add(pick);
        var dash = new Rectangle { StrokeDashArray = new DoubleCollection { 4, 3 }, StrokeThickness = 1.2, Stroke = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), RadiusX = 14, RadiusY = 14, IsHitTestVisible = false };
        var dzGrid = new Grid { Background = Brushes.Transparent };
        dzGrid.Children.Add(dash);
        dzGrid.Children.Add(new Border { Padding = new Thickness(16, 18, 16, 18), Child = dz });
        var zone = Card(dzGrid, new Thickness(6));
        zone.AllowDrop = true;
        DropZone = zone;
        Children.Add(zone);
        if (v.Progress != null)
        {
            var pr = Text(v.Progress, 12, Dim); pr.Margin = new Thickness(4, -2, 0, 8); Children.Add(pr);
        }

        // ---- Последние передачи
        if (v.Recent.Count > 0)
        {
            var list = new StackPanel();
            list.Children.Add(Text(L("НЕДАВНИЕ", "RECENT"), 11, Dim));
            foreach (var it in v.Recent.Take(6))
            {
                var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var ic = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Faint, Child = Glyph(it.Incoming ? "\uE896" : "\uE898", 13, it.Incoming ? Green : Accent) };
                row.Children.Add(ic);
                var names = new StackPanel { Margin = new Thickness(10, 0, 8, 0) };
                names.Children.Add(Text(it.Name, 12.5));
                names.Children.Add(Text(TransferPolicy.FormatSize(it.Size, ru) + " · " + it.Status, 11, it.Done ? Dim : Accent));
                Grid.SetColumn(names, 1); row.Children.Add(names);
                if (it.Incoming && it.Path != null)
                {
                    var open = Btn(L("Открыть", "Open"), "open", it.Path); open.MinHeight = 30; open.Padding = new Thickness(12, 4, 12, 4);
                    Grid.SetColumn(open, 2); row.Children.Add(open);
                }
                list.Children.Add(row);
            }
            Children.Add(Card(list));
        }

        // ---- Нижняя строка
        var bottom = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        void Add(Button b) { b.Margin = new Thickness(0, 0, 8, 0); bottom.Children.Add(b); }
        Add(Btn(L("Папка загрузок", "Downloads folder"), "folder", null, "\uE838"));
        Add(Btn(L("Подключить ещё", "Connect another"), "pair", null, "\uE710"));
        if (online != null) Add(Btn(L("Отключить телефон", "Unlink phone"), "unlink", online.Id, "\uE711"));
        Children.Add(bottom);
        var hint = Text(L("Сохраняется в: ", "Saved to: ") + v.Folder, 11, Dim);
        hint.Margin = new Thickness(2, 6, 0, 0);
        Children.Add(hint);
    }
}

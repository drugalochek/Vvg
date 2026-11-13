using TextDataFormat = System.Windows.TextDataFormat;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using WPath = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using RadioButton = System.Windows.Controls.RadioButton;
using Size = System.Windows.Size;
using Rectangle = System.Windows.Shapes.Rectangle;
using Clipboard = System.Windows.Clipboard;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Island;

/// <summary>Скриншотер: выделил область → порисовал → скопировал или сохранил.</summary>
public partial class SnipWindow : Window
{
    enum Phase { Select, Edit }
    enum Mode { None, Selecting, Drawing }

    static readonly string[] Palette = { "#FFFF453A", "#FFFFD60A", "#FF30D158", "#FF0A84FF", "#FFFFFFFF", "#FF000000" };
    static readonly double[] Thicknesses = { 2, 3, 5, 8 };

    readonly double _w, _h, _sx, _sy;
    readonly Action<string, bool> _done;
    readonly RectangleGeometry _selGeo = new(), _clipGeo = new();
    readonly List<UIElement> _undo = new();

    Phase _phase = Phase.Select;
    Mode _mode = Mode.None;
    Rect _sel = Rect.Empty;
    Point _start, _p0;
    UIElement? _cur;

    string _tool = "Pen";
    Color _color = (Color)ColorConverter.ConvertFromString(Palette[0]);
    int _thickIdx = 1;

    public SnipWindow(BitmapSource shot, double sx, double sy, Action<string, bool> done)
    {
        InitializeComponent();
        ApplyLanguage();
        _w = SystemParameters.PrimaryScreenWidth;
        _h = SystemParameters.PrimaryScreenHeight;
        _sx = sx; _sy = sy; _done = done;
        Width = _w; Height = _h;
        Bg.Source = shot;

        _selGeo.Rect = new Rect(0, 0, 0, 0);
        _clipGeo.Rect = new Rect(0, 0, 0, 0);
        Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, _w, _h)), _selGeo);
        Ink.Clip = _clipGeo;

        BuildSwatches();
        UpdateThickLabel();
        Loaded += (_, _) => Activate();
        Closed += (_, _) => { Bg.Source = null; Ink.Children.Clear(); _undo.Clear(); };
    }


    bool _ru;
    string Tr(string ru, string en) => _ru ? ru : en;

    void ApplyLanguage()
    {
        bool ru = _ru = Settings.Load().Language == "ru";
        Title = ru ? "Moku — Снимок экрана" : "Moku — Screenshot";
        PenTool.Content = ru ? "Перо" : "Pen";
        ArrowTool.Content = ru ? "Стрелка" : "Arrow";
        RectangleTool.Content = ru ? "Прямоугольник" : "Rectangle";
        HighlighterTool.Content = ru ? "Маркер" : "Highlighter";
        ThickBtn.ToolTip = ru ? "Толщина линии" : "Thickness";
        UndoButton.ToolTip = ru ? "Отменить (Ctrl+Z)" : "Undo (Ctrl+Z)";
        SaveButton.ToolTip = ru ? "Сохранить (Ctrl+S)" : "Save (Ctrl+S)";
        CopyButton.ToolTip = ru ? "Копировать (Enter)" : "Copy (Enter)";
        CloseButton.ToolTip = ru ? "Закрыть (Esc)" : "Close (Esc)";
        Hint.Text = ru ? "Выделите область экрана  ·  Esc — отмена" : "Select a screen area  ·  Esc — cancel";
    }

    // ================= Панель инструментов =================
    void BuildSwatches()
    {
        foreach (var hex in Palette)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var dot = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
                Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(c),
                BorderThickness = new Thickness(2),
                BorderBrush = hex == Palette[0] ? Brushes.White : Brushes.Transparent,
                Tag = hex
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                _color = c;
                foreach (Border b in ColorPanel.Children)
                    b.BorderBrush = (string)b.Tag == hex ? Brushes.White : Brushes.Transparent;
            };
            ColorPanel.Children.Add(dot);
        }
    }

    void Tool_Checked(object sender, RoutedEventArgs e) => _tool = (string)((RadioButton)sender).Tag;

    void Thick_Click(object sender, RoutedEventArgs e)
    {
        _thickIdx = (_thickIdx + 1) % Thicknesses.Length;
        UpdateThickLabel();
    }

    void UpdateThickLabel() => ThickBtn.Content = Thicknesses[_thickIdx] + " px";

    void Toolbar_Swallow(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void ShowToolbar()
    {
        Toolbar.Visibility = Visibility.Visible;
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = Toolbar.DesiredSize;
        double x = Math.Clamp(_sel.Right - sz.Width, 8, Math.Max(8, _w - sz.Width - 8));
        double y = _sel.Bottom + 8;
        if (y + sz.Height > _h - 8) y = _sel.Top - sz.Height - 8;
        if (y < 8) y = _sel.Bottom - sz.Height - 8;
        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    // ================= Мышь =================
    void Overlay_Down(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Overlay);
        if (_phase == Phase.Edit && !_sel.IsEmpty && _sel.Contains(p)) BeginDraw(p);
        else BeginSelect(p);
        Overlay.CaptureMouse();
    }

    void Overlay_Move(object sender, MouseEventArgs e)
    {
        if (_mode == Mode.None) return;
        var p = e.GetPosition(Overlay);
        if (_mode == Mode.Selecting)
        {
            p = new Point(Math.Clamp(p.X, 0, _w), Math.Clamp(p.Y, 0, _h));
            _sel = new Rect(_start, p);
            UpdateSelectionVisuals();
        }
        else if (_mode == Mode.Drawing)
        {
            p = new Point(Math.Clamp(p.X, _sel.Left, _sel.Right), Math.Clamp(p.Y, _sel.Top, _sel.Bottom));
            UpdateDraw(p);
        }
    }

    void Overlay_Up(object sender, MouseButtonEventArgs e)
    {
        Overlay.ReleaseMouseCapture();
        if (_mode == Mode.Selecting)
        {
            _mode = Mode.None;
            if (_sel.IsEmpty || _sel.Width < 6 || _sel.Height < 6)
            {
                _sel = Rect.Empty;
                UpdateSelectionVisuals();
                Hint.Visibility = Visibility.Visible;
                return;
            }
            _phase = Phase.Edit;
            ShowToolbar();
        }
        else if (_mode == Mode.Drawing)
        {
            _mode = Mode.None;
            _cur = null;
        }
    }

    void BeginSelect(Point p)
    {
        Ink.Children.Clear();
        _undo.Clear();
        Toolbar.Visibility = Visibility.Collapsed;
        Hint.Visibility = Visibility.Collapsed;
        _phase = Phase.Select;
        _mode = Mode.Selecting;
        _start = p;
        _sel = new Rect(p, p);
        UpdateSelectionVisuals();
    }

    void UpdateSelectionVisuals()
    {
        if (_sel.IsEmpty)
        {
            _selGeo.Rect = new Rect(0, 0, 0, 0);
            _clipGeo.Rect = new Rect(0, 0, 0, 0);
            SelRect.Visibility = Visibility.Collapsed;
            Badge.Visibility = Visibility.Collapsed;
            return;
        }
        _selGeo.Rect = _sel;
        _clipGeo.Rect = _sel;
        SelRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelRect, _sel.X);
        Canvas.SetTop(SelRect, _sel.Y);
        SelRect.Width = _sel.Width;
        SelRect.Height = _sel.Height;

        BadgeText.Text = $"{(int)Math.Round(_sel.Width * _sx)} × {(int)Math.Round(_sel.Height * _sy)}";
        Badge.Visibility = Visibility.Visible;
        Canvas.SetLeft(Badge, _sel.X);
        Canvas.SetTop(Badge, _sel.Y > 28 ? _sel.Y - 26 : _sel.Y + 6);
    }

    // ================= Рисование =================
    SolidColorBrush NewBrush() => new(_color);

    void BeginDraw(Point p)
    {
        _mode = Mode.Drawing;
        _p0 = p;
        double t = Thicknesses[_thickIdx];

        switch (_tool)
        {
            case "Pen":
            case "Marker":
                bool marker = _tool == "Marker";
                var pl = new Polyline
                {
                    Stroke = NewBrush(),
                    StrokeThickness = marker ? t * 4 : t,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = marker ? PenLineCap.Flat : PenLineCap.Round,
                    StrokeEndLineCap = marker ? PenLineCap.Flat : PenLineCap.Round,
                    Opacity = marker ? 0.4 : 1
                };
                pl.Points.Add(p);
                _cur = pl;
                break;
            case "Rect":
                _cur = new Rectangle { Stroke = NewBrush(), StrokeThickness = t, RadiusX = 2, RadiusY = 2 };
                break;
            default: // Arrow
                _cur = new WPath
                {
                    Stroke = NewBrush(), StrokeThickness = t,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Data = ArrowGeo(p, p, t)
                };
                break;
        }
        Ink.Children.Add(_cur);
        _undo.Add(_cur);
    }

    void UpdateDraw(Point p)
    {
        switch (_cur)
        {
            case Polyline pl:
                var last = pl.Points[^1];
                if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) > 1.5) pl.Points.Add(p);
                break;
            case Rectangle r:
                var rc = new Rect(_p0, p);
                Canvas.SetLeft(r, rc.X);
                Canvas.SetTop(r, rc.Y);
                r.Width = rc.Width;
                r.Height = rc.Height;
                break;
            case WPath a:
                a.Data = ArrowGeo(_p0, p, Thicknesses[_thickIdx]);
                break;
        }
    }

    static Geometry ArrowGeo(Point a, Point b, double t)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(a, false, false);
            c.LineTo(b, true, false);
            double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double len = Math.Max(14, t * 4.5);
            var p1 = new Point(b.X - len * Math.Cos(ang - 0.45), b.Y - len * Math.Sin(ang - 0.45));
            var p2 = new Point(b.X - len * Math.Cos(ang + 0.45), b.Y - len * Math.Sin(ang + 0.45));
            c.BeginFigure(p1, false, false);
            c.LineTo(b, true, false);
            c.LineTo(p2, true, false);
        }
        return g;
    }

    // ================= Результат =================
    BitmapSource RenderSelection()
    {
        double w = Stage.ActualWidth, h = Stage.ActualHeight;
        var full = new RenderTargetBitmap(
            (int)Math.Round(w * _sx), (int)Math.Round(h * _sy), 96 * _sx, 96 * _sy, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var vb = new VisualBrush(Stage)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, h),
                Stretch = Stretch.Fill
            };
            dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
        }
        full.Render(dv);

        int x = Math.Clamp((int)Math.Round(_sel.X * _sx), 0, full.PixelWidth - 1);
        int y = Math.Clamp((int)Math.Round(_sel.Y * _sy), 0, full.PixelHeight - 1);
        int cw = Math.Clamp((int)Math.Round(_sel.Width * _sx), 1, full.PixelWidth - x);
        int ch = Math.Clamp((int)Math.Round(_sel.Height * _sy), 1, full.PixelHeight - y);
        var crop = new CroppedBitmap(full, new Int32Rect(x, y, cw, ch));
        crop.Freeze();
        return crop;
    }

    void Copy_Click(object s, RoutedEventArgs e) => CopyAndClose();
    void Close_Click(object s, RoutedEventArgs e) => Close();
    void Undo_Click(object s, RoutedEventArgs e) => Undo();
    void Save_Click(object s, RoutedEventArgs e) => SaveToFile();

    void Undo()
    {
        if (_undo.Count == 0) return;
        Ink.Children.Remove(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    void CopyAndClose()
    {
        if (_phase != Phase.Edit || _sel.IsEmpty) return;
        var bmp = RenderSelection();
        bool ok = false;
        for (int attempt = 0; attempt < 4 && !ok; attempt++)
        {
            try { Clipboard.SetImage(bmp); ok = true; }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            {
                Log.Warn("Clipboard.SetImage (попытка " + (attempt + 1) + "): " + ex.Message);
                System.Threading.Thread.Sleep(60);
            }
        }
        // Раньше сообщение «Скопирован» показывалось даже при неудаче — теперь честный результат.
        Finish(ok ? Tr("Скопировано в буфер обмена", "Copied to clipboard") : Tr("Буфер обмена занят — попробуй ещё раз", "Clipboard is busy — please try again"), ok);
    }

    void SaveToFile()
    {
        if (_phase != Phase.Edit || _sel.IsEmpty) return;
        var dlg = new SaveFileDialog
        {
            Filter = "PNG|*.png",
            FileName = $"Moku_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(RenderSelection()));
            using var fs = File.Create(dlg.FileName);
            enc.Save(fs);
            Finish(Tr("Сохранено: ", "Saved: ") + System.IO.Path.GetFileName(dlg.FileName), true);
        }
        catch (Exception ex) { Finish(Tr("Ошибка: ", "Error: ") + ex.Message, false); }
    }

    void Finish(string message, bool ok)
    {
        Close();
        _done(message, ok);
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Enter || (ctrl && e.Key == Key.C)) CopyAndClose();
        else if (ctrl && e.Key == Key.Z) Undo();
        else if (ctrl && e.Key == Key.S) SaveToFile();
    }
}

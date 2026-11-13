using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;

namespace Island;

/// <summary>v2.10: небольшое окно ввода текста в стиле острова (переименование плиток и папок).</summary>
public sealed class PromptWindow : Window
{
    readonly TextBox _box = new();
    public string Value => _box.Text.Trim();

    public PromptWindow(string title, string initial, bool russian, int maxLength = 60)
    {
        Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.None;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var shell = new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(248, 34, 37, 49), 0),
                new GradientStop(Color.FromArgb(242, 18, 20, 29), 1)
            }, new Point(0, 0), new Point(0, 1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = .45, Color = Colors.Black }
        };
        Content = shell;

        var root = new StackPanel();
        shell.Child = root;
        root.Children.Add(new TextBlock
        {
            Text = title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap
        });

        _box.Text = initial; _box.MaxLength = maxLength; _box.Height = 34; _box.Padding = new Thickness(9, 6, 9, 5);
        _box.FontSize = 13; _box.Background = new SolidColorBrush(Color.FromRgb(30, 32, 40)); _box.Foreground = Brushes.White;
        _box.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 64, 78)); _box.CaretBrush = Brushes.White;
        _box.SelectionBrush = new SolidColorBrush(Color.FromArgb(120, 120, 160, 255));
        _box.KeyDown += Box_KeyDown;
        root.Children.Add(_box);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = MakeButton(russian ? "Отмена" : "Cancel", 92, false);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = MakeButton(russian ? "Готово" : "Done", 112, true);
        ok.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        root.Children.Add(actions);

        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not TextBox) try { DragMove(); } catch (InvalidOperationException) { } };
    }

    void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        else if (e.Key == Key.Escape) { DialogResult = false; Close(); e.Handled = true; }
    }

    void Accept()
    {
        if (Value.Length == 0) { _box.Focus(); return; }
        DialogResult = true;
        Close();
    }

    static Button MakeButton(string text, double width, bool primary)
    {
        var button = new Button
        {
            Content = text, Width = width, Height = 34, BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(primary ? (byte)105 : (byte)55, 255, 255, 255)),
            Background = primary
                ? new LinearGradientBrush(Color.FromArgb(58, 255, 255, 255), Color.FromArgb(28, 255, 255, 255), 90)
                : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand
        };
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        button.Template = template;
        return button;
    }
}

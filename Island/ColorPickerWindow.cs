using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Island;

public sealed class ColorPickerWindow : Window
{
    readonly Canvas square = new() { ClipToBounds = true, Cursor = Cursors.Cross };
    readonly Border hueBar = new() { Cursor = Cursors.Hand };
    readonly Ellipse marker = new() { Width=14, Height=14, Stroke=Brushes.White, StrokeThickness=2, Fill=Brushes.Transparent, IsHitTestVisible=false };
    readonly Ellipse hueMarker = new() { Height=10, Stroke=Brushes.White, StrokeThickness=2, Fill=new SolidColorBrush(Color.FromArgb(80,0,0,0)), IsHitTestVisible=false };
    readonly TextBox hex = new();
    readonly Slider alphaSlider = new() { Minimum = 0, Maximum = 255, Height = 22, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock alphaText = new() { Width = 38, Foreground = Brushes.White, FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    byte alpha = 255;
    double h,s,v;
    bool syncing;
    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(Color initial)
    {
        SelectedColor=initial; (h,s,v)=ToHsv(initial);
        Width=430; Height=410; ResizeMode=ResizeMode.NoResize; WindowStyle=WindowStyle.None;
        AllowsTransparency=true; Background=Brushes.Transparent; WindowStartupLocation=WindowStartupLocation.CenterOwner; ShowInTaskbar=false;
        var shell=new Border { Background=new SolidColorBrush(Color.FromRgb(19,20,27)), BorderBrush=new SolidColorBrush(Color.FromArgb(65,255,255,255)), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(16), Padding=new Thickness(18), Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=24, ShadowDepth=4, Opacity=.45, Color=Colors.Black } };
        Content=shell;
        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        shell.Child=root;
        var header=new DockPanel { Margin=new Thickness(0,0,0,14) };
        var close=MakeButton("×",32,false); close.Click+=(_,_)=>{ DialogResult=false; Close(); }; DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text="CUSTOM COLOR", Foreground=Brushes.White, FontSize=13, FontWeight=FontWeights.SemiBold, VerticalAlignment=VerticalAlignment.Center });
        root.Children.Add(header);
        var picker=new Grid { Margin=new Thickness(0,0,0,14) };
        picker.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        picker.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(24) });
        Grid.SetRow(picker,1); root.Children.Add(picker);
        var squareBorder=new Border { CornerRadius=new CornerRadius(10), ClipToBounds=true, Child=square, Margin=new Thickness(0,0,12,0) };
        Grid.SetColumn(squareBorder,0); picker.Children.Add(squareBorder);
        square.Children.Add(new Rectangle { Fill=new LinearGradientBrush(Colors.White,Color.FromArgb(0,255,255,255),0), IsHitTestVisible=false });
        square.Children.Add(new Rectangle { Fill=new LinearGradientBrush(Color.FromArgb(0,0,0,0),Colors.Black,90), IsHitTestVisible=false });
        square.Children.Add(marker);
        square.SizeChanged+=(_,_)=>LayoutMarkers();
        square.MouseLeftButtonDown+=(_,e)=>{ square.CaptureMouse(); SetSquare(e.GetPosition(square)); };
        square.MouseMove+=(_,e)=>{ if(e.LeftButton==MouseButtonState.Pressed) SetSquare(e.GetPosition(square)); };
        square.MouseLeftButtonUp+=(_,_)=>square.ReleaseMouseCapture();
        hueBar.Width=24; hueBar.CornerRadius=new CornerRadius(7);
        hueBar.Background=new LinearGradientBrush { StartPoint=new Point(0,0), EndPoint=new Point(0,1), GradientStops=new GradientStopCollection { new(Colors.Red,0),new(Colors.Yellow,.1667),new(Colors.Lime,.3333),new(Colors.Cyan,.5),new(Colors.Blue,.6667),new(Colors.Magenta,.8333),new(Colors.Red,1) } };
        var hueCanvas=new Canvas { IsHitTestVisible=false }; hueBar.Child=hueCanvas; hueCanvas.Children.Add(hueMarker);
        hueBar.SizeChanged+=(_,_)=>LayoutMarkers();
        hueBar.MouseLeftButtonDown+=(_,e)=>{ hueBar.CaptureMouse(); SetHue(e.GetPosition(hueBar)); };
        hueBar.MouseMove+=(_,e)=>{ if(e.LeftButton==MouseButtonState.Pressed) SetHue(e.GetPosition(hueBar)); };
        hueBar.MouseLeftButtonUp+=(_,_)=>hueBar.ReleaseMouseCapture();
        Grid.SetColumn(hueBar,1); picker.Children.Add(hueBar);
        var hexRow=new DockPanel { Margin=new Thickness(0,0,0,8) };
        hexRow.Children.Add(new TextBlock { Text="HEX", Foreground=new SolidColorBrush(Color.FromRgb(160,165,180)), FontSize=11, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,12,0) });
        hex.Text=initial.ToString(); hex.Height=32; hex.Padding=new Thickness(9,5,9,5); hex.Background=new SolidColorBrush(Color.FromRgb(30,32,40)); hex.Foreground=Brushes.White; hex.BorderBrush=new SolidColorBrush(Color.FromRgb(60,64,78)); hex.CaretBrush=Brushes.White;
        hex.LostFocus+=(_,_)=>ReadHex(); hex.KeyDown+=(_,e)=>{ if(e.Key==Key.Enter){ReadHex();e.Handled=true;} };
        hexRow.Children.Add(hex); Grid.SetRow(hexRow,2); root.Children.Add(hexRow);
        var actions=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=MakeButton("Отмена",92,false); cancel.Margin=new Thickness(0,0,8,0); cancel.Click+=(_,_)=>{DialogResult=false;Close();};
        var apply=MakeButton("Применить",112,true); apply.Click+=(_,_)=>{ReadHex();SelectedColor=Current();DialogResult=true;Close();};
        var alphaRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true };
        alphaRow.Children.Add(new TextBlock { Text = "ALPHA", Foreground = new SolidColorBrush(Color.FromRgb(160,165,180)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) });
        alphaText.Text = "255";
        DockPanel.SetDock(alphaText, Dock.Right);
        alphaRow.Children.Add(alphaText);
        alphaSlider.Value = initial.A;
        alphaSlider.Margin = new Thickness(0,0,10,0);
        alphaSlider.ValueChanged += (_, e) => { if (!syncing) { alpha = (byte)Math.Clamp((int)Math.Round(e.NewValue), 0, 255); alphaText.Text = alpha.ToString(); UpdateView(); } };
        alphaRow.Children.Add(alphaSlider);
        Grid.SetRow(alphaRow,3); root.Children.Add(alphaRow);
        actions.Children.Add(cancel); actions.Children.Add(apply); Grid.SetRow(actions,4); root.Children.Add(actions);
        Loaded+=(_,_)=>{LayoutMarkers();UpdateView();};
    }
    Button MakeButton(string text,double width,bool primary)=>new() { Content=text, Width=width, Height=34, BorderThickness=new Thickness(1), BorderBrush=new SolidColorBrush(Color.FromArgb(75,255,255,255)), Background=primary?new SolidColorBrush(Color.FromArgb(42,255,255,255)):new SolidColorBrush(Color.FromArgb(24,255,255,255)), Foreground=Brushes.White, FontSize=12, Cursor=Cursors.Hand };
    void LayoutMarkers()
    {
        double w=square.ActualWidth,sh=square.ActualHeight;
        if(w>0&&sh>0) foreach(UIElement el in square.Children) if(el is FrameworkElement fe&&!ReferenceEquals(fe,marker)){fe.Width=w;fe.Height=sh;}
        Canvas.SetLeft(marker,s*w-marker.Width/2); Canvas.SetTop(marker,(1-v)*sh-marker.Height/2);
        if(hueBar.Child is Canvas hc&&hueBar.ActualHeight>0){hueMarker.Width=hueBar.ActualWidth;Canvas.SetLeft(hueMarker,0);Canvas.SetTop(hueMarker,h/360*hueBar.ActualHeight-hueMarker.Height/2);}
    }
    void SetSquare(Point p){s=Math.Clamp(p.X/Math.Max(1,square.ActualWidth),0,1);v=1-Math.Clamp(p.Y/Math.Max(1,square.ActualHeight),0,1);LayoutMarkers();UpdateView();}
    void SetHue(Point p){h=Math.Clamp(p.Y/Math.Max(1,hueBar.ActualHeight),0,1)*360;LayoutMarkers();UpdateView();}
    void UpdateView(){square.Background=new SolidColorBrush(Hsv(h,1,1));var c=Current();syncing=true;hex.Text=c.ToString();alphaText.Text=alpha.ToString();if((int)alphaSlider.Value!=alpha)alphaSlider.Value=alpha;syncing=false;LayoutMarkers();}
    void ReadHex(){if(syncing)return;try{var t=hex.Text.Trim();if(t.Length==6)t="#FF"+t.TrimStart('#');else if(t.Length==7)t="#FF"+t[1..];var c=(Color)ColorConverter.ConvertFromString(t);alpha=c.A;alphaSlider.Value=alpha;(h,s,v)=ToHsv(c);UpdateView();}catch{hex.Text=Current().ToString();}}
    Color Current(){var rgb=Hsv(h,s,v);return Color.FromArgb(alpha,rgb.R,rgb.G,rgb.B);}
    static Color Hsv(double h,double s,double v){h=(h%360+360)%360;double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;(double r,double g,double b)=h switch{<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));}
    static (double,double,double) ToHsv(Color c){double r=c.R/255d,g=c.G/255d,b=c.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;double h=d==0?0:max==r?60*((g-b)/d%6):max==g?60*((b-r)/d+2):60*((r-g)/d+4);if(h<0)h+=360;return(h,max==0?0:d/max,max);}
}
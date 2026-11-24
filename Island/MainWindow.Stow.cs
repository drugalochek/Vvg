using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Island;

/// <summary>
/// Hide the island without closing Moku: hold the left button and swipe toward the screen edge —
/// the island shrinks into a thin black line. Swipe the line back (or click it) to restore.
/// </summary>
public partial class MainWindow
{
    bool _stowed;
    Point? _swipeStart;
    bool _swipeTracking, _swipeHandled;
    const double StowW = 88, StowH = 8;   // 8 = minimum body height the liquid renderer draws
    static readonly SolidColorBrush StowHitArea = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    internal bool IsStowed => _stowed;

    void InitStowGesture()
    {
        PreviewMouseLeftButtonDown += Swipe_Down;
        PreviewMouseMove += Swipe_Move;
        PreviewMouseLeftButtonUp += Swipe_Up;
        LostMouseCapture += (_, _) => { if (_swipeTracking) { _swipeTracking = false; _swipeStart = null; } };
    }

    /// <summary>+1 = hide by swiping down (island at the bottom), −1 = hide by swiping up.</summary>
    int HideDirection => IslandSwipePolicy.HideDirection(_s.VerticalAlign);

    void Swipe_Down(object sender, MouseButtonEventArgs e)
    {
        _swipeHandled = false;
        if (_welcomeRunning || _state == IslandState.Expanded || _modalOpen) { _swipeStart = null; return; }
        _swipeStart = PointToScreen(e.GetPosition(this));
        _swipeTracking = false;
    }

    void Swipe_Move(object sender, MouseEventArgs e)
    {
        if (_swipeStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var now = PointToScreen(e.GetPosition(this));
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        double dy = (now.Y - start.Y) / Math.Max(0.5, dpi);
        if (!_swipeTracking && Math.Abs(dy) > IslandSwipePolicy.StartDistance)
        {
            _swipeTracking = true;
            CaptureMouse();          // a swipe started on a media button must not also click it
        }
        if (!_swipeTracking) return;
        switch (IslandSwipePolicy.Resolve(dy, HideDirection, _stowed))
        {
            case SwipeAction.Stow: StowIsland(); Finish(); break;
            case SwipeAction.Restore: RestoreIsland(); Finish(); break;
        }
        void Finish() { _swipeHandled = true; _swipeStart = null; }
    }

    void Swipe_Up(object sender, MouseButtonEventArgs e)
    {
        bool tracked = _swipeTracking || _swipeHandled;
        _swipeTracking = false; _swipeStart = null; _swipeHandled = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (tracked) { e.Handled = true; return; }           // no Pill click / expand after a swipe
        if (_stowed && _state != IslandState.Expanded)
        {
            RestoreIsland();                                  // a plain click on the line also brings it back
            e.Handled = true;
        }
    }

    void StowIsland()
    {
        if (_stowed || _state == IslandState.Expanded) return;
        _stowed = true;
        _toastTimer.Stop();
        if (_state == IslandState.Toast)
        {
            _state = IslandState.Collapsed;
            ToastPanel.Visibility = Visibility.Collapsed;
            TimePanel.Visibility = Visibility.Visible;
        }
        Mini.IsHitTestVisible = false;
        FadeTo(Mini, 0, 120);
        // Alpha 1 keeps the small window around the line clickable; it is invisible.
        Background = StowHitArea;
        Pill.ToolTip = IslandSwipePolicy.RestoreHint(_s.Language == "ru", HideDirection);
        Animate(StowW, StowH, StowH / 2, spring: false, speed: _s.ExpandSpeed / 100.0);
        EnsureFx();
    }

    void RestoreIsland(bool animate = true)
    {
        if (!_stowed) return;
        _stowed = false;
        Background = System.Windows.Media.Brushes.Transparent;
        Pill.ToolTip = null;
        Mini.IsHitTestVisible = true;
        FadeTo(Mini, 1, 220);
        if (animate && _state != IslandState.Expanded)
        {
            UpdateMini();
            Animate(_now != null ? PlayW : IdleW, MiniH, MiniR, spring: true, speed: _s.ExpandSpeed / 100.0);
        }
        EnsureFx();
    }
}

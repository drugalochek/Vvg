using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace Island;

/// <summary>
/// v2.8: обложка трека, оценка темпа музыки (для «танца» глаз), отложенное сохранение настроек
/// и кнопки/колёсико у полей сдвига.
/// </summary>
public partial class MainWindow
{
    // ------------------------------------------------------------------ сохранение настроек

    DispatcherTimer? _saveTimer;

    /// <summary>Сохранить настройки чуть позже: при движении ползунка файл не перезаписывается 60 раз в секунду.</summary>
    void SaveSoon()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) => { _saveTimer!.Stop(); _s.Save(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void FlushSave()
    {
        if (_saveTimer?.IsEnabled == true) { _saveTimer.Stop(); _s.Save(); }
    }

    // ------------------------------------------------------------------ поля сдвига X / Y

    static int StepSize() => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;

    void OffsetX_Minus(object sender, RoutedEventArgs e) => SetOffsetX(_s.OffsetX - StepSize());
    void OffsetX_Plus(object sender, RoutedEventArgs e) => SetOffsetX(_s.OffsetX + StepSize());
    void OffsetY_Minus(object sender, RoutedEventArgs e) => SetOffset(_s.OffsetY - StepSize());
    void OffsetY_Plus(object sender, RoutedEventArgs e) => SetOffset(_s.OffsetY + StepSize());

    /// <summary>Колёсико над полем меняет число (с Shift — по 10).</summary>
    void OffsetInput_Wheel(object sender, MouseWheelEventArgs e)
    {
        int d = (e.Delta > 0 ? 1 : -1) * StepSize();
        if (ReferenceEquals(sender, OffsetXInput)) SetOffsetX(_s.OffsetX + d);
        else SetOffset(_s.OffsetY + d);
        if (sender is TextBox tb) tb.CaretIndex = tb.Text.Length;
        e.Handled = true;
    }

    // ------------------------------------------------------------------ обложка трека

    byte[]? _artShown;
    bool _artVisible;

    void UpdateArt(byte[]? art)
    {
        if (ReferenceEquals(art, _artShown)) return;
        _artShown = art;
        ImageSource? img = null;
        if (art != null)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = 160;            // хватает и для 48 px на 200 % масштабе, а памяти — копейки
                bi.StreamSource = new MemoryStream(art);
                bi.EndInit();
                bi.Freeze();
                img = bi;
            }
            catch (Exception ex)
            {
                Log.Warn("Обложка не распознана: " + ex.Message);
            }
        }

        _artVisible = img != null;
        if (img != null)
        {
            var brush = new ImageBrush(img) { Stretch = Stretch.UniformToFill };   // YouTube 16:9 — обрежется по центру
            brush.Freeze();
            ArtImage.Background = brush;
            MiniArt.Background = brush;
            MiniArt.Visibility = Visibility.Visible;
            NoteIcon.Visibility = Visibility.Collapsed;
            ArtPlaceholder.Visibility = Visibility.Collapsed;
            ArtImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            var pop = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(520))
            { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut } };
            ArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            ArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        else
        {
            ArtImage.BeginAnimation(OpacityProperty, null);
            ArtImage.Opacity = 0;
            ArtImage.Background = null;
            MiniArt.Background = null;
            MiniArt.Visibility = Visibility.Collapsed;
            NoteIcon.Visibility = Visibility.Visible;
            ArtPlaceholder.Visibility = Visibility.Visible;
        }
    }

    // ------------------------------------------------------------------ темп музыки

    double _tempoClock, _lastBeatTime = -10, _beatAnchor;
    double _beatPeriod = 0.5;                 // секунды на удар (0,5 = 120 BPM)
    double _tempoConf;                        // насколько уверены в темпе: 0..1
    double _danceEnergy;                      // «раскачка» танца: растёт, пока удары идут ровно
    double _nextBpmUiAt;

    double Bpm => 60.0 / _beatPeriod;

    /// <summary>Позиция в долях удара от «якоря»: целая часть — номер удара, дробная — фаза.</summary>
    double BeatPosition => (_tempoClock - _beatAnchor) / _beatPeriod;

    /// <summary>
    /// Оценка темпа по ударам бочки: интервалы приводятся к 70–180 BPM (удвоение/деление пополам),
    /// сглаживаются и отсекают случайные удары. Фаза подтягивается к каждому удару, поэтому танец
    /// идёт в такт, даже если анализатор пропустил пару ударов.
    /// </summary>
    void TrackTempo(double dt, bool playing, bool beat)
    {
        _tempoClock += dt;
        if (!playing)
        {
            _danceEnergy = Math.Max(0, _danceEnergy - dt * 1.4);
            _tempoConf = Math.Max(0, _tempoConf - dt * 0.2);
            UpdateBpmBadge(false);
            return;
        }

        if (beat)
        {
            double iv = _tempoClock - _lastBeatTime;
            _lastBeatTime = _tempoClock;
            if (iv > 0.2 && iv < 2.2)
            {
                while (iv < 0.333) iv *= 2;
                while (iv > 0.857) iv /= 2;
                double ratio = iv / _beatPeriod;
                bool fits = ratio > 0.86 && ratio < 1.16;
                double w = _tempoConf < 0.35 ? 0.45 : fits ? 0.16 : 0.03;
                _beatPeriod = Math.Clamp(_beatPeriod + (iv - _beatPeriod) * w, 0.333, 0.857);
                _tempoConf = Math.Clamp(_tempoConf + (fits ? 0.14 : -0.12), 0, 1);
            }
            double pos = BeatPosition;
            double err = pos - Math.Round(pos);                 // насколько удар разошёлся с ожидаемым
            _beatAnchor += err * _beatPeriod * 0.55;
            _danceEnergy = Math.Min(1, _danceEnergy + 0.22);
        }
        else if (_tempoClock - _lastBeatTime > 1.6)
            _danceEnergy = Math.Max(0, _danceEnergy - dt * 0.7);   // ударов нет (тихое место) — затихает

        UpdateBpmBadge(true);
    }

    void UpdateBpmBadge(bool playing)
    {
        if (_tempoClock < _nextBpmUiAt || BpmBadge == null) return;
        _nextBpmUiAt = _tempoClock + 0.5;
        bool show = playing && _tempoConf > 0.45 && _danceEnergy > 0.2;
        if (show) BpmText.Text = $"{Bpm:0} BPM";
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        if (BpmBadge.Visibility != vis) BpmBadge.Visibility = vis;
    }

    // ------------------------------------------------------------------ мелочи для эмоций

    /// <summary>Подмигнуть правым глазом.</summary>
    void Wink()
    {
        if (_welcomeRunning || !_s.EyeEmotions || _eyeR == null) return;
        StartBlink(_eyeR, 0.12, 0.2);
        EnsureFx();
    }

    /// <summary>Лёгкий «желейный» толчок всего острова (например, когда глаза гладят).</summary>
    void JellyPoke(double x, double y)
    {
        if (_state != IslandState.Collapsed || _s.JellyStrength <= 0) return;
        _jx.Velocity += x;
        _jy.Velocity += y;
        _islandMoving = true;
        EnsureFx();
    }
}

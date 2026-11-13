using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Panel = System.Windows.Controls.Panel;

namespace Island;

/// <summary>
/// v2.11: настройки разложены по разделам. Вкладка «Настройки» открывается меню из карточек
/// (цвета, положение, глаза, анимации, свёрнутый вид, музыка, приложения, общие) и строкой поиска;
/// клик по карточке открывает только настройки этого раздела, «Назад» / Esc / боковая кнопка мыши — обратно.
/// </summary>
public partial class MainWindow
{
    sealed record SettingEntry(Func<string> Label, string Cat, FrameworkElement Element, bool Static);
    sealed record SettingsCategory(string Id, string Glyph, Color Tint, string Ru, string En, string SubRu, string SubEn);

    static readonly SettingsCategory[] SettingsCats =
    {
        new("look", "\uE790", Color.FromRgb(0xFF, 0x8F, 0xAB), "Цвета и тема", "Colors & theme",
            "Тема, градиент, акцент, стекло", "Theme, gradient, accent, glass"),
        new("place", "\uE740", Color.FromRgb(0x7F, 0xB8, 0xFF), "Положение и размер", "Position & size",
            "Где стоит остров, размер, углы", "Placement, size, corners"),
        new("eyes", "\uE890", Color.FromRgb(0x8C, 0xFF, 0x9E), "Глаза", "Eyes",
            "Форма, цвет, взгляд, эмоции, танец", "Shape, color, gaze, mood, dance"),
        new("anim", "\uE945", Color.FromRgb(0xFF, 0xD1, 0x66), "Анимации", "Animations",
            "Стиль появления, желе, скорость", "Appear style, jelly, speed"),
        new("mini", "\uE823", Color.FromRgb(0x7F, 0xE3, 0xFF), "Свёрнутый вид", "Compact view",
            "Часы, виджеты, таймер", "Clock, widgets, timer"),
        new("music", "\uE8D6", Color.FromRgb(0xC3, 0xA6, 0xFF), "Музыка и уведомления", "Music & notifications",
            "Волна, всплывающие уведомления", "Wave, pop-up notifications"),
        new("apps", "\uE71D", Color.FromRgb(0xFF, 0x9F, 0x43), "Приложения", "Apps",
            "Размер плиток, папки, иконки", "Tile size, folders, icons"),
        new("home", "\uE80F", Color.FromRgb(0x7F, 0xE3, 0xFF), "Главная", "Home",
            "Буфер обмена или запущенные окна", "Clipboard or running windows"),
        new("general", "\uE713", Color.FromRgb(0xB8, 0xC2, 0xD6), "Общие", "General",
            "Язык, клавиша, автозапуск, игры", "Language, shortcut, autostart, games"),
    };

    readonly List<SettingEntry> _settingsIndex = new();
    string _buildCat = "";          // раздел, в который сейчас строятся строки (для поиска)
    string _settingsCat = "";       // открытый раздел; "" — меню
    bool _settingsNavReady;
    DispatcherTimer? _resetArm;     // «Точно сбросить?» — второй клик в течение 3 с

    bool Ru => _s.Language == "ru";

    Panel? CategoryPanel(string id) => id switch
    {
        "look" => CatLook, "place" => CatPlace, "eyes" => CatEyes, "anim" => CatAnim, "mini" => CatMini,
        "music" => CatMusic, "apps" => CatApps, "home" => CatHome, "general" => CatGeneral, _ => null
    };

    /// <summary>Однократная подготовка: строки из разметки попадают в поиск, боковая кнопка мыши — «Назад».</summary>
    void EnsureSettingsNav()
    {
        if (_settingsNavReady || SettingsPage == null) return;
        _settingsNavReady = true;

        void Add(string cat, TextBlock? label)
        {
            if (label?.Parent is FrameworkElement row)
                _settingsIndex.Add(new SettingEntry(() => label.Text, cat, row, true));
        }
        Add("look", ThemeLabel); Add("look", GradientColorsLabel); Add("look", GradientBalanceLabel);
        Add("look", AccentLabel); Add("look", GlassOpacityLabel);
        Add("place", PositionLabel); Add("place", VerticalPositionLabel); Add("place", HorizontalOffsetLabel);
        Add("place", TopOffsetLabel); Add("place", IslandSizeLabel); Add("place", CornerRadiusLabel);
        Add("general", LanguageLabel); Add("general", FullscreenOpacityLabel);
        if (AutoBtn != null) _settingsIndex.Add(new SettingEntry(() => Ru ? "Запуск при старте Windows" : "Start with Windows", "general", AutoBtn, true));
        if (SupportBtn != null) _settingsIndex.Add(new SettingEntry(() => SupportBtn.Content?.ToString() ?? "", "general", SupportBtn, true));

        SettingsPage.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1 && _settingsCat.Length > 0) { ShowSettingsMenu(); e.Handled = true; }
        };
    }

    /// <summary>Перестроить карточки и подписи (после смены языка или пересборки разделов).</summary>
    void RefreshSettingsNav()
    {
        if (SettingsCards == null) return;
        EnsureSettingsNav();
        bool ru = Ru;
        SettingsCards.Children.Clear();
        foreach (var c in SettingsCats)
            SettingsCards.Children.Add(MakeCategoryCard(c, ru));
        SettingsSearchHint.Text = ru ? "Найти настройку…" : "Find a setting…";
        SettingsBackBtn.ToolTip = ru ? "Назад (Esc)" : "Back (Esc)";
        DisarmReset();
        if (_settingsCat.Length > 0) FillDetailHeader(_settingsCat);
        if (SettingsSearch.Text.Trim().Length > 0) RunSettingsSearch();
    }

    // ------------------------------------------------------------------ меню

    FrameworkElement MakeCategoryCard(SettingsCategory c, bool ru)
    {
        var icons = (FontFamily)FindResource("Icons");
        var overlay = new Border { CornerRadius = new CornerRadius(14), Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        var badge = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x30, c.Tint.R, c.Tint.G, c.Tint.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, c.Tint.R, c.Tint.G, c.Tint.B)), BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = c.Glyph, FontFamily = icons, FontSize = 14, Foreground = new SolidColorBrush(c.Tint),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = ru ? c.Ru : c.En, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = ru ? c.SubRu : c.SubEn, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0)
        });
        var chevron = new TextBlock
        {
            Text = "\uE76C", FontFamily = icons, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
        };
        var content = new Grid { Margin = new Thickness(11, 0, 12, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1); Grid.SetColumn(chevron, 2);
        content.Children.Add(badge); content.Children.Add(texts); content.Children.Add(chevron);

        var card = MakePressable(overlay, content, 50, () => OpenSettingsCategory(c.Id));
        card.ToolTip = ru ? c.SubRu : c.SubEn;
        return card;
    }

    /// <summary>Карточка-кнопка: подсветка при наведении, лёгкое «вдавливание» при нажатии.</summary>
    Grid MakePressable(Border overlay, FrameworkElement content, double height, Action click)
    {
        var scale = new ScaleTransform(1, 1);
        var card = new Grid
        {
            Height = height, Margin = new Thickness(3), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale
        };
        card.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14), Background = (Brush)FindResource("Card"),
            BorderBrush = (Brush)FindResource("CardRim"), BorderThickness = new Thickness(1)
        });
        card.Children.Add(overlay);
        card.Children.Add(content);

        void Fade(double to, int ms) => overlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        void Scale(double to, int ms, IEasingFunction ease)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        bool pressed = false;
        card.MouseEnter += (_, _) => Fade(0.10, 160);
        card.MouseLeave += (_, _) => { Fade(0, 220); if (pressed) { pressed = false; Scale(1, 260, new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }); } };
        card.MouseLeftButtonDown += (_, e) => { pressed = true; Fade(0.18, 80); Scale(0.97, 90, new CubicEase { EasingMode = EasingMode.EaseOut }); e.Handled = true; };
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            Fade(0.10, 160);
            Scale(1, 300, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });
            e.Handled = true;
            click();
        };
        return card;
    }

    /// <summary>Показать меню разделов.</summary>
    void ShowSettingsMenu(bool animate = true)
    {
        if (SettingsMenu == null) return;
        _settingsCat = "";
        DisarmReset();
        SettingsDetail.Visibility = Visibility.Collapsed;
        SettingsMenu.Visibility = Visibility.Visible;
        SettingsPage.ScrollToTop();
        if (!animate) { SettingsSearch.Text = ""; return; }
        SlideIn(SettingsMenu, -24);
        // карточки появляются «волной»
        int i = 0;
        foreach (UIElement card in SettingsCards.Children)
        {
            card.Opacity = 0;
            var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(18 * i++), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var el = card;
            a.Completed += (_, _) => { el.BeginAnimation(OpacityProperty, null); el.Opacity = 1; };
            card.BeginAnimation(OpacityProperty, a);
        }
    }

    /// <summary>Открыть раздел; focus — строка, к которой прокрутить и подсветить (из поиска).</summary>
    void OpenSettingsCategory(string id, FrameworkElement? focus = null)
    {
        var panel = CategoryPanel(id);
        if (panel == null) return;
        _settingsCat = id;
        DisarmReset();
        foreach (var c in SettingsCats)
            if (CategoryPanel(c.Id) is Panel other) other.Visibility = c.Id == id ? Visibility.Visible : Visibility.Collapsed;
        FillDetailHeader(id);
        SettingsMenu.Visibility = Visibility.Collapsed;
        SettingsDetail.Visibility = Visibility.Visible;
        if (SettingsSearch.IsKeyboardFocusWithin) Keyboard.ClearFocus();
        SettingsPage.ScrollToTop();
        SlideIn(SettingsDetail, 24);
        if (id == "general") UpdateAutoButton();
        if (focus != null)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                focus.BringIntoView(new Rect(0, -30, focus.ActualWidth, focus.ActualHeight + 60));
                FlashSetting(focus);
            }));
    }

    void FillDetailHeader(string id)
    {
        var c = SettingsCats.FirstOrDefault(x => x.Id == id);
        if (c == null) return;
        bool ru = Ru;
        SettingsDetailTitle.Text = ru ? c.Ru : c.En;
        SettingsDetailSub.Text = ru ? c.SubRu : c.SubEn;
        SettingsDetailIcon.Text = c.Glyph;
        SettingsDetailIcon.Foreground = new SolidColorBrush(c.Tint);
        SettingsDetailBadge.Background = new SolidColorBrush(Color.FromArgb(0x30, c.Tint.R, c.Tint.G, c.Tint.B));
        SettingsDetailBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, c.Tint.R, c.Tint.G, c.Tint.B));
        SettingsDetailBadge.BorderThickness = new Thickness(1);
        SettingsResetBtn.Content = new TextBlock { Text = ru ? "Сбросить" : "Reset", FontSize = 11, Margin = new Thickness(12, 0, 12, 0) };
        SettingsResetBtn.ToolTip = ru ? "Вернуть настройки этого раздела по умолчанию" : "Restore this section's defaults";
    }

    static void SlideIn(FrameworkElement el, double fromX)
    {
        var shift = new TranslateTransform(fromX, 0);
        el.RenderTransform = shift;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var move = new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        fade.Completed += (_, _) => { el.BeginAnimation(OpacityProperty, null); el.Opacity = 1; };
        shift.BeginAnimation(TranslateTransform.XProperty, move);
        el.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Мягкая подсветка найденной строки.</summary>
    void FlashSetting(FrameworkElement el)
    {
        var accent = _accentColor;
        if (el is Panel panel)
        {
            var old = panel.Background;
            var brush = new SolidColorBrush(Color.FromArgb(0, accent.R, accent.G, accent.B));
            panel.Background = brush;
            var a = new ColorAnimation(Color.FromArgb(0x48, accent.R, accent.G, accent.B), TimeSpan.FromMilliseconds(260))
            {
                AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new SineEase()
            };
            a.Completed += (_, _) => panel.Background = old;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }
        else
        {
            var a = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(220)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) };
            a.Completed += (_, _) => { el.BeginAnimation(OpacityProperty, null); el.Opacity = 1; };
            el.BeginAnimation(OpacityProperty, a);
        }
    }

    void SettingsBack_Click(object sender, RoutedEventArgs e) => ShowSettingsMenu();

    /// <summary>Сброс раздела: первый клик — «Точно сбросить?», второй (в течение 3 с) — сброс.</summary>
    void SettingsReset_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsCat.Length == 0) return;
        if (_resetArm == null || !_resetArm.IsEnabled)
        {
            _resetArm ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _resetArm.Tick -= ResetArm_Tick; _resetArm.Tick += ResetArm_Tick;
            _resetArm.Start();
            SettingsResetBtn.Content = new TextBlock
            {
                Text = Ru ? "Точно сбросить?" : "Reset for sure?", FontSize = 11, Margin = new Thickness(12, 0, 12, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xB4))
            };
            return;
        }
        string cat = _settingsCat;
        DisarmReset();
        ResetSettingsSection(cat);
        ShowToast("Moku", Ru ? "Раздел сброшен" : "Section reset", ColorOk);
    }

    void ResetArm_Tick(object? sender, EventArgs e) => DisarmReset();

    void DisarmReset()
    {
        _resetArm?.Stop();
        if (SettingsResetBtn != null && _settingsCat.Length > 0)
            SettingsResetBtn.Content = new TextBlock { Text = Ru ? "Сбросить" : "Reset", FontSize = 11, Margin = new Thickness(12, 0, 12, 0) };
    }

    /// <summary>Esc / «Назад» внутри настроек: из раздела — в меню, из поиска — очистить. true — обработано.</summary>
    bool SettingsHandleEscape()
    {
        if (SettingsPage == null || SettingsPage.Visibility != Visibility.Visible) return false;
        if (_settingsCat.Length > 0) { ShowSettingsMenu(); return true; }
        if (SettingsSearch.Text.Length > 0) { SettingsSearch.Text = ""; return true; }
        return false;
    }

    // ------------------------------------------------------------------ поиск

    void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool empty = SettingsSearch.Text.Length == 0;
        SettingsSearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SettingsSearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        RunSettingsSearch();
    }

    void SettingsSearchClear_Click(object sender, RoutedEventArgs e)
    {
        SettingsSearch.Text = "";
        SettingsSearch.Focus();
    }

    void SettingsSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsSearch.Text.Length > 0) { SettingsSearch.Text = ""; e.Handled = true; }
        else if (e.Key == Key.Enter && _firstSearchHit != null) { _firstSearchHit(); e.Handled = true; }
    }

    Action? _firstSearchHit;

    void RunSettingsSearch()
    {
        if (SettingsResults == null) return;
        string q = SettingsSearch.Text.Trim();
        _firstSearchHit = null;
        SettingsResults.Children.Clear();
        if (q.Length == 0)
        {
            SettingsResults.Visibility = Visibility.Collapsed;
            SettingsCards.Visibility = Visibility.Visible;
            return;
        }
        SettingsCards.Visibility = Visibility.Collapsed;
        SettingsResults.Visibility = Visibility.Visible;
        bool ru = Ru;
        bool Match(string text) => text.Contains(q, StringComparison.CurrentCultureIgnoreCase);

        int shown = 0;
        foreach (var c in SettingsCats.Where(c => Match(ru ? c.Ru : c.En) || Match(ru ? c.SubRu : c.SubEn)))
        {
            string id = c.Id;
            AddResult(c, ru ? c.Ru : c.En, ru ? "раздел" : "section", () => OpenSettingsCategory(id));
            shown++;
        }
        foreach (var entry in _settingsIndex)
        {
            if (shown >= 14) break;
            string label = entry.Label().Replace("\n", " ");
            if (label.Length == 0 || !Match(label)) continue;
            var c = SettingsCats.First(x => x.Id == entry.Cat);
            var target = entry;
            AddResult(c, label, ru ? c.Ru : c.En, () => OpenSettingsCategory(target.Cat, target.Element));
            shown++;
        }
        if (shown == 0)
            SettingsResults.Children.Add(new TextBlock
            {
                Text = ru ? "Ничего не нашлось — попробуй другое слово." : "Nothing found — try another word.",
                Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 12, Margin = new Thickness(4, 8, 0, 0)
            });
    }

    void AddResult(SettingsCategory c, string title, string where, Action open)
    {
        _firstSearchHit ??= open;
        var icons = (FontFamily)FindResource("Icons");
        var content = new Grid { Margin = new Thickness(10, 0, 12, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x30, c.Tint.R, c.Tint.G, c.Tint.B)),
            Child = new TextBlock
            {
                Text = c.Glyph, FontFamily = icons, FontSize = 11, Foreground = new SolidColorBrush(c.Tint),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        });
        var name = new TextBlock
        {
            Text = title, Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);
        content.Children.Add(name);
        var place = new TextBlock
        {
            Text = where, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(place, 2);
        content.Children.Add(place);
        var overlay = new Border { CornerRadius = new CornerRadius(14), Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        SettingsResults.Children.Add(MakePressable(overlay, content, 40, open));
    }
}

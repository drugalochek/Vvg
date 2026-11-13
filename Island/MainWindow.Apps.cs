using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Island;

/// <summary>
/// v2.9: плитки приложений — настоящие иконки Steam-игр, крестик удаления, перестановка перетаскиванием;
/// плавное появление/исчезновение в играх и общий помощник перевода уведомлений.
/// </summary>
public partial class MainWindow
{
    /// <summary>Строка на языке интерфейса.</summary>
    string Tr(string ru, string en) => _s.Language == "ru" ? ru : en;

    // ------------------------------------------------------------------ плавная прозрачность в играх

    void FadeWindowOpacity(double target, double ms = 420)
    {
        target = Math.Clamp(target, 0, 1);
        var anim = new DoubleAnimation(Opacity, target, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        anim.Completed += (_, _) =>
        {
            // Снимаем анимацию и фиксируем значение, чтобы дальнейшие изменения не «застревали».
            BeginAnimation(OpacityProperty, null);
            Opacity = target;
        };
        BeginAnimation(OpacityProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    // ------------------------------------------------------------------ плитки приложений

    const double AppTileW = 112, AppTileH = 88;
    string? _dragAppPath;
    Point _dragStart;
    bool _dragArmed, _suppressAppClick;
    FrameworkElement? _dragTile;

    void BuildApps()
    {
        if (AppsGrid == null) return;
        AppsGrid.Children.Clear();
        if (!AppsGrid.AllowDrop)
        {
            AppsGrid.AllowDrop = true;
            AppsGrid.Background ??= Brushes.Transparent;
            AppsGrid.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent("MokuAppPath") ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
        }
        BuildSteamCandidates();
        foreach (var app in _s.Apps.ToList())
        {
            bool steamLink = app.Path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);
            if (!steamLink && !File.Exists(app.Path)) continue;
            AppsGrid.Children.Add(CreateAppTile(app, steamLink));
        }

        if (AppsGrid.Children.Count == 0)
        {
            AppsGrid.Children.Add(new TextBlock
            {
                Text = Tr("Пока пусто. Нажми «Добавить», чтобы выбрать программу.",
                          "No apps yet. Select Add app to get started."),
                Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#88FFFFFF")!,
                FontSize = 12, Margin = new Thickness(8, 12, 8, 8),
                TextWrapping = TextWrapping.Wrap, MaxWidth = 440
            });
        }
    }

    FrameworkElement CreateAppTile(AppShortcut app, bool steamLink)
    {
        var path = app.Path;
        var tile = new Grid
        {
            Width = AppTileW, Height = AppTileH, Margin = new Thickness(4), Tag = path,
            AllowDrop = true, RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1), Background = Brushes.Transparent
        };

        var button = new Button
        {
            Style = (Style)FindResource("TileBtn"), Width = AppTileW, Height = AppTileH, Margin = new Thickness(0),
            ToolTip = steamLink ? app.Name : app.Path
        };
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        ImageSource? iconSource = null;
        try
        {
            if (steamLink) iconSource = LoadSteamArtwork(path.Split('/').LastOrDefault() ?? "");
            else
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(48, 48));
                    source.Freeze();
                    iconSource = source;
                }
            }
        }
        catch (Exception ex) { Log.Warn("Не удалось получить иконку " + path + ": " + ex.Message); }

        if (iconSource != null)
        {
            var img = new System.Windows.Controls.Image
            {
                Source = iconSource, Width = 32, Height = 32, Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Center,
                Clip = new RectangleGeometry(new Rect(0, 0, 32, 32), 7, 7)
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            content.Children.Add(img);
        }
        else content.Children.Add(new TextBlock
        {
            Text = steamLink ? "\uE7FC" : "\uE8A5", FontFamily = (FontFamily)FindResource("Icons"),
            FontSize = 25, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 7)
        });
        content.Children.Add(new TextBlock
        {
            Text = app.Name, FontSize = 11, Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 98,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) =>
        {
            if (_suppressAppClick) { _suppressAppClick = false; return; }
            LaunchApp(path);
        };

        // ПКМ-меню оставлено как запасной путь.
        var menu = new ContextMenu();
        var remove = new MenuItem { Header = Tr("Удалить из списка", "Remove from list") };
        remove.Click += (_, _) => RemoveAppAnimated(tile, path);
        menu.Items.Add(remove);
        button.ContextMenu = menu;
        tile.Children.Add(button);

        // Крестик в углу: появляется при наведении на плитку.
        var close = CreateRemoveButton();
        close.Click += (_, _) => RemoveAppAnimated(tile, path);
        tile.Children.Add(close);
        tile.MouseEnter += (_, _) => FadeTo(close, 1, 160);
        tile.MouseLeave += (_, _) => FadeTo(close, 0, 220);

        // Перестановка перетаскиванием.
        tile.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInside(e.OriginalSource as DependencyObject, close)) return;
            _dragArmed = true; _dragStart = e.GetPosition(AppsGrid); _dragAppPath = path;
        };
        tile.PreviewMouseLeftButtonUp += (_, _) => _dragArmed = false;
        tile.PreviewMouseMove += (_, e) => AppTile_MouseMove(tile, e);
        tile.DragEnter += AppTile_DragOver;
        tile.DragOver += AppTile_DragOver;
        tile.Drop += (_, e) => { e.Handled = true; };
        return tile;
    }

    Button CreateRemoveButton()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "bg" };
        border.SetValue(Border.WidthProperty, 20.0);
        border.SetValue(Border.HeightProperty, 20.0);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x2A, 0x2E)));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetValue(TextBlock.TextProperty, "\uE711");
        glyph.SetValue(TextBlock.FontFamilyProperty, (FontFamily)FindResource("Icons"));
        glyph.SetValue(TextBlock.FontSizeProperty, 8.5);
        glyph.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        glyph.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        glyph.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(glyph);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), "bg"));
        template.Triggers.Add(hover);

        return new Button
        {
            Template = template, Width = 20, Height = 20, Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 5, 0), Opacity = 0, Focusable = false,
            ToolTip = Tr("Убрать из списка", "Remove")
        };
    }

    static void FadeTo(UIElement el, double to, double ms)
    {
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    static bool IsInside(DependencyObject? d, DependencyObject target)
    {
        while (d != null)
        {
            if (ReferenceEquals(d, target)) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    void RemoveAppAnimated(FrameworkElement tile, string path)
    {
        tile.IsHitTestVisible = false;
        var dur = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        if (tile.RenderTransform is ScaleTransform st && !st.IsFrozen)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, dur) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, dur) { EasingFunction = ease });
        }
        var fade = new DoubleAnimation(0, dur) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            _s.Apps.RemoveAll(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
            _s.Save();
            BuildApps();
            ShowToast("Moku", Tr("Приложение убрано из списка", "App removed"));
        };
        tile.BeginAnimation(OpacityProperty, fade);
    }

    void AppTile_MouseMove(FrameworkElement tile, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || _dragAppPath == null) return;
        var p = e.GetPosition(AppsGrid);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragArmed = false;
        _dragTile = tile;
        _suppressAppClick = true;
        tile.BeginAnimation(OpacityProperty, null);
        tile.Opacity = 0.45;
        try
        {
            DragDrop.DoDragDrop(tile, new DataObject("MokuAppPath", _dragAppPath), DragDropEffects.Move);
        }
        catch (Exception ex) { Log.Warn("Перетаскивание плитки: " + ex.Message); }
        finally
        {
            FadeTo(tile, 1, 180);
            _dragTile = null;
            _dragAppPath = null;
            SaveAppOrder();
            // Клик после отпускания уже не придёт — снимаем блокировку чуть позже.
            Dispatcher.BeginInvoke(new Action(() => _suppressAppClick = false),
                System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    void AppTile_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent("MokuAppPath") || _dragTile == null || sender is not FrameworkElement target)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        if (ReferenceEquals(target, _dragTile)) return;
        int from = AppsGrid.Children.IndexOf(_dragTile), to = AppsGrid.Children.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        // Плитки одного размера, поэтому после перестановки под курсором оказывается сама
        // перетаскиваемая плитка — «дрожания» туда-обратно нет.
        AppsGrid.Children.RemoveAt(from);
        AppsGrid.Children.Insert(to, _dragTile);
        Nudge(target, from < to ? -1 : 1);
    }

    static void Nudge(FrameworkElement el, int dir)
    {
        var tt = new TranslateTransform(dir * 30, 0);
        var group = new TransformGroup();
        if (el.RenderTransform is ScaleTransform st) group.Children.Add(st);
        group.Children.Add(tt);
        el.RenderTransform = group;
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
        anim.Completed += (_, _) => { if (group.Children.Count > 0 && group.Children[0] is ScaleTransform s) el.RenderTransform = s; };
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    void SaveAppOrder()
    {
        var order = AppsGrid.Children.OfType<FrameworkElement>().Select(c => c.Tag as string).Where(t => t != null).ToList();
        if (order.Count == 0) return;
        var reordered = new List<AppShortcut>();
        foreach (var p in order)
        {
            var a = _s.Apps.FirstOrDefault(x => string.Equals(x.Path, p, StringComparison.OrdinalIgnoreCase));
            if (a != null && !reordered.Contains(a)) reordered.Add(a);
        }
        // Пропавшие с диска приложения (не показаны) остаются в конце списка.
        reordered.AddRange(_s.Apps.Where(a => !reordered.Contains(a)));
        if (reordered.SequenceEqual(_s.Apps)) return;
        _s.Apps = reordered;
        _s.Save();
    }

    // ------------------------------------------------------------------ иконки Steam

    static readonly Dictionary<string, ImageSource?> _steamArtCache = new();
    static List<string>? _steamRoots, _steamLibraries;
    static Dictionary<string, (string? ClientIcon, string? Icon)>? _steamAppInfo;
    static Dictionary<string, string>? _steamShortcutIcons;

    static readonly object _steamLock = new();

    static void ResetSteamArtCache()
    {
        lock (_steamLock)
        {
            _steamArtCache.Clear();
            _steamRoots = null; _steamLibraries = null; _steamAppInfo = null; _steamShortcutIcons = null;
        }
    }

    /// <summary>appinfo.vdf может весить десятки мегабайт — читаем его заранее в фоне.</summary>
    static void PrewarmSteamArt() => Task.Run(() =>
    {
        try { EnsureSteamInfo(); } catch (Exception ex) { Log.Warn("Steam prewarm: " + ex.Message); }
    });

    static void EnsureSteamInfo()
    {
        lock (_steamLock)
        {
            EnsureSteamRoots();
            _steamAppInfo ??= ReadSteamAppInfo();
            _steamShortcutIcons ??= ReadSteamShortcutIcons();
        }
    }

    static void EnsureSteamRoots()
    {
        if (_steamRoots != null && _steamLibraries != null) return;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                p = Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')).Replace('/', '\\');
                if (Directory.Exists(p)) roots.Add(Path.GetFullPath(p));
            }
            catch { }
        }
        Add(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
        Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
        Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string);
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        var libraries = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            try
            {
                var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    var lib = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(lib)) libraries.Add(lib);
                }
            }
            catch { }
        }
        _steamRoots = roots.ToList();
        _steamLibraries = libraries.ToList();
    }

    /// <summary>
    /// Читает из appcache/appinfo.vdf хэши иконок игр: clienticon (.ico, как у ярлыка на рабочем столе)
    /// и icon (маленькая .jpg). Поддерживаются форматы v27–v29 (в v29 ключи вынесены в таблицу строк).
    /// </summary>
    static Dictionary<string, (string? ClientIcon, string? Icon)> ReadSteamAppInfo()
    {
        var result = new Dictionary<string, (string?, string?)>();
        EnsureSteamRoots();
        foreach (var root in _steamRoots!)
        {
            var file = Path.Combine(root, "appcache", "appinfo.vdf");
            try
            {
                if (!File.Exists(file)) continue;
                var data = File.ReadAllBytes(file);
                if (data.Length < 16) continue;
                uint magic = BitConverter.ToUInt32(data, 0);
                int version = (int)(magic & 0xFF);
                if ((magic >> 8) != 0x075644 || version < 0x27 || version > 0x29) continue;
                int pos = 8;
                byte[]? clientKey = null, iconKey = null;
                if (version >= 0x29)
                {
                    long tableOffset = BitConverter.ToInt64(data, 8);
                    pos = 16;
                    if (tableOffset > 0 && tableOffset < data.Length - 4)
                    {
                        int count = BitConverter.ToInt32(data, (int)tableOffset);
                        int sp = (int)tableOffset + 4;
                        for (int i = 0; i < count && sp < data.Length; i++)
                        {
                            int end = Array.IndexOf(data, (byte)0, sp);
                            if (end < 0) break;
                            var s = Encoding.UTF8.GetString(data, sp, end - sp);
                            if (s == "clienticon") clientKey = BitConverter.GetBytes(i);
                            else if (s == "icon") iconKey = BitConverter.GetBytes(i);
                            sp = end + 1;
                        }
                    }
                }
                else
                {
                    clientKey = Encoding.ASCII.GetBytes("clienticon\0");
                    iconKey = Encoding.ASCII.GetBytes("icon\0");
                }
                int header = version >= 0x28 ? 4 + 4 + 8 + 20 + 4 + 20 : 4 + 4 + 8 + 20 + 4;
                while (pos + 8 <= data.Length)
                {
                    uint appId = BitConverter.ToUInt32(data, pos);
                    if (appId == 0) break;
                    int size = BitConverter.ToInt32(data, pos + 4);
                    int start = pos + 8, stop = start + size;
                    if (size <= 0 || stop > data.Length) break;
                    int kv = start + header;
                    string? client = clientKey == null ? null : FindKvString(data, kv, stop, clientKey);
                    string? icon = iconKey == null ? null : FindKvString(data, kv, stop, iconKey);
                    if (client != null || icon != null) result[appId.ToString()] = (client, icon);
                    pos = stop;
                }
            }
            catch (Exception ex) { Log.Warn("appinfo.vdf: " + ex.Message); }
        }
        return result;
    }

    /// <summary>Ищет в бинарном KeyValues строковое поле (тип 0x01) с заданным ключом и возвращает 40-символьный хэш.</summary>
    static string? FindKvString(byte[] data, int from, int to, byte[] key)
    {
        for (int i = from; i < to - key.Length - 1; i++)
        {
            if (data[i] != 0x01) continue;
            bool match = true;
            for (int k = 0; k < key.Length; k++) if (data[i + 1 + k] != key[k]) { match = false; break; }
            if (!match) continue;
            int vs = i + 1 + key.Length, ve = Array.IndexOf(data, (byte)0, vs, Math.Max(0, Math.Min(to, vs + 64) - vs));
            if (ve <= vs) continue;
            var value = Encoding.ASCII.GetString(data, vs, ve - vs);
            if (Regex.IsMatch(value, "^[0-9a-fA-F]{40}$")) return value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Ярлыки Steam (.url) на рабочем столе и в меню «Пуск»: в них прописан путь к иконке игры.</summary>
    static Dictionary<string, string> ReadSteamShortcutIcons()
    {
        var map = new Dictionary<string, string>();
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };
        foreach (var dir in dirs.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct())
        {
            try
            {
                foreach (var url in Directory.EnumerateFiles(dir, "*.url", SearchOption.AllDirectories).Take(800))
                {
                    try
                    {
                        string text = File.ReadAllText(url);
                        var id = Regex.Match(text, @"steam://rungameid/(\d+)", RegexOptions.IgnoreCase).Groups[1].Value;
                        var icon = Regex.Match(text, @"^IconFile=(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
                        if (id.Length > 0 && icon.Length > 0 && File.Exists(icon)) map[id] = icon;
                    }
                    catch { }
                }
            }
            catch { }
        }
        return map;
    }

    ImageSource? LoadSteamArtwork(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || !Regex.IsMatch(appId, @"^\d+$")) return null;
        if (_steamArtCache.TryGetValue(appId, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            EnsureSteamInfo();
            _steamAppInfo!.TryGetValue(appId, out var info);

            // 1) Ярлыки: .ico из ярлыка рабочего стола / «Пуска» или из steam\games по clienticon.
            var icoCandidates = new List<string>();
            if (_steamShortcutIcons!.TryGetValue(appId, out var shortcutIcon)) icoCandidates.Add(shortcutIcon);
            if (info.ClientIcon != null)
                foreach (var root in _steamRoots!) icoCandidates.Add(Path.Combine(root, "steam", "games", info.ClientIcon + ".ico"));
            foreach (var ico in icoCandidates)
                if ((result = LoadIconFile(ico)) != null) break;

            // 2) Маленькая иконка из кэша библиотеки (новый формат — папка appid, старый — appid_icon.jpg).
            if (result == null)
            {
                var jpg = new List<string>();
                foreach (var root in _steamLibraries!)
                {
                    var cache = Path.Combine(root, "appcache", "librarycache");
                    var folder = Path.Combine(cache, appId);
                    if (info.Icon != null)
                    {
                        jpg.Add(Path.Combine(folder, info.Icon + ".jpg"));
                        jpg.Add(Path.Combine(root, "steam", "games", info.Icon + ".jpg"));
                    }
                    try
                    {
                        if (Directory.Exists(folder))
                            jpg.AddRange(Directory.EnumerateFiles(folder, "*.jpg")
                                .Where(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f), "^[0-9a-f]{40}$", RegexOptions.IgnoreCase)));
                    }
                    catch { }
                    jpg.Add(Path.Combine(cache, appId + "_icon.jpg"));
                }
                foreach (var f in jpg) if ((result = LoadBitmapFile(f, 64)) != null) break;
            }

            // 3) Если иконки нет — обложка/логотип игры (будет аккуратно обрезана по центру).
            if (result == null)
            {
                foreach (var root in _steamLibraries!)
                {
                    var cache = Path.Combine(root, "appcache", "librarycache");
                    var folder = Path.Combine(cache, appId);
                    foreach (var f in new[]
                    {
                        Path.Combine(folder, "library_600x900.jpg"), Path.Combine(cache, appId + "_library_600x900.jpg"),
                        Path.Combine(folder, "logo.png"), Path.Combine(cache, appId + "_logo.png"),
                        Path.Combine(folder, "header.jpg"), Path.Combine(cache, appId + "_header.jpg")
                    })
                        if ((result = LoadBitmapFile(f, 128)) != null) break;
                    if (result != null) break;
                }
            }
        }
        catch (Exception ex) { Log.Warn("Иконка Steam " + appId + ": " + ex.Message); }
        _steamArtCache[appId] = result;
        return result;
    }

    static ImageSource? LoadIconFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (!path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return LoadBitmapFile(path, 64);
            using var fs = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapFrame? best = null;
            foreach (var f in decoder.Frames)
                if (best == null || (f.PixelWidth > best.PixelWidth && f.PixelWidth <= 256)) best = f;
            if (best == null) return null;
            best.Freeze();
            return best;
        }
        catch { return null; }
    }

    static ImageSource? LoadBitmapFile(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }
}

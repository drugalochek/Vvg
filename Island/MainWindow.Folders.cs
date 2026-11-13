using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Point = System.Windows.Point;

namespace Island;

/// <summary>
/// v2.10: папки на вкладке «Приложения», свои подписи и иконки плиток.
/// Папка существует, пока в ней есть хотя бы одно приложение (хранится в AppShortcut.Folder).
/// </summary>
public partial class MainWindow
{
    IEnumerable<string> FolderNames() => _s.Apps.Where(a => a.Folder.Length > 0).Select(a => a.Folder)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);

    FrameworkElement CreateFolderTile(string folder, List<AppShortcut> apps)
    {
        var tile = new Grid
        {
            Width = AppTileW, Height = AppTileH, Margin = new Thickness(4), AllowDrop = true,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), Background = Brushes.Transparent
        };
        var button = new Button
        {
            Style = (Style)FindResource("TileBtn"), Width = AppTileW, Height = AppTileH, Margin = new Thickness(0),
            ToolTip = Tr($"Папка «{folder}» — {apps.Count} шт.", $"Folder “{folder}” — {apps.Count} item(s)")
        };

        // Превью: до четырёх иконок в рамке, как папки на телефоне.
        double box = AppIconSize + 6, mini = (box - 9) / 2;
        var grid = new UniformGrid { Columns = 2, Rows = 2, Width = box - 5, Height = box - 5 };
        foreach (var app in apps.Take(4))
        {
            var el = IconElement(AppIcon(app, IsSteamLink(app.Path)), IsSteamLink(app.Path), mini);
            el.Margin = new Thickness(0.5);
            grid.Children.Add(el);
        }
        var frame = new Border
        {
            Width = box, Height = box, CornerRadius = new CornerRadius(box * 0.26), Padding = new Thickness(2.5),
            Background = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, _s.AppTileSize == "Compact" ? 4 : 6),
            Child = grid
        };
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(frame);
        content.Children.Add(new TextBlock
        {
            Text = folder, FontSize = AppLabelSize, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = AppTileW - 14, HorizontalAlignment = HorizontalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) => OpenFolder(folder);

        var menu = new ContextMenu();
        button.ContextMenu = menu;
        button.ContextMenuOpening += (_, _) =>
        {
            menu.Items.Clear();
            AddMenu(menu, Tr("Открыть", "Open"), () => OpenFolder(folder));
            AddMenu(menu, Tr("Переименовать папку…", "Rename folder…"), () => RenameFolder(folder));
            AddMenu(menu, Tr("Расформировать папку", "Ungroup folder"), () =>
            {
                foreach (var a in _s.Apps.Where(a => SameFolder(a.Folder, folder))) a.Folder = "";
                _s.Save(); BuildApps();
            });
        };
        tile.Children.Add(button);

        // Бросили плитку на папку — кладём приложение внутрь.
        HookFolderDrop(tile, folder);
        return tile;
    }

    FrameworkElement CreateBackTile()
    {
        var tile = new Grid
        {
            Width = AppTileW, Height = AppTileH, Margin = new Thickness(4), AllowDrop = true,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), Background = Brushes.Transparent
        };
        var button = new Button { Style = (Style)FindResource("TileBtn"), Width = AppTileW, Height = AppTileH, Margin = new Thickness(0) };
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = "\uE72B", FontFamily = (FontFamily)FindResource("Icons"), FontSize = AppIconSize * 0.6,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 7)
        });
        content.Children.Add(new TextBlock
        {
            Text = Tr("Назад", "Back"), FontSize = AppLabelSize, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) => OpenFolder(null);
        tile.Children.Add(button);
        HookFolderDrop(tile, "");
        return tile;
    }

    void HookFolderDrop(FrameworkElement tile, string folder)
    {
        void Highlight(bool on)
        {
            if (tile.RenderTransform is not ScaleTransform st || st.IsFrozen) return;
            var a = new DoubleAnimation(on ? 1.08 : 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        tile.DragEnter += (_, e) => { if (e.Data.GetDataPresent("MokuAppPath")) Highlight(true); };
        tile.DragLeave += (_, _) => Highlight(false);
        tile.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("MokuAppPath") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        tile.Drop += (_, e) =>
        {
            e.Handled = true;
            Highlight(false);
            if (e.Data.GetData("MokuAppPath") is string path) MoveAppToFolder(path, folder, afterDrag: true);
        };
    }

    void OpenFolder(string? folder)
    {
        _openFolder = string.IsNullOrEmpty(folder) ? null : folder;
        BuildApps();
        FadePage(AppsGrid);
    }

    void MoveAppToFolder(string path, string folder, bool afterDrag = false)
    {
        var app = _s.Apps.FirstOrDefault(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
        if (app == null || SameFolder(app.Folder, folder)) return;
        // Пишем имя так же, как у уже существующей папки (без дублей «Игры» / «игры»).
        app.Folder = FolderNames().FirstOrDefault(f => SameFolder(f, folder)) ?? folder.Trim();
        _s.Save();
        ShowToast("Moku", app.Folder.Length > 0
            ? Tr($"Перемещено в «{app.Folder}»", $"Moved to “{app.Folder}”")
            : Tr("Вынуто из папки", "Taken out of the folder"));
        if (afterDrag)
            // Перетаскивание ещё не закончилось — перестраиваем плитки, когда DoDragDrop вернёт управление.
            Dispatcher.BeginInvoke(new Action(BuildApps), System.Windows.Threading.DispatcherPriority.Background);
        else BuildApps();
    }

    void RenameFolder(string folder)
    {
        string? name = AskText(Tr("Название папки", "Folder name"), folder, 40);
        if (name == null || name == folder) return;
        foreach (var a in _s.Apps.Where(a => SameFolder(a.Folder, folder))) a.Folder = name;
        if (SameFolder(_openFolder, folder)) _openFolder = name;
        _s.Save(); BuildApps();
    }

    string? AskText(string title, string initial, int maxLength = 60)
    {
        var dlg = new PromptWindow(title, initial, _s.Language == "ru", maxLength) { Owner = this };
        bool ok = WithModal(() => dlg.ShowDialog() == true);
        return ok && dlg.Value.Length > 0 ? dlg.Value : null;
    }

    static void AddMenu(ItemsControl menu, string header, Action act, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        mi.Click += (_, _) => act();
        menu.Items.Add(mi);
    }

    void BuildAppMenu(ContextMenu menu, AppShortcut app, FrameworkElement tile)
    {
        menu.Items.Clear();
        AddMenu(menu, Tr("Переименовать…", "Rename…"), () =>
        {
            string? name = AskText(Tr("Подпись плитки", "Tile label"), app.Name);
            if (name == null || name == app.Name) return;
            app.Name = name; _s.Save(); BuildApps();
        });
        AddMenu(menu, Tr("Сменить иконку…", "Change icon…"), () => ChooseAppIcon(app));
        AddMenu(menu, Tr("Стандартная иконка", "Default icon"), () => { app.Icon = ""; _s.Save(); BuildApps(); }, app.Icon.Length > 0);

        var sizeMenu = new MenuItem { Header = Tr("Размер плитки", "Tile size") };
        foreach (var (size, label) in new[] { ("1x1", "1 × 1"), ("2x1", "2 × 1"), ("2x2", "2 × 2") })
        {
            var sizeItem = new MenuItem { Header = label, IsCheckable = true, IsChecked = (_s.AppTileSizes.TryGetValue(app.Path, out var currentSize) ? currentSize : "1x1") == size };
            sizeItem.Click += (_, _) => { _s.AppTileSizes[app.Path] = size; _s.Save(); BuildApps(); };
            sizeMenu.Items.Add(sizeItem);
        }
        menu.Items.Add(sizeMenu);

        var move = new MenuItem { Header = Tr("Переместить в папку", "Move to folder") };
        foreach (var f in FolderNames().Where(f => !SameFolder(f, app.Folder)))
        {
            string target = f;
            AddMenu(move, f, () => MoveAppToFolder(app.Path, target));
        }
        if (move.Items.Count > 0) move.Items.Add(new Separator());
        AddMenu(move, Tr("Новая папка…", "New folder…"), () =>
        {
            string? name = AskText(Tr("Название новой папки", "New folder name"), Tr("Папка", "Folder"), 40);
            if (name != null) MoveAppToFolder(app.Path, name);
        });
        if (app.Folder.Length > 0) AddMenu(move, Tr("Убрать из папки", "Remove from folder"), () => MoveAppToFolder(app.Path, ""));
        menu.Items.Add(move);

        menu.Items.Add(new Separator());
        AddMenu(menu, Tr("Удалить из списка", "Remove from list"), () => RemoveAppAnimated(tile, app.Path));
    }

    void ChooseAppIcon(AppShortcut app)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Tr("Выбери картинку или иконку", "Choose an image or icon"),
            Filter = Tr("Картинки и иконки|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll|Все файлы|*.*",
                        "Images and icons|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll|All files|*.*"),
            CheckFileExists = true, Multiselect = false
        };
        if (WithModal(() => dialog.ShowDialog(this)) != true) return;
        if (LoadCustomIcon(dialog.FileName) == null)
        {
            ShowToast("Moku", Tr("Не удалось открыть эту картинку", "Could not open this image"), ColorError);
            return;
        }
        app.Icon = dialog.FileName;
        _s.Save();
        BuildApps();
    }
}

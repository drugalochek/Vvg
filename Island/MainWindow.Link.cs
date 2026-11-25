using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using QRCoder;
using Brush = System.Windows.Media.Brush;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Island;

/// <summary>Moku Link: вкладка «Телефон» и связь с приложением Moku для Android.</summary>
public partial class MainWindow
{
    TransferServer? _link;
    TransferDashboard? _linkDashboard;
    string? _linkError, _linkCode, _linkSelected, _linkProgress, _linkAddress;
    byte[]? _linkQr;
    bool _linkPairing;
    readonly List<(string Id, LinkItemView Item)> _linkRecent = new();
    readonly DispatcherTimer _linkTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    DispatcherTimer? _linkRenderThrottle;
    bool _linkTimerHooked;

    string LinkFolder => string.IsNullOrWhiteSpace(_s.TransferFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Moku")
        : _s.TransferFolder;

    /// <summary>Запуск или остановка сервера по настройке. Вызывается при старте и при переключении.</summary>
    void ApplyLinkState()
    {
        if (!_s.TransferEnabled)
        {
            _link?.Dispose(); _link = null; _linkPairing = false; _linkError = null; _linkProgress = null;
            RenderLink();
            return;
        }
        if (_link != null) { RenderLink(); return; }
        try
        {
            string folder = LinkFolder;
            var server = new TransferServer(_s.TransferPcId, () => folder) { PcName = Environment.MachineName };
            server.SetPhones(_s.LinkedPhones);
            server.Paired += p => Dispatcher.BeginInvoke(() => OnLinkPaired(p));
            server.Unpaired += id => Dispatcher.BeginInvoke(() => RemoveLinkedPhone(id, notifyPhone: false));
            server.FileReceived += f => Dispatcher.BeginInvoke(() => OnLinkReceived(f));
            server.Delivered += o => Dispatcher.BeginInvoke(() => OnLinkDelivered(o));
            server.Progress += (_, name, done, total) => Dispatcher.BeginInvoke(() =>
            {
                _linkProgress = done >= total ? null : $"{name} — {(total > 0 ? done * 100 / total : 0)}%";
                RenderLinkSoon();
            });
            server.PhoneSeen += _ => { };
            server.Start();
            _link = server; _linkError = null;
            if (_s.LinkedPhones.Count == 0) StartLinkPairing();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            _link = null;
            _linkError = Tr($"Порт {TransferPolicy.Port} занят другой программой", $"Port {TransferPolicy.Port} is used by another app");
            Log.Error("link start", ex);
        }
        catch (Exception ex)
        {
            _link = null; _linkError = Tr("Не удалось запустить передачу", "Could not start transfer");
            Log.Error("link start", ex);
        }
        RenderLink();
    }

    void StartLinkPairing()
    {
        if (_link == null) return;
        _linkCode = _link.NewPairCode();
        var hosts = TransferServer.LocalAddresses();
        _linkAddress = hosts.FirstOrDefault();
        _linkQr = null;
        if (hosts.Count > 0)
        {
            try
            {
                string uri = TransferPolicy.PairUri(hosts, _link.Port, _s.TransferPcId, Environment.MachineName, _linkCode);
                using var gen = new QRCodeGenerator();
                using var data = gen.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
                _linkQr = new PngByteQRCode(data).GetGraphic(8, new byte[] { 0, 0, 0, 255 }, new byte[] { 255, 255, 255, 255 }, false);
            }
            catch (Exception ex) { Log.Error("link qr", ex); }
        }
        _linkPairing = true;
        RenderLink();
    }

    void OnLinkPaired(TransferServer.Phone p)
    {
        _s.LinkedPhones.RemoveAll(x => x.Id == p.Id);
        _s.LinkedPhones.Add(new LinkedPhone { Id = p.Id, Name = p.Name, TokenHash = p.TokenHash, LastSeenUtc = DateTime.UtcNow });
        _s.Save();
        _linkPairing = false; _linkCode = null; _linkQr = null; _linkSelected = p.Id;
        ShowToast(Tr("Телефон подключён", "Phone connected"), p.Name, "#FF30D158");
        RenderLink();
    }

    void RemoveLinkedPhone(string id, bool notifyPhone)
    {
        _s.LinkedPhones.RemoveAll(x => x.Id == id);
        _s.Save();
        _link?.SetPhones(_s.LinkedPhones);
        if (_linkSelected == id) _linkSelected = null;
        if (_s.LinkedPhones.Count == 0 && _link != null) StartLinkPairing();
        RenderLink();
    }

    void OnLinkReceived(TransferServer.Received f)
    {
        AddLinkRecent(Guid.NewGuid().ToString("N"), new LinkItemView(f.Name, f.Size, true, Tr("Получено", "Received"), f.Path, true));
        _linkProgress = null;
        ShowToast(Tr("Получено с телефона", "Received from phone"), f.Name, "#FF30D158");
        RenderLink();
    }

    void OnLinkDelivered(TransferServer.Outgoing o)
    {
        int i = _linkRecent.FindIndex(r => r.Id == o.Id);
        if (i >= 0) _linkRecent[i] = (o.Id, _linkRecent[i].Item with { Status = Tr("Отправлено ✓", "Sent ✓"), Done = true });
        _linkProgress = null;
        if (_link != null && _link.Pending(o.PhoneId) == 0) ShowToast(Tr("Отправлено на телефон", "Sent to phone"), o.Name, "#FF30D158");
        RenderLink();
    }

    void AddLinkRecent(string id, LinkItemView item)
    {
        _linkRecent.Insert(0, (id, item));
        if (_linkRecent.Count > 20) _linkRecent.RemoveRange(20, _linkRecent.Count - 20);
    }

    string? LinkTarget()
    {
        if (_linkSelected != null && _s.LinkedPhones.Any(p => p.Id == _linkSelected)) return _linkSelected;
        var online = _s.LinkedPhones.FirstOrDefault(p => _link?.IsOnline(p.Id) == true);
        return (online ?? _s.LinkedPhones.FirstOrDefault())?.Id;
    }

    void SendFilesToPhone(IEnumerable<string> paths)
    {
        var target = LinkTarget();
        if (_link == null || target == null) return;
        int skipped = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path)) { skipped++; continue; }
            try
            {
                var o = _link.Enqueue(target, path);
                AddLinkRecent(o.Id, new LinkItemView(o.Name, o.Size, false,
                    _link.IsOnline(target) ? Tr("Отправляется…", "Sending…") : Tr("Ждёт телефон", "Waiting for phone"), null, false));
            }
            catch (Exception ex) { skipped++; Log.Error("link enqueue", ex); }
        }
        if (skipped > 0) _linkProgress = Tr("Папки пока не отправляются — выбери файлы внутри", "Folders are not sent yet — pick the files inside");
        RenderLink();
    }

    void OnLinkAction(string action, string? arg)
    {
        try
        {
            switch (action)
            {
                case "toggle":
                    _s.TransferEnabled = !_s.TransferEnabled; _s.Save(); ApplyLinkState(); break;
                case "pair": StartLinkPairing(); break;
                case "cancelpair": _link?.CancelPairing(); _linkPairing = false; RenderLink(); break;
                case "select": _linkSelected = arg; RenderLink(); break;
                case "unlink": if (arg != null) RemoveLinkedPhone(arg, notifyPhone: false); break;
                case "pick":
                    var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = Tr("Файлы для телефона", "Files for the phone") };
                    if (dlg.ShowDialog(this) == true) SendFilesToPhone(dlg.FileNames);
                    break;
                case "folder":
                    Directory.CreateDirectory(LinkFolder);
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + LinkFolder + "\"") { UseShellExecute = true });
                    break;
                case "open":
                    if (arg != null && File.Exists(arg))
                        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + arg + "\"") { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex) { Log.Error("link action " + action, ex); }
    }

    void RenderLinkSoon()
    {
        if (_linkRenderThrottle == null)
        {
            _linkRenderThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _linkRenderThrottle.Tick += (_, _) => { _linkRenderThrottle!.Stop(); RenderLink(); };
        }
        if (!_linkRenderThrottle.IsEnabled) _linkRenderThrottle.Start();
    }

    void RenderLink()
    {
        if (TransferPage == null || TransferPage.Visibility != Visibility.Visible) return;
        if (_linkDashboard == null)
        {
            _linkDashboard = new TransferDashboard(Ru, _s.CornerRadius, OnLinkAction, TryFindResource("Card") as Brush, TryFindResource("CardRim") as Brush);
            TransferPage.Content = _linkDashboard;
        }
        var phones = _s.LinkedPhones.Select(p => new LinkPhoneView(p.Id, p.Name, _link?.IsOnline(p.Id) == true, _link?.Pending(p.Id) ?? 0)).ToList();
        _linkDashboard.Render(new LinkView(_s.TransferEnabled && _link != null || _linkError != null && _s.TransferEnabled, _linkError,
            _linkPairing, _linkQr, _linkCode, _linkAddress, phones, LinkTarget(), _linkRecent.Select(r => r.Item).ToList(), LinkFolder, _linkProgress));
        if (_linkDashboard.DropZone is { } zone)
        {
            zone.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            zone.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) SendFilesToPhone(files); e.Handled = true; };
        }
    }

    /// <summary>Вкладка открыта/закрыта: обновление статуса «в сети» идёт только пока её видно.</summary>
    void UpdateLinkPageState(bool visible)
    {
        if (!_linkTimerHooked)
        {
            _linkTimerHooked = true;
            string last = "";
            _linkTimer.Tick += (_, _) =>
            {
                string sig = string.Join("|", _s.LinkedPhones.Select(p => p.Id + (_link?.IsOnline(p.Id) == true)));
                if (sig != last) { last = sig; RenderLink(); }
            };
        }
        if (visible) { _linkDashboard = null; RenderLink(); _linkTimer.Start(); }
        else _linkTimer.Stop();
    }

    void RebuildLinkDashboard() { _linkDashboard = null; RenderLink(); }

    void RestartLinkIfRunning()
    {
        if (_link == null) { RenderLink(); return; }
        _link.Dispose(); _link = null;
        ApplyLinkState();
    }

    void StopLink() { _linkTimer.Stop(); _link?.Dispose(); _link = null; }
}

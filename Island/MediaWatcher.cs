using System.IO;
using System.Windows.Threading;
using Windows.Media.Control;

namespace Island;

/// <summary>Текущий трек. Art — байты обложки (PNG/JPEG из медиасессии), если плеер её отдаёт.</summary>
public record NowPlaying(string Title, string Artist, bool Playing, byte[]? Art = null);

/// <summary>Следит за текущей медиасессией Windows (Яндекс Музыка, Spotify, браузер, AIMP и т.д.).</summary>
public class MediaWatcher : IDisposable
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _session;
    // Создаётся в UI-потоке: события WinRT приходят из пула потоков, UI трогаем только через Dispatcher.
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    int _version;
    string? _artKey;          // обложка кэшируется по треку: события плеера приходят часто, а читать её дорого
    byte[]? _artBytes;
    volatile bool _disposed;

    public event Action<NowPlaying?>? Changed;

    public async Task StartAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _mgr.CurrentSessionChanged += OnCurrentSessionChanged;
            Attach();
        }
        catch (Exception ex)
        {
            Log.Error("Медиа-API Windows недоступен, трек отображаться не будет", ex);
        }
    }

    void Attach()
    {
        if (_disposed) return;
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
        }
        _session = _mgr?.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnProps;
            _session.PlaybackInfoChanged += OnPlayback;
        }
        _ = Refresh();
    }

    void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = Refresh();
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = Refresh();

    /// <summary>Обложка трека (Spotify, Яндекс Музыка, YouTube в браузере и т.д.). null — если её нет.</summary>
    async Task<byte[]?> ReadArtAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        string key = (props.Artist ?? "") + "|" + props.Title + "|" + (props.AlbumTitle ?? "");
        if (key == _artKey && _artBytes != null) return _artBytes;
        var thumb = props.Thumbnail;
        if (thumb == null) return null;
        try
        {
            using var ras = await thumb.OpenReadAsync();
            if (ras.Size == 0 || ras.Size > 12_000_000) return null;
            using var src = ras.AsStreamForRead();
            using var ms = new MemoryStream((int)ras.Size);
            await src.CopyToAsync(ms);
            if (ms.Length == 0) return null;
            _artKey = key;
            _artBytes = ms.ToArray();
            return _artBytes;
        }
        catch (Exception ex)
        {
            Log.Warn("Не удалось прочитать обложку трека: " + ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_mgr != null) _mgr.CurrentSessionChanged -= OnCurrentSessionChanged;
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
        }
        _session = null;
        _mgr = null;
    }

    void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => Attach();

    async Task Refresh()
    {
        int ver = Interlocked.Increment(ref _version);
        NowPlaying? np = null;
        var s = _session;
        if (s != null)
        {
            try
            {
                var props = await s.TryGetMediaPropertiesAsync();
                var playing = s.GetPlaybackInfo().PlaybackStatus ==
                              GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (props != null && !string.IsNullOrWhiteSpace(props.Title))
                    np = new NowPlaying(props.Title, props.Artist ?? "", playing, await ReadArtAsync(props));
            }
            catch (Exception ex)
            {
                Log.Warn("Не удалось получить данные медиасессии: " + ex.Message);
            }
        }
        // Более новый запрос уже запущен — результат этого устарел, не даём ему перезаписать свежий.
        if (_disposed || ver != Volatile.Read(ref _version)) return;
        _ = _ui.BeginInvoke(new Action(() => { if (!_disposed) Changed?.Invoke(np); }));
    }
}

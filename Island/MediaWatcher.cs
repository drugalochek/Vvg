using System.Windows.Threading;
using Windows.Media.Control;

namespace Island;

public record NowPlaying(string Title, string Artist, bool Playing);

/// <summary>Следит за текущей медиасессией Windows (Яндекс Музыка, Spotify, браузер, AIMP и т.д.).</summary>
public class MediaWatcher : IDisposable
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _session;
    // Создаётся в UI-потоке: события WinRT приходят из пула потоков, UI трогаем только через Dispatcher.
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    int _version;
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
                    np = new NowPlaying(props.Title, props.Artist ?? "", playing);
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

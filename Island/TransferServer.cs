using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Island;

/// <summary>
/// Moku Link — локальный сервер передачи файлов для приложения Moku на телефоне.
/// Работает только пока включена передача: TCP 47820 (запросы) и UDP 47821 (поиск ПК в сети).
/// Каждое подключённое устройство авторизуется своим токеном, выданным после сканирования QR-кода.
/// </summary>
internal sealed class TransferServer : IDisposable
{
    internal sealed record Phone(string Id, string Name, string TokenHash);
    internal sealed record Outgoing(string Id, string PhoneId, string Path, string Name, long Size);
    internal sealed record Received(string PhoneId, string PhoneName, string Path, string Name, long Size);

    readonly string pcId;
    readonly Func<string> folder;
    readonly ConcurrentDictionary<string, Phone> phonesByHash = new();
    readonly ConcurrentDictionary<string, Outgoing> outbox = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> wakeups = new();
    readonly ConcurrentDictionary<string, DateTime> lastSeen = new();
    readonly CancellationTokenSource cts = new();
    TcpListener? listener;
    UdpClient? udp;
    string? pairCode;
    DateTime pairExpires;
    int pairFailures;
    readonly object pairLock = new();

    internal string PcName { get; set; } = Environment.MachineName;
    internal int Port { get; private set; } = TransferPolicy.Port;
    internal bool Running => listener != null;

    internal event Action<Phone>? Paired;
    internal event Action<string>? Unpaired;
    internal event Action<Received>? FileReceived;
    internal event Action<Outgoing>? Delivered;
    internal event Action<string, string, long, long>? Progress; // phoneId, name, done, total
    internal event Action<string>? PhoneSeen;

    internal TransferServer(string pcId, Func<string> folder)
    {
        this.pcId = pcId; this.folder = folder;
    }

    internal void SetPhones(IEnumerable<LinkedPhone> phones)
    {
        phonesByHash.Clear();
        foreach (var p in phones) phonesByHash[p.TokenHash] = new Phone(p.Id, p.Name, p.TokenHash);
        foreach (var o in outbox.Values.Where(o => !phonesByHash.Values.Any(p => p.Id == o.PhoneId)).ToList()) outbox.TryRemove(o.Id, out _);
    }

    internal bool IsOnline(string phoneId) => lastSeen.TryGetValue(phoneId, out var t) && DateTime.UtcNow - t < TimeSpan.FromSeconds(45);

    internal string NewPairCode()
    {
        lock (pairLock)
        {
            pairCode = TransferPolicy.NewPairCode();
            pairExpires = DateTime.UtcNow + TransferPolicy.PairCodeLifetime;
            pairFailures = 0;
            return pairCode;
        }
    }

    internal void CancelPairing() { lock (pairLock) pairCode = null; }

    internal void Start()
    {
        if (listener != null) return;
        var l = new TcpListener(IPAddress.Any, TransferPolicy.Port);
        l.Start();
        listener = l; Port = ((IPEndPoint)l.LocalEndpoint).Port;
        _ = AcceptLoop(l, cts.Token);
        try
        {
            var u = new UdpClient(AddressFamily.InterNetwork);
            u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            u.Client.Bind(new IPEndPoint(IPAddress.Any, TransferPolicy.DiscoveryPort));
            udp = u;
            _ = DiscoveryLoop(u, cts.Token);
        }
        catch (Exception ex) { Log.Error("link discovery", ex); }
    }

    internal Outgoing Enqueue(string phoneId, string path)
    {
        var info = new FileInfo(path);
        var o = new Outgoing(Guid.NewGuid().ToString("N"), phoneId, info.FullName, TransferPolicy.SafeFileName(info.Name), info.Length);
        outbox[o.Id] = o;
        Wake(phoneId);
        return o;
    }

    internal int Pending(string phoneId) => outbox.Values.Count(o => o.PhoneId == phoneId);

    void Wake(string phoneId)
    {
        var s = wakeups.GetOrAdd(phoneId, _ => new SemaphoreSlim(0));
        if (s.CurrentCount == 0) s.Release();
    }

    /// <summary>IPv4-адреса ПК в локальной сети: сначала Wi-Fi и Ethernet, без виртуальных адаптеров и VPN.</summary>
    internal static List<string> LocalAddresses()
    {
        var list = new List<(int Rank, string Ip)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp) continue;
                string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                bool virt = d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("vethernet") ||
                            d.Contains("wintun") || d.Contains("wireguard") || d.Contains("tap-") || d.Contains("tun") || d.Contains("happ") ||
                            d.Contains("loopback") || d.Contains("bluetooth");
                int rank = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 1 : 2;
                if (virt) rank += 10;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a.Address)) continue;
                    var b = a.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;
                    list.Add((rank, a.Address.ToString()));
                }
            }
        }
        catch (Exception ex) { Log.Error("link addresses", ex); }
        var good = list.Where(x => x.Rank < 10).ToList();
        return (good.Count > 0 ? good : list).OrderBy(x => x.Rank).Select(x => x.Ip).Distinct().Take(4).ToList();
    }

    async Task DiscoveryLoop(UdpClient u, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var r = await u.ReceiveAsync(ct);
                if (Encoding.UTF8.GetString(r.Buffer).Trim() != TransferPolicy.DiscoverMessage) continue;
                var reply = Encoding.UTF8.GetBytes($"{{\"app\":\"Moku\",\"id\":{TransferPolicy.Json(pcId)},\"name\":{TransferPolicy.Json(PcName)},\"port\":{Port},\"v\":{TransferPolicy.ProtocolVersion}}}");
                await u.SendAsync(reply, r.RemoteEndPoint, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex) { Log.Error("link discovery loop", ex); await Task.Delay(500, CancellationToken.None); }
        }
    }

    async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await l.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (ct.IsCancellationRequested) return; continue; }
            _ = Task.Run(() => Handle(client, ct));
        }
    }

    async Task Handle(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        client.NoDelay = true;
        try
        {
            var net = client.GetStream();
            net.ReadTimeout = 30000;
            var (req, leftover) = await ReadHead(net, ct);
            if (req == null) { await Respond(net, 400, "{\"error\":\"bad request\"}"); return; }
            var body = new BodyStream(net, leftover, req.ContentLength);
            await Route(req, body, net, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception ex) { Log.Error("link request", ex); }
    }

    static async Task<(TransferPolicy.Request?, byte[])> ReadHead(NetworkStream s, CancellationToken ct)
    {
        var buf = new byte[16384]; int len = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (len < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(len), timeout.Token);
            if (n == 0) return (null, Array.Empty<byte>());
            len += n;
            for (int i = 3; i < len; i++)
            {
                if (buf[i - 3] == '\r' && buf[i - 2] == '\n' && buf[i - 1] == '\r' && buf[i] == '\n')
                {
                    var head = Encoding.UTF8.GetString(buf, 0, i - 3);
                    return (TransferPolicy.ParseRequestHead(head), buf[(i + 1)..len]);
                }
            }
        }
        return (null, Array.Empty<byte>());
    }

    Phone? Authorize(TransferPolicy.Request req)
    {
        if (!req.Headers.TryGetValue("X-Moku-Token", out var token) || token.Length < 16) return null;
        if (!phonesByHash.TryGetValue(TransferPolicy.Hash(token), out var phone)) return null;
        lastSeen[phone.Id] = DateTime.UtcNow;
        PhoneSeen?.Invoke(phone.Id);
        return phone;
    }

    async Task Route(TransferPolicy.Request req, BodyStream body, NetworkStream net, CancellationToken ct)
    {
        string hello = $"{{\"app\":\"Moku\",\"id\":{TransferPolicy.Json(pcId)},\"name\":{TransferPolicy.Json(PcName)},\"v\":{TransferPolicy.ProtocolVersion}}}";
        if (req.Path == "/api/hello") { await Respond(net, 200, hello); return; }
        if (req.Path == "/api/pair" && req.Method == "POST") { await Pair(req, net); return; }

        var phone = Authorize(req);
        if (phone == null) { await Respond(net, 401, "{\"error\":\"unauthorized\"}"); return; }

        switch (req.Path)
        {
            case "/api/ping":
                await Respond(net, 200, hello);
                return;
            case "/api/upload" when req.Method == "POST":
                await Upload(phone, req, body, net, ct);
                return;
            case "/api/inbox":
                await Inbox(phone, req, net, ct);
                return;
            case "/api/file":
                await SendFile(phone, req, net, ct);
                return;
            case "/api/ack" when req.Method == "POST":
                if (req.Query.TryGetValue("id", out var id) && outbox.TryGetValue(id, out var o) && o.PhoneId == phone.Id && outbox.TryRemove(id, out _))
                    Delivered?.Invoke(o);
                await Respond(net, 200, "{\"ok\":true}");
                return;
            case "/api/unpair" when req.Method == "POST":
                phonesByHash.TryRemove(phone.TokenHash, out _);
                Unpaired?.Invoke(phone.Id);
                await Respond(net, 200, "{\"ok\":true}");
                return;
            default:
                await Respond(net, 404, "{\"error\":\"not found\"}");
                return;
        }
    }

    async Task Pair(TransferPolicy.Request req, NetworkStream net)
    {
        string code = req.Query.GetValueOrDefault("code") ?? "";
        string phoneId = req.Query.GetValueOrDefault("phone") ?? "";
        string name = (req.Query.GetValueOrDefault("device") ?? "Phone").Trim();
        if (name.Length == 0) name = "Phone";
        if (name.Length > 60) name = name[..60];
        if (phoneId.Length is < 8 or > 64) { await Respond(net, 400, "{\"error\":\"bad phone id\"}"); return; }
        bool ok;
        lock (pairLock)
        {
            ok = pairCode != null && DateTime.UtcNow < pairExpires && TransferPolicy.SameSecret(code, pairCode);
            if (ok) pairCode = null; // одноразовый
            else if (pairCode != null && ++pairFailures >= 10) pairCode = null;
        }
        if (!ok) { await Respond(net, 403, "{\"error\":\"code\"}"); return; }
        string token = TransferPolicy.NewSecret(32);
        var phone = new Phone(phoneId, name, TransferPolicy.Hash(token));
        foreach (var old in phonesByHash.Where(p => p.Value.Id == phoneId).ToList()) phonesByHash.TryRemove(old.Key, out _);
        phonesByHash[phone.TokenHash] = phone;
        lastSeen[phone.Id] = DateTime.UtcNow;
        Paired?.Invoke(phone);
        await Respond(net, 200, $"{{\"token\":{TransferPolicy.Json(token)},\"id\":{TransferPolicy.Json(pcId)},\"name\":{TransferPolicy.Json(PcName)}}}");
    }

    async Task Upload(Phone phone, TransferPolicy.Request req, BodyStream body, NetworkStream net, CancellationToken ct)
    {
        string dir = folder();
        Directory.CreateDirectory(dir);
        string name = TransferPolicy.SafeFileName(req.Query.GetValueOrDefault("name"));
        string tmp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".mokupart");
        long total = req.ContentLength, done = 0;
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                var buf = new byte[1 << 16];
                var last = DateTime.MinValue;
                int n;
                while ((n = await body.ReadAsync(buf, ct)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (DateTime.UtcNow - last > TimeSpan.FromMilliseconds(250)) { last = DateTime.UtcNow; Progress?.Invoke(phone.Id, name, done, total); }
                }
            }
            if (done != total) throw new IOException("upload truncated");
            string final;
            lock (pairLock)
            {
                final = TransferPolicy.UniqueName(name, f => File.Exists(Path.Combine(dir, f)));
                File.Move(tmp, Path.Combine(dir, final));
            }
            Progress?.Invoke(phone.Id, name, total, total);
            FileReceived?.Invoke(new Received(phone.Id, phone.Name, Path.Combine(dir, final), final, total));
            await Respond(net, 200, $"{{\"ok\":true,\"name\":{TransferPolicy.Json(final)}}}");
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    async Task Inbox(Phone phone, TransferPolicy.Request req, NetworkStream net, CancellationToken ct)
    {
        int wait = int.TryParse(req.Query.GetValueOrDefault("wait"), out var w) ? Math.Clamp(w, 0, 30) : 0;
        var items = outbox.Values.Where(o => o.PhoneId == phone.Id).ToList();
        if (items.Count == 0 && wait > 0)
        {
            var s = wakeups.GetOrAdd(phone.Id, _ => new SemaphoreSlim(0));
            while (s.CurrentCount > 0) s.Wait(0);
            try { await s.WaitAsync(TimeSpan.FromSeconds(wait), ct); } catch (OperationCanceledException) { return; }
            items = outbox.Values.Where(o => o.PhoneId == phone.Id).ToList();
        }
        var json = string.Join(",", items.Select(o => $"{{\"id\":{TransferPolicy.Json(o.Id)},\"name\":{TransferPolicy.Json(o.Name)},\"size\":{o.Size}}}"));
        await Respond(net, 200, "{\"files\":[" + json + "]}");
    }

    async Task SendFile(Phone phone, TransferPolicy.Request req, NetworkStream net, CancellationToken ct)
    {
        if (!req.Query.TryGetValue("id", out var id) || !outbox.TryGetValue(id, out var o) || o.PhoneId != phone.Id)
        { await Respond(net, 404, "{\"error\":\"not found\"}"); return; }
        FileStream fs;
        try { fs = new FileStream(o.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, true); }
        catch { outbox.TryRemove(id, out _); await Respond(net, 410, "{\"error\":\"gone\"}"); return; }
        await using (fs)
        {
            long total = fs.Length;
            var head = $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {total}\r\n" +
                       $"X-Moku-Name: {Uri.EscapeDataString(o.Name)}\r\nConnection: close\r\n\r\n";
            await net.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
            var buf = new byte[1 << 16]; long done = 0; int n; var last = DateTime.MinValue;
            while ((n = await fs.ReadAsync(buf, ct)) > 0)
            {
                await net.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (DateTime.UtcNow - last > TimeSpan.FromMilliseconds(250)) { last = DateTime.UtcNow; Progress?.Invoke(phone.Id, o.Name, done, total); }
            }
            await net.FlushAsync(ct);
        }
    }

    static async Task Respond(NetworkStream net, int code, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var head = $"HTTP/1.1 {code} {TransferPolicy.StatusText(code)}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await net.WriteAsync(Encoding.ASCII.GetBytes(head));
        await net.WriteAsync(body);
        await net.FlushAsync();
    }

    public void Dispose()
    {
        cts.Cancel();
        try { listener?.Stop(); } catch { }
        try { udp?.Dispose(); } catch { }
        listener = null; udp = null;
        foreach (var s in wakeups.Values) { try { s.Release(); } catch { } }
    }

    /// <summary>Тело запроса: уже прочитанные вместе с заголовком байты + остаток из сокета, строго Content-Length.</summary>
    sealed class BodyStream
    {
        readonly NetworkStream net; readonly byte[] head; int headPos; long remaining;
        internal BodyStream(NetworkStream net, byte[] head, long length) { this.net = net; this.head = head; remaining = length; }
        internal async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            if (remaining <= 0) return 0;
            int max = (int)Math.Min(buffer.Length, remaining);
            int n;
            if (headPos < head.Length)
            {
                n = Math.Min(max, head.Length - headPos);
                head.AsMemory(headPos, n).CopyTo(buffer);
                headPos += n;
            }
            else
            {
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(TimeSpan.FromSeconds(60));
                n = await net.ReadAsync(buffer[..max], t.Token);
                if (n == 0) throw new IOException("connection closed");
            }
            remaining -= n;
            return n;
        }
    }
}

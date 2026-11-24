using System.Diagnostics;
using Microsoft.Win32;

namespace Island;

internal enum HappVpnRequestResult { Sent, ClientMissing, Failed }

/// <summary>
/// Happ Desktop deep links handled by its runtime (present in Happ 4.8.x): happ://connect, happ://disconnect.
/// happ://open (documented, 4.6.1+) also starts the connection and is used as a fallback for older builds.
/// Delivering a link is NOT evidence of a tunnel: the tile state comes from the real adapter / system proxy.
/// No simulated clicks, config edits or process kills.
/// </summary>
internal sealed class HappVpnConnector
{
    internal static readonly Uri ConnectUri = new("happ://connect");
    internal static readonly Uri DisconnectUri = new("happ://disconnect");
    internal static readonly Uri LegacyConnectUri = new("happ://open");
    readonly Func<bool> registered;
    readonly Action<Uri> activate;
    internal HappVpnConnector(Func<bool>? registered = null, Action<Uri>? activate = null)
    {
        this.registered = registered ?? IsRegistered;
        this.activate = activate ?? Activate;
    }
    internal HappVpnRequestResult RequestConnection() => Send(ConnectUri);
    internal HappVpnRequestResult RequestDisconnection() => Send(DisconnectUri);
    internal HappVpnRequestResult RequestLegacyConnection() => Send(LegacyConnectUri);
    internal bool Installed { get { try { return registered(); } catch { return false; } } }
    HappVpnRequestResult Send(Uri uri)
    {
        try
        {
            if (!registered()) return HappVpnRequestResult.ClientMissing;
            activate(uri);
            return HappVpnRequestResult.Sent;
        }
        catch (Exception ex)
        {
            Log.Error("Could not send Happ request " + uri, ex);
            return HappVpnRequestResult.Failed;
        }
    }
    static bool IsRegistered()
    {
        using var key = Registry.ClassesRoot.OpenSubKey("happ");
        if (key == null || !key.GetValueNames().Contains("URL Protocol", StringComparer.OrdinalIgnoreCase)) return false;
        using var command = key.OpenSubKey(@"shell\open\command");
        return command?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value);
    }
    static void Activate(Uri uri)
    {
        using var process = Process.Start(new ProcessStartInfo(uri.OriginalString) { UseShellExecute = true });
    }
}

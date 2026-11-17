using System.Diagnostics;
using Microsoft.Win32;

namespace Island;

internal enum HappVpnRequestResult { Sent, ClientMissing, Failed }

/// <summary>
/// Happ Desktop documents happ://open as starting the connection (fixed in 4.6.1).
/// Delivering a protocol activation is NOT evidence of an established VPN tunnel.
/// No guessed disconnect/status commands, simulated clicks, config edits or process kills.
/// </summary>
internal sealed class HappVpnConnector
{
    internal static readonly Uri ConnectUri = new("happ://open");
    readonly Func<bool> registered;
    readonly Action<Uri> activate;
    internal HappVpnConnector(Func<bool>? registered = null, Action<Uri>? activate = null)
    {
        this.registered = registered ?? IsRegistered;
        this.activate = activate ?? Activate;
    }
    internal HappVpnRequestResult RequestConnection()
    {
        try
        {
            if (!registered()) return HappVpnRequestResult.ClientMissing;
            activate(ConnectUri);
            return HappVpnRequestResult.Sent;
        }
        catch (Exception ex)
        {
            Log.Error("Could not send Happ connection request", ex);
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

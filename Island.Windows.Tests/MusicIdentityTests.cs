using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;
using Island;

internal static class MusicIdentityTests
{
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid Format; public uint Id; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    static void SetId(IPropertyStore store, string id)
    {
        IntPtr value = Marshal.AllocCoTaskMem(24);
        IntPtr text = Marshal.StringToCoTaskMemUni(id);
        try
        {
            for (int i = 0; i < 24; i++) Marshal.WriteByte(value, i, 0);
            Marshal.WriteInt16(value, 0, 31);
            Marshal.WriteIntPtr(value, 8, text);
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, value));
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally { Marshal.FreeCoTaskMem(text); Marshal.FreeCoTaskMem(value); }
    }
    internal static void Run(Action<bool, string> check)
    {
        string source = "Moku.Test.DesktopPlayer." + Guid.NewGuid().ToString("N");
        int pid = Environment.ProcessId;
        var window = new Window { Title = "", Width = 100, Height = 100, ShowInTaskbar = false, WindowState = WindowState.Minimized };
        try
        {
            window.Show();
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            Guid iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
            try { SetId(store, source); } finally { Marshal.ReleaseComObject(store); }
            check(MusicAppIdentity.WindowOwners(source, new HashSet<int> { pid }).Contains(pid),
                "explicit desktop AUMID resolves titleless minimized player window");
            check(!MusicAppIdentity.WindowOwners(source, new HashSet<int>()).Contains(pid),
                "desktop identity cannot escape allowed Windows session process set");
            check(MusicProcessResolver.Resolve(source) == MusicProcessResolver.Root(pid),
                "production source resolver finds custom AUMID not matching EXE or packaged identity");
            check(MusicAppIdentity.WindowOwners(source + ".Other", new HashSet<int> { pid }).Count == 0,
                "desktop AUMID identity comparison is exact rather than fuzzy");
        }
        finally { window.Close(); }

        // A player can be hidden in the tray with no usable HWND. Its registered
        // shortcut must still map the custom media AUMID to an exact executable.
        string shortcutSource = "Moku.Test.ShortcutPlayer." + Guid.NewGuid().ToString("N");
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MokuIdentityTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "Test player.lnk");
        object? shell = null, shortcut = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
            shortcut = ((dynamic)shell).CreateShortcut(file);
            ((dynamic)shortcut).TargetPath = Environment.ProcessPath!;
            ((dynamic)shortcut).Save();
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
            ((IPersistFile)link).Load(file, 0);
            SetId((IPropertyStore)link, shortcutSource);
            ((IPersistFile)link).Save(file, true);
            check(MusicAppIdentity.ShortcutExecutables(shortcutSource).Any(path => MusicSourcePolicy.MatchesExecutablePath(path, Environment.ProcessPath)),
                "registered shortcut resolves custom player AUMID to executable without launching it");
            check(MusicProcessResolver.Resolve(shortcutSource) == MusicProcessResolver.Root(pid),
                "production resolver finds shortcut-registered player with no matching window AUMID");
        }
        finally
        {
            foreach (object? item in new[] { link, shortcut, shell })
                if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

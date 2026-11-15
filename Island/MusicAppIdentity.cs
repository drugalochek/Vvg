using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Island;

/// <summary>Resolve explicit desktop/Electron AUMIDs, not just packaged-process IDs.
/// Reads window properties and registered shortcuts; never launches a shortcut or guesses by title.</summary>
internal static class MusicAppIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid Format;
        public uint Id;
        public PropertyKey(Guid format, uint id) { Format = format; Id = id; }
    }
    static PropertyKey AppIdKey => new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr id);
        void SetIDList(IntPtr id);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(IntPtr variant);
    delegate bool WindowCallback(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);

    static string? ReadId(IPropertyStore store)
    {
        IntPtr variant = Marshal.AllocCoTaskMem(24);
        for (int i = 0; i < 24; i++) Marshal.WriteByte(variant, i, 0);
        try
        {
            var key = AppIdKey;
            if (store.GetValue(ref key, variant) < 0 || Marshal.ReadInt16(variant) != 31) return null; // VT_LPWSTR
            return Marshal.PtrToStringUni(Marshal.ReadIntPtr(variant, 8));
        }
        finally { PropVariantClear(variant); Marshal.FreeCoTaskMem(variant); }
    }
    internal static HashSet<int> WindowOwners(string source, HashSet<int> allowed)
    {
        var owners = new HashSet<int>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!allowed.Contains((int)pid) || owners.Contains((int)pid)) return true;
            IPropertyStore? store = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                // Hidden and minimized windows count too. No preview/title/visibility filters.
                if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) >= 0 &&
                    source.Equals(ReadId(store), StringComparison.OrdinalIgnoreCase)) owners.Add((int)pid);
            }
            catch { }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
            return true;
        }, IntPtr.Zero);
        return owners;
    }

    static readonly object CacheGate = new();
    static readonly Dictionary<string, (DateTime Until, string[] Paths)> Cache = new(StringComparer.OrdinalIgnoreCase);
    internal static string[] ShortcutExecutables(string source)
    {
        lock (CacheGate)
        {
            if (Cache.TryGetValue(source, out var cached) && DateTime.UtcNow < cached.Until) return cached.Paths;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu,
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory };
            foreach (var folder in roots)
            {
                string root = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint };
                    foreach (var file in Directory.EnumerateFiles(root, "*.lnk", options).Take(2000))
                    {
                        object? linkObject = null;
                        try
                        {
                            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!;
                            linkObject = Activator.CreateInstance(type)!;
                            ((IPersistFile)linkObject).Load(file, 0);
                            if (!source.Equals(ReadId((IPropertyStore)linkObject), StringComparison.OrdinalIgnoreCase)) continue;
                            var link = (IShellLinkW)linkObject;
                            var target = new StringBuilder(32768);
                            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // raw path; no Resolve(), no network/launch
                            string path = Environment.ExpandEnvironmentVariables(target.ToString());
                            if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                            if (File.Exists(path) && !Path.GetFileName(path).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
                            // Squirrel shortcuts can point at Update.exe --processStart Player.exe.
                            var arguments = new StringBuilder(32768);
                            link.GetArguments(arguments, arguments.Capacity);
                            AddSquirrelTargets(paths, path, arguments.ToString());
                        }
                        catch { }
                        finally { if (linkObject != null && Marshal.IsComObject(linkObject)) Marshal.ReleaseComObject(linkObject); }
                    }
                }
                catch { }
            }
            if (Cache.Count >= 32) Cache.Clear();
            var result = paths.ToArray();
            Cache[source] = (DateTime.UtcNow.AddSeconds(30), result);
            return result;
        }
    }
    static void AddSquirrelTargets(HashSet<string> paths, string target, string arguments)
    {
        if (!Path.GetFileName(target).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) return;
        IntPtr argv = CommandLineToArgvW("shortcut " + arguments, out int count);
        if (argv == IntPtr.Zero) return;
        try
        {
            for (int i = 1; i + 1 < count; i++)
            {
                string? flag = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                if (!string.Equals(flag, "--processStart", StringComparison.OrdinalIgnoreCase)) continue;
                string? exe = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, (i + 1) * IntPtr.Size));
                if (string.IsNullOrEmpty(exe) || exe != Path.GetFileName(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
                string dir = Path.GetDirectoryName(target)!;
                string direct = Path.Combine(dir, exe);
                if (File.Exists(direct)) paths.Add(direct);
                foreach (string appDir in Directory.EnumerateDirectories(dir, "app-*", SearchOption.TopDirectoryOnly).Take(100))
                {
                    string path = Path.Combine(appDir, exe);
                    if (File.Exists(path)) paths.Add(path);
                }
                return;
            }
        }
        finally { LocalFree(argv); }
    }
}

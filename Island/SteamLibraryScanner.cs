using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Island;

internal sealed record SteamGame(string AppId, string Name);

internal static class SteamLibraryScanner
{
    public static List<SteamGame> FindInstalledGames()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRoot(roots, ReadSteamPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
        AddRoot(roots, ReadSteamPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
        AddRoot(roots, ReadSteamPath(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            libraries.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                var text = File.ReadAllText(vdf);
                foreach (Match match in Regex.Matches(text, @"""path""\s*""([^""]+)""", RegexOptions.IgnoreCase))
                    AddRoot(libraries, match.Groups[1].Value.Replace(@"\\", @"\"));
                foreach (Match match in Regex.Matches(text, @"""\d+""\s*""([^""]+)""", RegexOptions.IgnoreCase))
                    AddRoot(libraries, match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var games = new Dictionary<string, SteamGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var text = File.ReadAllText(manifest);
                    var id = Regex.Match(text, @"""appid""\s*""(\d+)""").Groups[1].Value;
                    var name = Regex.Match(text, @"""name""\s*""([^""]+)""").Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                        games[id] = new SteamGame(id, name);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static string? ReadSteamPath(RegistryKey hive, string keyPath, string valueName)
    {
        try { using var key = hive.OpenSubKey(keyPath); return key?.GetValue(valueName) as string; }
        catch (System.Security.SecurityException) { return null; }
    }

    static void AddRoot(HashSet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (Directory.Exists(normalized)) roots.Add(Path.GetFullPath(normalized));
    }
}

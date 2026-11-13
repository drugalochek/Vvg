namespace Island;

/// <summary>Library folders are virtual containers, not directories on disk.</summary>
internal static class LibraryPolicies
{
    public static string FolderKey(string name) => "folder:" + name;
    public static string FileKey(string path) => "file:" + path;
    public static string CleanName(string? name) => new((name ?? "").Trim().Where(c => !char.IsControl(c)).Take(40).ToArray());

    public static bool CreateFolder(Settings settings, string? proposed)
    {
        string name = CleanName(proposed);
        if (name.Length == 0 || settings.AppFolders.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase))) return false;
        settings.AppFolders.Add(name); settings.RootTileOrder.Add(FolderKey(name)); return true;
    }
    public static bool RenameFolder(Settings settings, string oldName, string? proposed)
    {
        string name = CleanName(proposed);
        int index = settings.AppFolders.FindIndex(f => string.Equals(f, oldName, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || name.Length == 0 || settings.AppFolders.Where((_, i) => i != index).Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase))) return false;
        settings.AppFolders[index] = name;
        foreach (var app in settings.Apps.Where(a => string.Equals(a.Folder, oldName, StringComparison.OrdinalIgnoreCase))) app.Folder = name;
        for (int i = 0; i < settings.RootTileOrder.Count; i++)
            if (string.Equals(settings.RootTileOrder[i], FolderKey(oldName), StringComparison.OrdinalIgnoreCase)) settings.RootTileOrder[i] = FolderKey(name);
        return true;
    }
    public static void RemoveFolder(Settings settings, string name)
    {
        foreach (var app in settings.Apps.Where(a => string.Equals(a.Folder, name, StringComparison.OrdinalIgnoreCase))) app.Folder = "";
        settings.AppFolders.RemoveAll(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        settings.RootTileOrder.RemoveAll(k => string.Equals(k, FolderKey(name), StringComparison.OrdinalIgnoreCase));
    }
    public static bool MoveFile(Settings settings, string path, string folder)
    {
        var app = settings.Apps.FirstOrDefault(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
        if (app == null) return false;
        string? destination = folder.Length == 0 ? "" : settings.AppFolders.FirstOrDefault(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        if (destination == null) return false;
        app.Folder = destination; return true;
    }
    public static List<string> OrderedRootKeys(Settings settings)
    {
        var all = settings.AppFolders.Select(FolderKey).Concat(settings.Apps.Where(a => a.Folder.Length == 0).Select(a => FileKey(a.Path))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var valid = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);
        return settings.RootTileOrder.Where(valid.Contains).Concat(all).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

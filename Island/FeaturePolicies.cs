namespace Island;

/// <summary>Platform-independent policies shared by the UI and regression checks.</summary>
internal static class FeaturePolicies
{
    public static (int X, int Y) TileSpan(string? value) => value switch
    { "2x1" => (2, 1), "2x2" => (2, 2), _ => (1, 1) };

    public static string HomeView(bool remember, string? last, string? defaultView) =>
        (remember ? last : defaultView) == "Windows" ? "Windows" : "Clipboard";

    public static bool LaunchPending(DateTime now, DateTime started) => now - started < TimeSpan.FromSeconds(30);

    public static bool Overlaps(int l, int t, int r, int b, int il, int it, int ir, int ib) =>
        l < ir && r > il && t < ib && b > it;

    public static IReadOnlyList<(int X, int Y, int W, int H)> PackTiles(IEnumerable<string> sizes, int columns)
    {
        columns = Math.Max(1, columns);
        var used = new HashSet<(int, int)>();
        var result = new List<(int, int, int, int)>();
        foreach (var size in sizes)
        {
            var (w, h) = TileSpan(size); w = Math.Min(w, columns);
            int x = 0, y = 0;
            while (true)
            {
                bool free = x + w <= columns;
                for (int dy = 0; dy < h && free; dy++)
                    for (int dx = 0; dx < w; dx++)
                        if (used.Contains((x + dx, y + dy))) { free = false; break; }
                if (free) break;
                if (++x >= columns) { x = 0; y++; }
            }
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++) used.Add((x + dx, y + dy));
            result.Add((x, y, w, h));
        }
        return result;
    }
}

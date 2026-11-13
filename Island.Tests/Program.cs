using Island;
using System.Text.Json;

int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception("FAIL: " + name); }
Check(FeaturePolicies.TileSpan("1x1") == (1,1), "1x1");
Check(FeaturePolicies.TileSpan("2x1") == (2,1), "2x1");
Check(FeaturePolicies.TileSpan("2x2") == (2,2), "2x2");
Check(FeaturePolicies.TileSpan("bad") == (1,1), "invalid span");
Check(FeaturePolicies.HomeView(false,"Windows","Clipboard") == "Clipboard", "default clipboard");
Check(FeaturePolicies.HomeView(false,"Clipboard","Windows") == "Windows", "default windows");
Check(FeaturePolicies.HomeView(true,"Windows","Clipboard") == "Windows", "remember windows");
Check(FeaturePolicies.HomeView(true,"Clipboard","Windows") == "Clipboard", "remember clipboard");
Check(FeaturePolicies.HomeView(true,null,"Windows") == "Clipboard", "null last safe");
var now = DateTime.UtcNow;
Check(FeaturePolicies.LaunchPending(now,now), "immediate debounce");
Check(FeaturePolicies.LaunchPending(now,now.AddSeconds(-29)), "slow launch debounce");
Check(!FeaturePolicies.LaunchPending(now,now.AddSeconds(-30)), "timeout unlock");
Check(FeaturePolicies.Overlaps(0,0,100,100,50,50,150,150), "normal window overlap");
Check(!FeaturePolicies.Overlaps(0,0,100,100,100,0,200,100), "touching edge not overlap");
Check(!FeaturePolicies.Overlaps(0,0,100,100,101,101,200,200), "separate windows");
var s = new Settings();
Check(s.Hotkey == "Alt+Space", "default Alt Space");
Check(!s.TransparentOverNormalWindows, "normal overlap opt in");
Check(s.GameMode, "existing game mode retained");
Check(s.HomeStartupView == "Clipboard", "default Home clipboard");
Check(s.FullscreenOpacity == 35 && s.Scale == 100 && s.Theme == "Midnight", "visual defaults unchanged");
s.Hotkey = "CtrlAltSpace"; s.Normalize(); Check(s.Hotkey == "Ctrl+Alt+Space", "legacy bind preserved");
s.Hotkey = "F8"; s.Normalize(); Check(s.Hotkey == "F8", "single key preserved");
s.Hotkey = "Off"; s.Normalize(); Check(s.Hotkey == "Off", "disabled bind");
s.HomeStartupView = "bad"; s.LastHomeView = "bad"; s.Normalize(); Check(s.HomeStartupView == "Clipboard" && s.LastHomeView == "Clipboard", "invalid home normalized");
s.HiddenSteamGames = new() {"570","570","abc","730"}; s.Normalize(); Check(s.HiddenSteamGames.SequenceEqual(new[]{"570","730"}), "hidden ids normalized");
s.AppTileSizes = new() { ["app"] = "2x2", ["broken"] = "3x3" }; s.Normalize(); Check(s.AppTileSizes.Count == 1 && s.AppTileSizes["app"] == "2x2", "invalid sizes removed");
s.HiddenSteamNames["570"] = "Dota 2";
s.LastHomeView = "Windows";
var copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!; copy.Normalize();
Check(copy.HiddenSteamGames.Contains("570") && copy.HiddenSteamNames["570"] == "Dota 2", "hidden games persist");
Check(copy.AppTileSizes["app"] == "2x2" && copy.LastHomeView == "Windows", "tile and home persistence");
var random = new Random(2120);
for (int cols = 1; cols <= 8; cols++)
 for (int run = 0; run < 50; run++)
 {
    var input = Enumerable.Range(0,60).Select(_ => new[]{"1x1","2x1","2x2"}[random.Next(3)]).ToArray();
    var layout = FeaturePolicies.PackTiles(input,cols); var occupied = new HashSet<(int,int)>();
    bool valid = layout.Count == input.Length;
    foreach(var tile in layout)
    {
        valid &= tile.X >= 0 && tile.Y >= 0 && tile.X + tile.W <= cols;
        for(int y=0;y<tile.H;y++) for(int x=0;x<tile.W;x++) valid &= occupied.Add((tile.X+x,tile.Y+y));
    }
    Check(valid,$"60 mixed tiles / {cols} columns / run {run}");
 }
var wrap = FeaturePolicies.PackTiles(new[]{"1x1","1x1","2x2"},3);
Check(wrap[2].Y == 1 && wrap[2].X == 0, "wide tile wraps at edge");
Console.WriteLine($"PASS: {checks} checks (policies, settings migration/persistence, 400 randomized packing cases). Windows integration still requires Windows.");

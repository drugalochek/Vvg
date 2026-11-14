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

// 2.12.1 contracts: explicit folders, mixed root order, manual Steam list, launcher modes.
var library = new Settings();
Check(LibraryPolicies.CreateFolder(library, "Work"), "create empty folder");
Check(LibraryPolicies.CreateFolder(library, "Games"), "create second folder");
Check(!LibraryPolicies.CreateFolder(library, " work "), "duplicate folder rejected case-insensitively");
Check(!LibraryPolicies.CreateFolder(library, " \t "), "blank folder rejected");
library.Apps.Add(new AppShortcut { Path = "/files/test.png", Name = "Image" });
library.Apps.Add(new AppShortcut { Path = "/files/general.cmd", Name = "Script" });
Check(LibraryPolicies.MoveFile(library, "/files/test.png", "Work"), "drag file into folder");
Check(library.Apps[0].Folder == "Work", "file moved into chosen folder");
Check(!LibraryPolicies.MoveFile(library, "/files/test.png", "Missing"), "unknown destination rejected");
library.RootTileOrder = new() {LibraryPolicies.FileKey("/files/general.cmd"), LibraryPolicies.FolderKey("Games"), LibraryPolicies.FolderKey("Work")};
Check(LibraryPolicies.OrderedRootKeys(library).SequenceEqual(library.RootTileOrder), "mixed folder/file drag order");
Check(!LibraryPolicies.RenameFolder(library, "Work", "Games"), "rename collision rejected");
Check(LibraryPolicies.RenameFolder(library, "Work", "Project"), "rename folder");
Check(library.Apps[0].Folder == "Project", "rename updates contained files");
Check(library.RootTileOrder[2] == LibraryPolicies.FolderKey("Project"), "rename retains root order");
Check(LibraryPolicies.MoveFile(library, "/files/test.png", ""), "folder cross moves file to root");
Check(library.Apps.Count == 2 && library.Apps[0].Folder.Length == 0, "folder cross never removes file");
Check(library.AppFolders.Contains("Project"), "empty folder survives removing last file");
var reloaded = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(library))!; reloaded.Normalize();
Check(reloaded.AppFolders.Contains("Project"), "empty folder survives restart");
Check(reloaded.RootTileOrder.Take(3).SequenceEqual(library.RootTileOrder), "mixed order survives restart");
LibraryPolicies.MoveFile(reloaded, "/files/test.png", "Project");
LibraryPolicies.RemoveFolder(reloaded, "Project");
Check(!reloaded.AppFolders.Contains("Project") && reloaded.Apps.Count == 2 && reloaded.Apps[0].Folder.Length == 0, "remove folder keeps all files at root");
var legacy = new Settings { Apps = new() { new() { Path="legacy.exe", Name="Old app", Folder="Old folder" } }, AppFolders = null!, RootTileOrder = null! };
legacy.Normalize(); Check(legacy.AppFolders.Contains("Old folder"), "migrate implicit legacy folder");
Check(new Settings().SteamSuggestionsExpanded, "Steam list expanded initially");
legacy.SteamSuggestionsExpanded = false;
Check(!JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(legacy))!.SteamSuggestionsExpanded, "Steam collapsed state persists");
Check(FileLaunchPolicy.CanElevate("test.exe") && FileLaunchPolicy.CanElevate("test.CMD") && FileLaunchPolicy.CanElevate("test.bat"), "exe and batch support UAC");
Check(FileLaunchPolicy.CanElevate("test.ps1") && FileLaunchPolicy.CanElevate("test.msi"), "PowerShell and installer support UAC");
Check(!FileLaunchPolicy.CanElevate("photo.png") && !FileLaunchPolicy.CanElevate("library.dll"), "no invented DLL entrypoint or elevated image viewer");
Check(!FileLaunchPolicy.IsNativeApplication("test.bat") && FileLaunchPolicy.IsNativeApplication("test.lnk"), "scripts not mistaken for native executable identity");
string testDirectory = Path.Combine(Path.GetTempPath(), "Moku assets");
string systemDirectory = Path.Combine(Path.GetTempPath(), "System");
string pngPath = Path.Combine(testDirectory,"photo.png");
string batchPath = Path.Combine(testDirectory,"general.cmd");
string psPath = Path.Combine(testDirectory,"start.ps1");
string exePath = Path.Combine(testDirectory,"app.exe");
var imagePlan=FileLaunchPolicy.Create(pngPath,false,systemDirectory);
Check(imagePlan.UseShellExecute && imagePlan.FileName == pngPath && imagePlan.WorkingDirectory == testDirectory, "documents use default association and local working folder");
var batchPlan=FileLaunchPolicy.Create(batchPath,true,systemDirectory);
Check(batchPlan.FileName.EndsWith("cmd.exe") && batchPlan.Verb == "runas", "batch elevation via cmd and UAC");
Check(batchPlan.WorkingDirectory == testDirectory && batchPlan.Arguments == "/d /s /c \"\"" + batchPath + "\"\"", "batch paths with spaces and relative assets");
var psPlan=FileLaunchPolicy.Create(psPath,false,systemDirectory);
Check(psPlan.FileName.EndsWith("powershell.exe") && psPlan.Arguments == "-NoProfile -File \"" + psPath + "\"" && !psPlan.Arguments.Contains("Bypass"), "PowerShell honors execution policy");
Check(FileLaunchPolicy.Create(exePath,false,systemDirectory).Verb.Length == 0, "normal click never requests elevation");
Check(FileLaunchPolicy.Create(exePath,true,systemDirectory).Verb == "runas", "explicit administrator command");
bool dllRejected=false;try { FileLaunchPolicy.Create("library.dll",true,"/system"); }catch(NotSupportedException){dllRejected=true;}
Check(dllRejected,"DLL stays a file, not a guessed rundll32 command");
var xdoc=System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory,"MainWindow.xaml"));
System.Xml.Linq.XNamespace xns="http://schemas.microsoft.com/winfx/2006/xaml";
System.Xml.Linq.XNamespace ui="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
bool HasName(string n)=>xdoc.Descendants().Any(e=>(string?)e.Attribute(xns+"Name")==n);
Check(!HasName("HomeClipboardModeBtn") && !HasName("HomeWindowsModeBtn"),"Home has no view switches");
Check(!HasName("MinimizeAllBtn") && !HasName("RestoreAllBtn"),"Home has no minimize/restore buttons");
Check(HasName("RecentList") && HasName("HomeWindowsScroll"),"both Home modes remain available via settings");
Check(HasName("CreateFolderButton"),"visible create-folder button");
var steamExpander=xdoc.Descendants(ui+"Expander").Single(e=>(string?)e.Attribute(xns+"Name")=="SteamSuggestionsExpander");
Check(steamExpander.Descendants().Any(e=>(string?)e.Attribute(xns+"Name")=="SteamGamesGrid"),"Steam suggestions inside direct expander");
var tileStyle=xdoc.Descendants(ui+"Style").Single(e=>(string?)e.Attribute(xns+"Key")=="TileBtn");
Check(!tileStyle.Descendants(ui+"DoubleAnimation").Any() && tileStyle.Descendants(ui+"MultiTrigger").Count()==2,"press/hover cannot leave animation clocks or sticky glow");

var popups=System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory,"PopupStyles.xaml"));
var menuStyle=popups.Descendants(ui+"Style").Single(e=>(string?)e.Attribute(xns+"Key")=="MokuContextMenu");
Check(menuStyle.Descendants(ui+"ControlTemplate").Any(),"context menu replaces system white chrome");
Check(menuStyle.Descendants(ui+"Border").Any(e=>(string?)e.Attribute("CornerRadius")=="{DynamicResource MokuPopupRadius}"),"menu uses shared live corner radius");
var itemStyle=popups.Descendants(ui+"Style").Single(e=>(string?)e.Attribute(xns+"Key")=="MokuMenuItem");
Check(itemStyle.Descendants(ui+"Popup").Single().Descendants(ui+"Border").Any(e=>(string?)e.Attribute("CornerRadius")=="{DynamicResource MokuPopupRadius}"),"submenus share custom rounded theme");
Check(popups.Descendants(ui+"Style").Any(e=>(string?)e.Attribute("TargetType")=="MenuItem" && e.Attribute(xns+"Key")==null && (string?)e.Attribute("BasedOn")=="{StaticResource MokuMenuItem}"),"implicit menu item style covers submenus without styling separators as MenuItem");
var previewStyle=popups.Descendants(ui+"Style").Single(e=>(string?)e.Attribute(xns+"Key")=="MokuWindowTooltip");
Check(previewStyle.Descendants(ui+"Border").Any(e=>(string?)e.Attribute("CornerRadius")=="{DynamicResource MokuPopupRadius}"),"window previews retain themed roundable popup");

// Home quick actions must resolve visible apps even when the OS uses a different host.
Check(QuickAccessPolicy.MatchesWindow("notepad.exe","Notepad","Notepad"),"Home Notepad, including packaged Windows 11 app");
Check(QuickAccessPolicy.MatchesWindow("calc.exe","CalculatorApp","ApplicationFrameWindow"),"Home Calculator resolved behind application frame host");
Check(QuickAccessPolicy.MatchesWindow("calc.exe","Calculator","WinUIDesktopWin32WindowClass"),"Home modern Calculator alias");
Check(!QuickAccessPolicy.MatchesWindow("calc.exe","ApplicationFrameHost","ApplicationFrameWindow"),"do not confuse unrelated UWP frame with Calculator");
Check(QuickAccessPolicy.MatchesWindow("powershell.exe","WindowsTerminal","CASCADIA_HOSTING_WINDOW_CLASS"),"Home reuses Windows Terminal host");
Check(QuickAccessPolicy.MatchesWindow("powershell.exe","pwsh","ConsoleWindowClass"),"Home recognizes PowerShell 7");
Check(!QuickAccessPolicy.MatchesWindow("powershell.exe","conhost","ConsoleWindowClass"),"unmapped console host cannot steal another app console");
Check(QuickAccessPolicy.MatchesWindow("explorer.exe","explorer","CabinetWClass"),"Home restores real Explorer folder");
Check(!QuickAccessPolicy.MatchesWindow("explorer.exe","explorer","Progman"),"desktop shell is not an open folder");
Check(QuickAccessPolicy.MatchesWindow("taskmgr.exe","Taskmgr","TaskManagerWindow"),"Home Task Manager case-insensitive process");
Check(QuickAccessPolicy.IsTerminal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"),"Windows command paths normalize in portable tests");
Check(!QuickAccessPolicy.MatchesWindow("other.exe","Notepad","Notepad"),"unrelated quick command does not activate arbitrary app");

Check(MusicSourcePolicy.Matches("Spotify.exe","spotify",null), "music executable source exact match");
Check(MusicSourcePolicy.Matches("SpotifyAB.SpotifyMusic_pkg!Spotify","Spotify","SpotifyAB.SpotifyMusic_pkg!Spotify"), "packaged music AUMID match");
Check(!MusicSourcePolicy.Matches("Spotify.exe","Discord",null), "Discord cannot replace player source");
Check(!MusicSourcePolicy.Matches(null,"Discord",null), "unknown media source does not select any output");
Check(!MusicSourcePolicy.Matches("chrome.exe","Discord",null), "browser player excludes Discord app");
Check(!MusicSourcePolicy.Matches("spotify","spotify-helper",null), "no fuzzy process name match");
Check(MusicSourcePolicy.Matches(@"C:\Apps\Player.exe","Player",null), "full source executable path normalizes");
string sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Island"));
if (Directory.Exists(sourceRoot))
{
    string mainCode=File.ReadAllText(Path.Combine(sourceRoot,"MainWindow.xaml.cs"));
    string windowsCode=File.ReadAllText(Path.Combine(sourceRoot,"MainWindow.Windows.cs"));
    string analyzerCode=File.ReadAllText(Path.Combine(sourceRoot,"AudioBeatAnalyzer.cs"));
    string quickRoute=windowsCode[windowsCode.IndexOf("bool TryActivateHomeQuickAction")..];
    Check(quickRoute.Contains("QuickWindowResolver.Find(command)") && !quickRoute.Contains("GetOpenWindows()"), "Home activation bypasses preview window filters");
    Check(mainCode.Contains("_audio.SetActive(np?.Playing == true, np?.SourceAppId)"), "audio capture follows current media source identity");
    Check(analyzerCode.Contains("new ProcessLoopbackCapture(pid)") && !analyzerCode.Contains("new WasapiLoopbackCapture"), "no system-mix capture or fallback");
    Check(mainCode.Contains("ProcessStartInfo(\"calculator:\")"), "suspended Calculator uses registered app activation fallback");
}
Console.WriteLine($"PASS: {checks} checks (folders, file launch plans, Home/Steam/button UI contracts, settings and 400 randomized packing cases). Windows integration still requires Windows.");

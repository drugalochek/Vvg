using Island;
using System.Text.Json;

int checks = 0;
bool Throws(Action a) { try { a(); return false; } catch { return true; } }
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception("FAIL: " + name); }
Check(SystemPanelPolicy.Cpu(100,200,100,120,260,140) == 80, "system CPU delta excludes idle");
Check(SystemPanelPolicy.Cpu(0,0,0,0,0,0) == null, "system CPU zero interval unavailable");
Check(SystemPanelPolicy.Cpu(10,20,30,1,20,30) == null, "system CPU counter reset unavailable");
Check(SystemPanelPolicy.Cpu(0,0,0,100,20,20) == 0, "system CPU clamps inconsistent counters");
Check(SystemPanelPolicy.Memory(16,8) == 50, "physical memory percentage");
Check(SystemPanelPolicy.Memory(0,0) == null && SystemPanelPolicy.Memory(8,16) == null, "invalid memory unavailable");
Check(SystemPanelPolicy.Battery(128,100) == null, "desktop has no fake battery");
Check(!SystemPanelPolicy.Poll(false,true) && !SystemPanelPolicy.Poll(true,false) && SystemPanelPolicy.Poll(true,true), "system polling only while expanded and visible");
Check(new[]{"sleep","restart","shutdown"}.All(SystemPanelPolicy.NeedsConfirmation), "power operations require confirmation");
Check(!SystemPanelPolicy.NeedsConfirmation("internet") && !SystemPanelPolicy.NeedsConfirmation("vpn") && !SystemPanelPolicy.NeedsConfirmation("microphone"), "switches do not prompt");
Check(SystemPanelPolicy.Toggles.SequenceEqual(new[]{"internet","bluetooth","vpn","microphone"}), "System tab exposes only the four real switches");
Check(SystemPanelPolicy.PowerActions.SequenceEqual(new[]{"sleep","restart","shutdown"}), "System tab exposes only sleep, restart and shut down");
Check(SystemPanelPolicy.FallbackUri("internet") == "ms-settings:network-status" && SystemPanelPolicy.FallbackUri("wifi") == null && SystemPanelPolicy.FallbackUri("vpn") == "ms-settings:network-vpn", "refused toggles fall back to the matching Windows page");
Check(SystemPanelPolicy.IsPhysicalNetwork("Ethernet","Ethernet","Realtek PCIe GbE Family Controller") && SystemPanelPolicy.IsPhysicalNetwork("Wireless80211","Wi-Fi","Intel(R) Wi-Fi 6 AX201"), "cable and Wi-Fi adapters count as internet");
Check(!SystemPanelPolicy.IsPhysicalNetwork("Ethernet","vEthernet (Default Switch)","Hyper-V Virtual Ethernet Adapter") && !SystemPanelPolicy.IsPhysicalNetwork("Unknown","happ-tun","Wintun Userspace Tunnel") && !SystemPanelPolicy.IsPhysicalNetwork("Ethernet","Bluetooth Network Connection","Bluetooth Device (PAN)") && !SystemPanelPolicy.IsPhysicalNetwork("Wireless80211","Local Area Connection* 1","Microsoft Wi-Fi Direct Virtual Adapter") && !SystemPanelPolicy.IsPhysicalNetwork(null,"x","y"), "virtual, VPN and Bluetooth adapters are never switched off");
Check(SystemPanelPolicy.InternetState(true,false) == true && SystemPanelPolicy.InternetState(false,true) == false && SystemPanelPolicy.InternetState(false,false) == null, "internet state: on / off by Moku / unavailable");
Check(SystemPanelPolicy.InternetScript(new[]{"Ethernet","Wi-Fi"}, false).Contains("Disable-NetAdapter -Name @('Ethernet','Wi-Fi')") && SystemPanelPolicy.InternetScript(new[]{"Bob's LAN"}, true).Contains("Enable-NetAdapter -Name @('Bob''s LAN')"), "internet script switches exactly the named adapters with quoting");
Check(SystemPanelPolicy.InternetScript(Array.Empty<string>(), true).Contains("Get-NetAdapter -Physical") && Throws(() => SystemPanelPolicy.InternetScript(Array.Empty<string>(), false)), "internet restore fallback; nothing to disable is an error");
{ string root = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(root, "Island", "MainWindow.Custom.cs")) && Directory.GetParent(root) is { } up) root = up.FullName;
  string custom = File.ReadAllText(Path.Combine(root, "Island", "MainWindow.Custom.cs"));
  Check(!custom.Contains("Stopwatch", StringComparison.OrdinalIgnoreCase) && !custom.Contains("секундомер", StringComparison.OrdinalIgnoreCase) && custom.Contains("shell:RecycleBinFolder") && custom.Contains("SHEmptyRecycleBin"), "Home: stopwatch replaced by the Recycle Bin tile"); }
Check(SystemPanelPolicy.FallbackUri("taskmanager") == null && SystemPanelPolicy.FallbackUri("arbitrary") == null, "removed / unknown actions have no URI");
Check(SystemPanelPolicy.GpuLoad(new[]{
    ("pid_1_luid_0x00000000_0x0000D1B4_phys_0_eng_0_engtype_3D", 30.0),
    ("pid_2_luid_0x00000000_0x0000D1B4_phys_0_eng_0_engtype_3D", 11.0),
    ("pid_2_luid_0x00000000_0x0000D1B4_phys_0_eng_3_engtype_VideoDecode", 20.0)}) == 41, "GPU load sums processes per engine and takes the busiest engine");
Check(SystemPanelPolicy.GpuLoad(new[]{
    ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 70.0),
    ("pid_1_luid_0x00000000_0x0000BBBB_phys_0_eng_0_engtype_3D", 70.0)}) == 70, "two adapters are not added together");
Check(SystemPanelPolicy.GpuLoad(new[]{("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 80.0),("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 60.0)}) == 100, "GPU load clamps at 100 %");
Check(SystemPanelPolicy.GpuLoad(Array.Empty<(string,double)>()) == null, "no GPU samples is unavailable, not 0 %");
Check(SystemPanelPolicy.GpuName("NVIDIA GeForce RTX 4060") == "GeForce RTX 4060", "GPU name drops vendor prefix");
Check(SystemPanelPolicy.GpuName("Intel(R) UHD Graphics 630") == "Intel UHD Graphics 630", "GPU name drops trademark marks");
Check(SystemPanelPolicy.GpuName("Microsoft Basic Render Driver") == null && SystemPanelPolicy.GpuName(null) == null, "software adapter is not a GPU");
Check(SystemPanelPolicy.LooksLikeTunnel("happ-tun","Wintun Userspace Tunnel") && SystemPanelPolicy.LooksLikeTunnel("xray0","Xray TUN"), "Happ tunnel adapters recognised");
Check(!SystemPanelPolicy.LooksLikeTunnel("Ethernet","Realtek PCIe GbE") && !SystemPanelPolicy.LooksLikeTunnel("Teredo Tunneling Pseudo-Interface","Teredo"), "ordinary and Teredo adapters are not a VPN");
Check(SystemPanelPolicy.IsLocalProxy(1,"127.0.0.1:10809") && SystemPanelPolicy.IsLocalProxy(1,"http=localhost:2080;https=localhost:2080"), "Happ system proxy recognised");
Check(!SystemPanelPolicy.IsLocalProxy(0,"127.0.0.1:10809") && !SystemPanelPolicy.IsLocalProxy(1,"proxy.corp.local:8080"), "disabled / corporate proxy is not Happ");
Check(SystemPanelPolicy.HappConnected(true,true,false) && SystemPanelPolicy.HappConnected(true,false,true), "Happ on through tunnel or proxy");
Check(!SystemPanelPolicy.HappConnected(false,true,true), "closed Happ is never reported as connected");
Check(SystemPanelPolicy.PhonebookEntries("[Work VPN]\r\nType=2\r\n\r\n[Home]\r\nType=2\r\n[work vpn]\r\n").SequenceEqual(new[]{"Work VPN","Home"}), "Windows VPN profiles read from phonebook");
Check(SystemPanelPolicy.PhonebookEntries(null).Count == 0, "missing phonebook has no profiles");
Check(SystemPanelPolicy.PendingExpired(DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(13)) && !SystemPanelPolicy.PendingExpired(DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(3)), "pending switch state expires");
Check(IslandSwipePolicy.HideDirection("Top") == -1 && IslandSwipePolicy.HideDirection("Bottom") == 1 && IslandSwipePolicy.HideDirection("Center") == -1, "island hides toward its screen edge");
Check(IslandSwipePolicy.Resolve(-30,-1,false) == SwipeAction.Stow && IslandSwipePolicy.Resolve(-10,-1,false) == SwipeAction.None, "swipe up hides top island after a deliberate distance");
Check(IslandSwipePolicy.Resolve(30,-1,true) == SwipeAction.Restore && IslandSwipePolicy.Resolve(-30,-1,true) == SwipeAction.None, "swipe down restores, swiping further up does nothing");
Check(IslandSwipePolicy.Resolve(30,-1,false) == SwipeAction.None, "swiping down on a visible top island does not hide it");
Check(IslandSwipePolicy.Resolve(30,1,false) == SwipeAction.Stow && IslandSwipePolicy.Resolve(-30,1,true) == SwipeAction.Restore, "bottom island mirrors the gesture");
Check(new Settings().VpnClient == "Windows", "existing installations keep Windows VPN action");
Check(SystemPanelPolicy.VpnClient("happ") == "Happ" && SystemPanelPolicy.VpnClient("Happ") == "Happ", "Happ client choice normalizes");
Check(SystemPanelPolicy.VpnClient(null) == "Windows" && SystemPanelPolicy.VpnClient("unknown") == "Windows", "unknown VPN client falls back safely");
var happSettings = new Settings { VpnClient = "Happ" }; happSettings.Normalize();
var persistedHapp = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(happSettings))!; persistedHapp.Normalize();
Check(persistedHapp.VpnClient == "Happ", "Happ client choice persists");
happSettings.VpnClient = "bad"; happSettings.Normalize(); Check(happSettings.VpnClient == "Windows", "invalid saved client normalized");
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
Check(AudioLevelPolicy.UseSessionMeter(false, false), "Windows 10 selects session level meter");
Check(!AudioLevelPolicy.UseSessionMeter(true, false), "modern Windows keeps isolated spectrum");
Check(AudioLevelPolicy.UseSessionMeter(true, true), "process loopback failure uses isolated level meter");
Check(AudioLevelPolicy.AcceptSession(42,42,false), "only selected player session accepted");
Check(!AudioLevelPolicy.AcceptSession(42,43,false), "Discord or other process excluded");
Check(!AudioLevelPolicy.AcceptSession(42,42,true), "system sounds excluded");
Check(!AudioLevelPolicy.AcceptSession(0,0,false), "unknown source cannot select system audio");
Check(!AudioLevelPolicy.AcceptSession(42,0,false), "unresolved session excluded");
Check(AudioLevelPolicy.PeakActivity(0)==0, "silent player is silent");
Check(AudioLevelPolicy.PeakActivity(0.25f)==0.5f, "quiet player level made visible");
Check(AudioLevelPolicy.PeakActivity(-1)==0 && AudioLevelPolicy.PeakActivity(2)==1, "peak clamp");
Check(AudioLevelPolicy.PeakActivity(float.NaN)==0 && AudioLevelPolicy.PeakActivity(float.PositiveInfinity)==0, "invalid samples remain finite");
if (Directory.Exists(sourceRoot))
{
    string meter = File.ReadAllText(Path.Combine(sourceRoot,"SessionPeakMonitor.cs"));
    Check(meter.Contains("session.AudioMeterInformation.MasterPeakValue") && !meter.Contains("device.AudioMeterInformation"), "meter reads player session not endpoint");
    Check(meter.Contains("AudioLevelPolicy.AcceptSession") && meter.Contains("SameProcess()"), "session identity and PID reuse guarded");
}
Check(MusicSourcePolicy.Matches("ru.yandex.desktop.music", "YandexMusic", null), "desktop Yandex media AUMID resolves actual EXE name without packaged identity");
Check(MusicSourcePolicy.Matches("RU.YANDEX.DESKTOP.MUSIC", "yandexmusic", null), "Yandex desktop alias is case insensitive");
Check(MusicSourcePolicy.Matches("ru.yandex.desktop.music", "Яндекс Музыка", null), "localized Yandex desktop executable alias resolves");
Check(!MusicSourcePolicy.Matches("ru.yandex.desktop.music", "browser", null), "Yandex Music identity never selects Yandex Browser");
Check(!MusicSourcePolicy.Matches("ru.yandex.desktop.music", "YandexMusicHelper", null), "Yandex alias never uses fuzzy helper matching");
Check(!MusicSourcePolicy.Matches("unrelated.ru.yandex.desktop.music", "YandexMusic", null), "desktop alias requires complete exact media identity");
Check(!MusicSourcePolicy.Matches("Spotify.exe", "YandexMusic", null), "Yandex alias cannot replace a different selected player");
Check(MusicSourcePolicy.SelectRoot(new[] { 42, 42 }, new[] { 42, 99 }) == 42, "duplicate player processes collapse to one root");
Check(MusicSourcePolicy.SelectRoot(new[] { 42, 43 }, new[] { 43, 99 }) == 43, "only matching player audio root is selected");
Check(MusicSourcePolicy.SelectRoot(new[] { 42, 43 }, new[] { 42, 43 }) == 0, "two distinct matching player trees remain ambiguous");
Check(MusicSourcePolicy.SelectRoot(new[] { 42, 43 }, new[] { 99 }) == 0, "unrelated loud audio never resolves ambiguous music source");
Check(MusicSourcePolicy.SelectRoot(Array.Empty<int>(), new[] { 99 }) == 0, "no player match means silence even with other audio");
Check(MusicSourcePolicy.SelectRoot(new[] { 42 }, Array.Empty<int>()) == 42, "known player can resolve before it opens an audio session");
Check(MusicSourcePolicy.SelectRoot(new[] { 0, -1 }, new[] { 0, -1 }) == 0, "invalid process IDs are never accepted");
Check(AudioLevelPolicy.ShouldFallBack(true, 0.75), "sustained player sound with silent PCM activates automatic fallback");
Check(!AudioLevelPolicy.ShouldFallBack(false, 5), "valid PCM is never replaced merely because player meter is active");
Check(!AudioLevelPolicy.ShouldFallBack(true, 0.2), "short sound/meter spikes do not switch backend");
Check(!AudioLevelPolicy.ShouldFallBack(true, 0), "a paused or silent track does not trigger fallback");
Check(MusicSourcePolicy.MatchesExecutablePath(@"C:\Music\Яндекс Музыка.exe", @"c:/music/Яндекс Музыка.exe"), "registered Unicode player path matches exact executable");
Check(!MusicSourcePolicy.MatchesExecutablePath(@"C:\Music\Player.exe", @"C:\Other\Player.exe"), "same filename in different directory is not a player identity");
Check(!MusicSourcePolicy.MatchesExecutablePath(null, @"C:\Discord.exe"), "missing registered target cannot match Discord");
Check(!MusicSourcePolicy.Matches("ru.yandex.desktop.music", "Discord", null), "custom Yandex identity cannot select unrelated voice app");
foreach (var oldMode in new[] { "Windows10", "Windows11", "broken" })
{
    var migrated = JsonSerializer.Deserialize<Settings>("{\"MusicWave\":true,\"WaveHeight\":123,\"AudioCaptureMode\":\"" + oldMode + "\"}")!;
    migrated.Normalize();
    string json = JsonSerializer.Serialize(migrated);
    Check(migrated.MusicWave && migrated.WaveHeight == 123, "legacy mode migration preserves wave preferences: " + oldMode);
    Check(!json.Contains("AudioCaptureMode"), "obsolete Windows selector is no longer saved: " + oldMode);
}
if (Directory.Exists(sourceRoot))
{
    string extra = File.ReadAllText(Path.Combine(sourceRoot,"MainWindow.SettingsExtra.cs"));
    string analyzer = File.ReadAllText(Path.Combine(sourceRoot,"AudioBeatAnalyzer.cs"));
    string resolver = File.ReadAllText(Path.Combine(sourceRoot,"MusicProcessResolver.cs"));
    string identity = File.ReadAllText(Path.Combine(sourceRoot,"MusicAppIdentity.cs"));
    Check(!extra.Contains("AudioModeSelector") && !extra.Contains("Режим звука Windows"), "music settings have no OS version selector");
    Check(!analyzer.Contains("SetCaptureMode") && analyzer.Contains("ShouldFallBack"), "backend and silent-capture fallback selected automatically");
    Check(resolver.Contains("MusicAppIdentity.WindowOwners") && resolver.Contains("MusicAppIdentity.ShortcutExecutables"), "production resolver reads desktop window and registered shortcut identities");
    Check(resolver.Contains("audible.Add(Root("), "child audio session is compared with player process tree root");
    Check(identity.Contains("SHGetPropertyStoreForWindow") && identity.Contains("IPersistFile"), "identity resolver uses actual Windows property stores");
}

// ---------------- Moku Link (передача файлов с Moku Mobile)
Check(TransferPolicy.SafeFileName("../../evil.exe") == "evil.exe", "link: path traversal stripped");
Check(TransferPolicy.SafeFileName("C:\\Windows\\a:b?.txt") == "a_b_.txt", "link: invalid characters replaced");
Check(TransferPolicy.SafeFileName("CON.txt") == "_CON.txt", "link: reserved device names");
Check(TransferPolicy.SafeFileName("   ...  ") == "file", "link: empty names get a default");
Check(TransferPolicy.UniqueName("a.jpg", n => n is "a.jpg" or "a (1).jpg") == "a (2).jpg", "link: unique names do not overwrite");
Check(TransferPolicy.NewPairCode().Length == 6, "link: six-digit pair code");
Check(TransferPolicy.SameSecret("123456", "123456") && !TransferPolicy.SameSecret("123456", "123457") && !TransferPolicy.SameSecret(null, "1"), "link: constant-time compare");
{
    var uri = TransferPolicy.PairUri(new[] { "192.168.1.5", "10.0.0.2" }, 47820, "pcid", "My PC", "042137");
    var info = TransferPolicy.ParsePairUri(uri);
    Check(info != null && info.Hosts.Length == 2 && info.Port == 47820 && info.Code == "042137" && info.Name == "My PC", "link: QR uri roundtrip");
    Check(TransferPolicy.ParsePairUri("https://evil/pair?h=1") == null && TransferPolicy.ParsePairUri("moku://pair?h=1&p=0&id=x&c=123456") == null, "link: bad QR rejected");
}
{
    var bad = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"LinkedPhones\":[{\"Id\":\"\",\"TokenHash\":\"x\"},{\"Id\":\"phone1234\",\"Name\":\"\",\"TokenHash\":\"" + new string('A', 64) + "\"}]}")!;
    bad.Normalize();
    Check(bad.LinkedPhones.Count == 1 && bad.LinkedPhones[0].Name == "Phone" && !bad.TransferEnabled && bad.TransferPcId.Length > 0, "link: settings normalized, transfer off by default");
}
{
    // Реальный обмен по сети с локальным сервером: сопряжение, загрузка с телефона, отправка на телефон, подтверждение.
    string dir = Path.Combine(Path.GetTempPath(), "moku-link-test-" + Guid.NewGuid().ToString("N"));
    using var server = new TransferServer("pc-test-id", () => dir) { PcName = "TestPC" };
    TransferServer.Received? got = null; TransferServer.Outgoing? delivered = null; TransferServer.Phone? paired = null;
    server.Paired += p => paired = p; server.FileReceived += f => got = f; server.Delivered += o => delivered = o;
    server.Start();
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}"), Timeout = TimeSpan.FromSeconds(20) };
    var hello = http.GetStringAsync("/api/hello").Result;
    Check(hello.Contains("\"app\":\"Moku\"") && hello.Contains("pc-test-id"), "link: hello identifies PC");
    Check((int)http.GetAsync("/api/ping").Result.StatusCode == 401, "link: unpaired phone rejected");
    Check((int)http.PostAsync("/api/pair?code=000000&phone=phone-abcdef&device=Pixel", null).Result.StatusCode == 403, "link: pairing needs active code");
    string code = server.NewPairCode();
    string wrong = code == "111111" ? "222222" : "111111";
    Check((int)http.PostAsync($"/api/pair?code={wrong}&phone=phone-abcdef&device=Pixel", null).Result.StatusCode == 403, "link: wrong code rejected");
    var pairResp = http.PostAsync($"/api/pair?code={code}&phone=phone-abcdef&device=Pixel%208", null).Result;
    string pairJson = pairResp.Content.ReadAsStringAsync().Result;
    Check(pairResp.IsSuccessStatusCode && paired?.Name == "Pixel 8", "link: pairing succeeds with QR code");
    string token = System.Text.Json.JsonDocument.Parse(pairJson).RootElement.GetProperty("token").GetString()!;
    Check((int)http.PostAsync($"/api/pair?code={code}&phone=phone-zzz999&device=X", null).Result.StatusCode == 403, "link: pair code is one-time");
    http.DefaultRequestHeaders.Add("X-Moku-Token", token);
    Check(http.GetAsync("/api/ping").Result.IsSuccessStatusCode && server.IsOnline("phone-abcdef"), "link: paired phone online");
    var payload = new byte[300_000]; new Random(7).NextBytes(payload);
    var up = http.PostAsync("/api/upload?name=" + Uri.EscapeDataString("../фото 1.jpg"), new ByteArrayContent(payload)).Result;
    Check(up.IsSuccessStatusCode && got != null && got.Name == "фото 1.jpg" && File.ReadAllBytes(got.Path).SequenceEqual(payload), "link: upload from phone saved intact");
    var up2 = http.PostAsync("/api/upload?name=" + Uri.EscapeDataString("фото 1.jpg"), new ByteArrayContent(new byte[] { 1, 2, 3 })).Result;
    Check(up2.IsSuccessStatusCode && got!.Name == "фото 1 (2).jpg" && Directory.GetFiles(dir).Length == 2, "link: second upload does not overwrite, no temp files left");
    string src = Path.Combine(Path.GetTempPath(), "moku-out-" + Guid.NewGuid().ToString("N") + ".bin");
    File.WriteAllBytes(src, payload);
    var waitInbox = http.GetStringAsync("/api/inbox?wait=10");
    Thread.Sleep(300);
    var o = server.Enqueue("phone-abcdef", src);
    string inbox = waitInbox.Result;
    Check(inbox.Contains(o.Id) && server.Pending("phone-abcdef") == 1, "link: long-poll inbox wakes on new file");
    var dl = http.GetAsync("/api/file?id=" + o.Id).Result;
    Check(dl.IsSuccessStatusCode && dl.Content.ReadAsByteArrayAsync().Result.SequenceEqual(payload) && dl.Headers.GetValues("X-Moku-Name").First() == Uri.EscapeDataString(o.Name), "link: download to phone intact");
    Check(http.PostAsync("/api/ack?id=" + o.Id, null).Result.IsSuccessStatusCode && delivered?.Id == o.Id && server.Pending("phone-abcdef") == 0, "link: ack marks delivered");
    using (var other = new HttpClient { BaseAddress = http.BaseAddress })
    {
        other.DefaultRequestHeaders.Add("X-Moku-Token", "not-a-real-token-123456");
        Check((int)other.GetAsync("/api/inbox").Result.StatusCode == 401, "link: foreign token cannot read inbox");
    }
    var udp = new System.Net.Sockets.UdpClient();
    udp.Client.ReceiveTimeout = 3000;
    var msg = System.Text.Encoding.UTF8.GetBytes(TransferPolicy.DiscoverMessage);
    udp.Send(msg, msg.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, TransferPolicy.DiscoveryPort));
    var ep = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
    string disc = System.Text.Encoding.UTF8.GetString(udp.Receive(ref ep));
    Check(disc.Contains("pc-test-id") && disc.Contains("\"port\":" + server.Port), "link: UDP discovery answers");
    Check(http.PostAsync("/api/unpair", null).Result.IsSuccessStatusCode && (int)http.GetAsync("/api/ping").Result.StatusCode == 401, "link: unpair revokes token");
    try { Directory.Delete(dir, true); File.Delete(src); } catch { }
}
Console.WriteLine($"PASS: {checks} checks (folders, file launch plans, Home/Steam/button UI contracts, settings and 400 randomized packing cases). Windows integration still requires Windows.");

// MQEL Patcher — sets up The Mighty Quest for Epic Loot to launch straight from Steam against a private
// server, with no per-launch scripts.
//
// It makes exactly TWO changes, BOTH inside the game folder — nothing outside it is ever touched:
//   1. Drops our self-contained cert-bypass proxy  ->  GameData\Bin\dinput8.dll
//      (our own code only; at runtime it loads the real Windows dinput8 from System32 and forwards input,
//       then applies the two-byte TLS-verify bypass so the client accepts the private server's cert).
//   2. Points the launcher config  ->  Launcher\PublicLauncherSettings.json  at the private server.
//
// The original PublicLauncherSettings.json is backed up so Uninstall restores stock and removes the DLL.
//
// Modes: no args -> UI; args -> CLI.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

static class Program
{
    public const string APPID = "239220";
    public const string GAME_FOLDER = "The Mighty Quest For Epic Loot";
    public const string ENV_NAME = "mqel-live";
    // The two base URLs the user configures. Everything else is derived by appending the env paths.
    public const string DEFAULT_LAUNCHER_URL = "http://localhost:8080";    // launcher's plain-HTTP services
    public const string DEFAULT_SERVER_URL = "https://localhost:8443";     // gameserver the client connects to (https)

    const string DLL_RESOURCE = "dinput8.dll";                       // embedded resource name
    const string BACKUP_JSON = "PublicLauncherSettings.mqel-backup.json";

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.Run(new PatcherForm());
            return 0;
        }
        AttachConsole(-1);
        return RunCli(args);
    }

    // ── install / uninstall (shared by CLI + UI) ────────────────────────────────────────────────────────

    /// <summary>Derive the five launcher-config URLs from the two base URLs the user provides.</summary>
    public static (string web, string gs, string patcher, string bg, string maint) Urls(string launcherBase, string serverBase)
    {
        string L = (launcherBase ?? "").Trim().TrimEnd('/');
        string S = (serverBase ?? "").Trim().TrimEnd('/');
        if (L.Length == 0) L = DEFAULT_LAUNCHER_URL;
        if (S.Length == 0) S = DEFAULT_SERVER_URL;
        return (
            web: $"{L}/{ENV_NAME}/launcher/load/",
            gs: $"{S}/{ENV_NAME}.gameserver",
            patcher: $"{L}/{ENV_NAME}.distribution",
            bg: $"{L}/static/empty.png",
            maint: $"{L}/{ENV_NAME}/launcher/");
    }

    public static void Install(string game, string launcherBase, string serverBase, Action<string> log)
    {
        string binDir = Path.Combine(game, "GameData", "Bin");
        string launcherDir = Path.Combine(game, "Launcher");
        string gameExe = Path.Combine(binDir, "MightyQuest.exe");
        string cfg = Path.Combine(launcherDir, "PublicLauncherSettings.json");
        if (!File.Exists(gameExe)) throw new Exception("game not found: " + gameExe);
        if (!File.Exists(cfg)) throw new Exception("launcher config not found: " + cfg);

        // 1a. drop our proxy into Bin (our own code, embedded in this exe)
        string dllOut = Path.Combine(binDir, "dinput8.dll");
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(DLL_RESOURCE)
                         ?? throw new Exception("embedded dinput8.dll missing from patcher build"))
        using (var fs = File.Create(dllOut))
            res.CopyTo(fs);
        log($"Installed cert-bypass proxy -> {dllOut}");

        // 1b. our proxy forwards real input calls to a RENAMED companion (a proxy can't forward to
        //     "dinput8.dll" without colliding with its own base name). Create that companion from the
        //     user's OWN Windows dinput8 — we ship no copyrighted code, and only READ it to copy locally.
        //     MightyQuest.exe is 32-bit, so we need the 32-bit dinput8: SpecialFolder.SystemX86 is the
        //     32-bit system dir (SysWOW64 on 64-bit Windows, System32 on 32-bit) — NOT plain System32,
        //     which on 64-bit Windows is the 64-bit DLL and would make the game fail to load.
        string origOut = Path.Combine(binDir, "dinput8_orig.dll");
        string sysDinput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "dinput8.dll");
        if (!File.Exists(sysDinput)) throw new Exception("Windows (32-bit) dinput8.dll not found at " + sysDinput);
        File.Copy(sysDinput, origOut, true);
        log($"Copied 32-bit OS dinput8 -> {Path.GetFileName(origOut)} (input pass-through; sourced locally)");

        // 2. point the launcher config at the private server (back up the original first, once)
        string backup = Path.Combine(launcherDir, BACKUP_JSON);
        if (!File.Exists(backup)) { File.Copy(cfg, backup, false); log($"Backed up original config -> {BACKUP_JSON}"); }

        var (web, gs, patcher, bg, maint) = Urls(launcherBase, serverBase);
        var node = JsonNode.Parse(File.ReadAllText(cfg))?.AsObject() ?? new JsonObject();
        node["LauncherWebSiteUrl"] = web;
        node["GameServerUrl"] = gs;
        node["PatcherServiceUrl"] = patcher;
        node["BackgroundImageUrl"] = bg;
        node["MaintenanceModeVerificationUrl"] = maint;
        node["EnvironmentName"] = ENV_NAME;
        File.WriteAllText(cfg, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        log($"Configured launcher -> {gs}");
        log("Done. Launch the game from Steam as normal.");
    }

    public static void Uninstall(string game, Action<string> log)
    {
        string binDir = Path.Combine(game, "GameData", "Bin");
        string launcherDir = Path.Combine(game, "Launcher");
        string dll = Path.Combine(binDir, "dinput8.dll");
        string orig = Path.Combine(binDir, "dinput8_orig.dll");
        string cfg = Path.Combine(launcherDir, "PublicLauncherSettings.json");
        string backup = Path.Combine(launcherDir, BACKUP_JSON);

        if (File.Exists(dll)) { TryDelete(dll); log("Removed dinput8.dll from Bin"); }
        else log("No dinput8.dll in Bin (already reverted?)");
        if (File.Exists(orig)) { TryDelete(orig); log("Removed dinput8_orig.dll from Bin"); }

        if (File.Exists(backup)) { TryReplace(backup, cfg); File.Delete(backup); log("Restored the original launcher config"); }
        else log("No config backup found (config left as-is)");
        log("Reverted to stock.");
    }

    static void TryDelete(string p) { for (int i = 0; i < 6; i++) { try { File.Delete(p); return; } catch { Thread.Sleep(300); } } File.Delete(p); }
    static void TryReplace(string from, string to) { for (int i = 0; i < 6; i++) { try { File.Copy(from, to, true); return; } catch { Thread.Sleep(300); } } File.Copy(from, to, true); }

    // ── CLI ─────────────────────────────────────────────────────────────────────────────────────────────
    static int RunCli(string[] args)
    {
        string launcherUrl = DEFAULT_LAUNCHER_URL, serverUrl = DEFAULT_SERVER_URL;
        bool uninstall = false; string? gamePathArg = null;
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--uninstall": case "-u": uninstall = true; break;
                case "--launcher-url": case "-l": launcherUrl = args[++i]; break;
                case "--server-url": case "--server": case "-s": serverUrl = args[++i]; break;
                case "--help": case "-h": case "/?": PrintUsage(); return 0;
                default: gamePathArg = args[i]; break;
            }
        try
        {
            string game = ResolveGamePath(gamePathArg);
            Console.WriteLine($"Game: {game}");
            if (uninstall) Uninstall(game, Console.WriteLine);
            else Install(game, launcherUrl, serverUrl, Console.WriteLine);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 1; }
    }

    public static string ResolveGamePath(string? arg)
    {
        if (!string.IsNullOrWhiteSpace(arg)) { if (IsGameDir(arg)) return arg; throw new Exception($"'{arg}' is not the game folder"); }
        foreach (var c in SteamLibraryCandidates()) { string g = Path.Combine(c, "steamapps", "common", GAME_FOLDER); if (IsGameDir(g)) return g; }
        throw new Exception("could not auto-detect the game install");
    }

    public static bool IsGameDir(string dir) =>
        File.Exists(Path.Combine(dir, "Launcher", "PublicLauncherSettings.json"))
        && File.Exists(Path.Combine(dir, "GameData", "Bin", "MightyQuest.exe"));

    static IEnumerable<string> SteamLibraryCandidates()
    {
        var libs = new List<string>();
        string? steam = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)
                        ?? (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
        if (steam != null)
        {
            steam = steam.Replace('/', '\\'); libs.Add(steam);
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\"")) libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        foreach (var d in new[] { @"C:\Program Files (x86)\Steam", @"D:\Steam", @"D:\Games\Steam", @"E:\Steam", @"E:\Games\Steam" }) libs.Add(d);
        return libs.Distinct();
    }

    static void PrintUsage()
    {
        Console.WriteLine("MQEL Patcher — direct-from-Steam play on a private server.\n");
        Console.WriteLine("  MqelPatcher                                         launch the UI");
        Console.WriteLine("  MqelPatcher --launcher-url URL --server-url URL     install");
        Console.WriteLine("  MqelPatcher --uninstall                             remove the DLL + restore the config");
        Console.WriteLine("  MqelPatcher \"PATH\\TO\\GAME\"                          explicit game folder\n");
        Console.WriteLine($"  defaults: --launcher-url {DEFAULT_LAUNCHER_URL}  --server-url {DEFAULT_SERVER_URL}");
        Console.WriteLine("  It only ever writes GameData\\Bin\\dinput8.dll and Launcher\\PublicLauncherSettings.json.");
    }
}

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace MQEL.Tests;

// Wire-contract golden tests. Each drives the REAL pipeline from a fresh first-run account through a scripted
// sequence of REAL captured requests, then asserts the exact serialized response body against a committed
// golden file. These turn "did the Program.cs split change one byte the client sees?" into `dotnet test`.
//
// Regenerate goldens after an INTENTIONAL contract change: set env UPDATE_GOLDENS=1 and re-run (review the diff).
public sealed class WireGoldenTests
{
    readonly ITestOutputHelper _o;
    public WireGoldenTests(ITestOutputHelper o) => _o = o;

    // ---- fixtures ---------------------------------------------------------------------------------------
    static readonly JsonElement Fx = JsonDocument.Parse(File.ReadAllText(FixturePath())).RootElement;
    static string FixturePath([CallerFilePath] string f = "") =>
        Path.Combine(Path.GetDirectoryName(f)!, "fixtures", "ftue-requests.json");
    static string GoldenDir([CallerFilePath] string f = "") =>
        Path.Combine(Path.GetDirectoryName(f)!, "fixtures", "goldens");

    static (string suffix, string query, string body) Req(string name)
    {
        var r = Fx.GetProperty("requests").GetProperty(name);
        return (r.GetProperty("suffix").GetString()!, r.GetProperty("query").GetString() ?? "", r.GetProperty("body").GetString() ?? "");
    }
    static string SendCmd(string name) => Fx.GetProperty("sendcommands").GetProperty(name).GetProperty("body").GetString()!;

    // ---- HTTP helpers -----------------------------------------------------------------------------------
    static HttpClient Client(WireGoldenFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("t", "wiretest-token");   // resolver pins to the dev account regardless
        return c;
    }
    static async Task<string> PostAsync(HttpClient c, string suffixOrPath, string body, string query = "")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/" + suffixOrPath.TrimStart('/') + query)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        var resp = await c.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }
    static async Task<string> GetAsync(HttpClient c, string suffixOrPath, string query = "")
        => await (await c.GetAsync("/" + suffixOrPath.TrimStart('/') + query)).Content.ReadAsStringAsync();

    // Drive a fresh account to "hero chosen" via the real onboarding requests.
    static async Task Onboard(HttpClient c)
    {
        var (dnP, _, dnB) = Req("ChooseDisplayName");
        var (hP, _, hB) = Req("ChooseFirstHero");
        await PostAsync(c, dnP, dnB);
        await PostAsync(c, hP, hB);
    }

    // ---- golden compare ---------------------------------------------------------------------------------
    // Scrub the handful of genuinely non-deterministic fields (server-stamped timestamps) so the golden is a
    // pure function of (state, request). ObjectIds are deterministic (InventorySeq counter off a fresh account).
    static string Scrub(string s)
    {
        s = Regex.Replace(s, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+\-]\d{2}:\d{2})?", "<TS>");
        return s.Replace("\r\n", "\n");
    }

    void Golden(string name, string actual)
    {
        Directory.CreateDirectory(GoldenDir());
        var path = Path.Combine(GoldenDir(), name + ".json");
        var scrubbed = Scrub(actual);
        var update = Environment.GetEnvironmentVariable("UPDATE_GOLDENS") == "1";
        if (update || !File.Exists(path))
        {
            File.WriteAllText(path, scrubbed);
            _o.WriteLine($"[golden {(update ? "updated" : "created")}] {name} ({scrubbed.Length} chars)");
            return;
        }
        var expected = Scrub(File.ReadAllText(path));
        Assert.True(expected == scrubbed,
            $"Wire body '{name}' changed vs golden.\n--- expected ---\n{Trunc(expected)}\n--- actual ---\n{Trunc(scrubbed)}");
    }
    static string Trunc(string s) => s.Length > 1600 ? s[..1600] + " …(" + s.Length + " chars)" : s;

    // ---- tests ------------------------------------------------------------------------------------------

    [Fact]  // pure launcher shim — locks the JsonObject rebuild (§1.4) and the package-version table.
    public async Task PackagesVersion_body()
    {
        using var f = new WireGoldenFactory();
        var resp = await PostAsync(Client(f), "PatcherService.hqs/GetRMLauncherAndPackagesVersion",
            "{\"launcherVersionName\":\"276072\"}");
        Golden("packages-version", resp);
    }

    [Fact]  // GAI for a brand-new account — the first-run (no hero/castle) AccountInformation body.
    public async Task Gai_first_run()
    {
        using var f = new WireGoldenFactory();
        var resp = await GetAsync(Client(f), "AccountInformationService.hqs/GetAccountInformation");
        Golden("gai-first-run", resp);
    }

    [Fact]  // GAI after the hero is chosen — the hero-created AccountInformation body.
    public async Task Gai_hero_created()
    {
        using var f = new WireGoldenFactory();
        var c = Client(f);
        await Onboard(c);
        var resp = await GetAsync(c, "AccountInformationService.hqs/GetAccountInformation");
        Golden("gai-hero-created", resp);
    }

    [Fact]  // StartAttack on the forest castle (2) — the served castle graph + loot tables.
    public async Task StartAttack_castle2()
    {
        using var f = new WireGoldenFactory();
        var c = Client(f);
        await Onboard(c);
        var (suffix, query, body) = Req("StartAttack");
        var resp = await PostAsync(c, suffix, body, query);
        Golden("startattack-castle2", resp);
    }

    [Fact]  // EndAttack scoring — server sums the client's looted-instance-ids against the StartAttack tables.
    public async Task EndAttack_scored()
    {
        using var f = new WireGoldenFactory();
        var c = Client(f);
        await Onboard(c);
        var (saS, saQ, saB) = Req("StartAttack");
        await PostAsync(c, saS, saB, saQ);            // populate the per-account combat scratch
        var (eaS, eaQ, eaB) = Req("EndAttack");
        var resp = await PostAsync(c, eaS, eaB, eaQ);
        Golden("endattack-scored", resp);
    }

    [Fact]  // SendCommands equip batch — ack shape ({} on success).
    public async Task SendCommands_equip()
    {
        using var f = new WireGoldenFactory();
        var c = Client(f);
        await Onboard(c);
        var resp = await PostAsync(c, "ServerCommandService.hqs/SendCommands", SendCmd("HeroEquipmentEquipCommand"));
        Golden("sendcommands-equip", resp);
    }

    [Fact]  // ChooseFirstHero — the generated starter-hero body ({"Result":<hero>}).
    public async Task ChooseFirstHero_body()
    {
        using var f = new WireGoldenFactory();
        var c = Client(f);
        var (dnP, _, dnB) = Req("ChooseDisplayName");
        await PostAsync(c, dnP, dnB);
        var (hP, hQ, hB) = Req("ChooseFirstHero");
        Golden("choosefirsthero", await PostAsync(c, hP, hB, hQ));
    }

    [Fact]  // ChooseDisplayName — ack shape ({}).
    public async Task ChooseDisplayName_body()
    {
        using var f = new WireGoldenFactory();
        var (dnP, dnQ, dnB) = Req("ChooseDisplayName");
        Golden("choosedisplayname", await PostAsync(Client(f), dnP, dnB, dnQ));
    }

    [Fact]  // GetAttackSelectionList — file-backed campaign attack list (guards DispatchStatic + onboarded state).
    public async Task Static_attack_selection_list()
    {
        using var f = new WireGoldenFactory();
        var c = Client(f);
        await Onboard(c);
        Golden("static-attack-selection", await PostAsync(c, "AttackSelectionService.hqs/GetAttackSelectionList", ""));
    }

    [Fact]  // file-backed .hqs dispatch (DispatchStatic) — the responses/<svc>/<method>.json fallthrough.
    public async Task Static_castles_for_sale()
    {
        using var f = new WireGoldenFactory();
        var resp = await PostAsync(Client(f), "CastleForSaleService.hqs/GetCastlesForSale", "");
        Golden("static-castles-for-sale", resp);
    }

    [Fact]  // starter-castle BuyCommand → CastleBought notifications (the one-time purchase path).
    public async Task SendCommands_buy_castle()
    {
        using var f = new WireGoldenFactory();
        var c = Client(f);
        await Onboard(c);
        var resp = await PostAsync(c, "ServerCommandService.hqs/SendCommands", SendCmd("BuyCommand"));
        Golden("sendcommands-buy-castle", resp);
    }
}

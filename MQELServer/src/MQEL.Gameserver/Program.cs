using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MQEL.Core.Model;
using MQEL.Core.Persistence;
using MQEL.Core.Verification;
using MQEL.Data.Persistence;
using MQEL.Verification;

var builder = WebApplication.CreateBuilder(args);

// The verification seam — stubbed today (returns valid=true). The audit substrate (persisting seed /
// result / replay / castle snapshot) is the gameserver's job and is wired in once we have a schema.
builder.Services.AddSingleton<IVerificationService, StubVerificationService>();

// Per-account persistence — the EF Core data layer behind the IAccountRepository black box (SQLite today,
// Postgres-ready via the provider in AddDataLayer). Backend chosen by the "Storage" config section.
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
builder.Services.AddDataLayer(storageOptions);

// Game-design catalogs from the decrypted spec DB — immutable after load, so DI singletons. Registered as
// factories (fallback to empty on load failure) and force-resolved at startup for eager load + the count logs.
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILogger<Program>>();
    try { return ItemCatalog.FindSpecRoot() is { } r ? ItemCatalog.Load(r) : new ItemCatalog(); }
    catch (Exception ex) { log.LogWarning(ex, "ItemCatalog load failed — store buys won't resolve"); return new ItemCatalog(); }
});
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILogger<Program>>();
    try { return MissionCatalog.FindSpecRoot() is { } r ? MissionCatalog.Load(r) : new MissionCatalog(); }
    catch (Exception ex) { log.LogWarning(ex, "MissionCatalog load failed — objective rewards won't fire"); return new MissionCatalog(); }
});
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILogger<Program>>();
    try { return AssignmentCatalog.FindSpecRoot() is { } r ? AssignmentCatalog.Load(r) : new AssignmentCatalog(); }
    catch (Exception ex) { log.LogWarning(ex, "AssignmentCatalog load failed — ExecuteAssignmentActionCommand won't resolve"); return new AssignmentCatalog(); }
});
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILogger<Program>>();
    try { return CastleRenovationCatalog.FindSpecRoot() is { } r ? CastleRenovationCatalog.Load(r) : new CastleRenovationCatalog(); }
    catch (Exception ex) { log.LogWarning(ex, "CastleRenovationCatalog load failed — renovation costs won't resolve"); return new CastleRenovationCatalog(); }
});

var app = builder.Build();

// Apply pending EF migrations on startup — creates/updates the schema (cross-platform SQLite file).
using (var migrateScope = app.Services.CreateScope())
{
    var migrateDb = migrateScope.ServiceProvider.GetRequiredService<GameDbContext>();
    migrateDb.Database.Migrate();
    // §3.3 — WAL is stored in the DB file header (set once, persists): readers (the dashboard poll) don't block
    // the game's writers and vice-versa. ExecuteSqlRaw returns a row for PRAGMA journal_mode; that's ignored.
    if (storageOptions.Provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        try { migrateDb.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); } catch { }
}

// ── Admin dashboard (wwwroot) — served by a small explicit file-send middleware, ahead of the capture rig +
//    the game catch-all. (UseStaticFiles wouldn't short-circuit under this app's catch-all routing pipeline; a
//    direct SendFileAsync is robust and keeps UI requests out of the capture + per-account path.)
var uiRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
var uiTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [".html"] = "text/html; charset=utf-8", [".css"] = "text/css; charset=utf-8", [".js"] = "text/javascript; charset=utf-8",
    [".woff"] = "font/woff", [".ttf"] = "font/ttf", [".png"] = "image/png", [".svg"] = "image/svg+xml", [".json"] = "application/json",
};
app.Use(async (ctx, next) =>
{
    var p = ctx.Request.Path.Value ?? "/";
    if (p == "/") p = "/index.html";
    if (p.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("/game-assets/", StringComparison.OrdinalIgnoreCase)
        || p.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
    {
        var file = Path.GetFullPath(Path.Combine(uiRoot, p.TrimStart('/')));
        // §2.6 — compare against uiRoot + separator, not the bare prefix: a sibling dir like "wwwrootX" would
        // otherwise pass the StartsWith(uiRoot) check and escape the intended root.
        if (file.StartsWith(uiRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(file))
        {
            ctx.Response.ContentType = uiTypes.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
            await ctx.Response.SendFileAsync(file);
            return;
        }
    }
    await next();
});
app.Logger.LogInformation("UI dashboard root={Root} files={N}", uiRoot,
    Directory.Exists(uiRoot) ? Directory.GetFiles(uiRoot, "*", SearchOption.AllDirectories).Length : -1);

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// STEP 1 — capture rig.
// Log EVERY request the real client makes (method, path, query, headers, body) to a JSONL transcript,
// so we learn the exact launch → login → lobby traffic. That captured ground truth is what we build the
// real endpoints against (several of them — e.g. the attack result/replay submission — are engine-native
// and NOT visible in the client's JS). See ../../FINDINGS.md.
// ─────────────────────────────────────────────────────────────────────────────────────────────────
var captureDir = app.Configuration["Capture:Directory"] ?? "captures";
Directory.CreateDirectory(captureDir);
var captureFile = Path.Combine(captureDir, "requests.jsonl");
var captureLock = new object();
// §5.1 — append-only audit substrate: the RAW EndAttack body (JSON + binary replay blob) is written here
// verbatim, one file per attack, BEFORE scoring. Deferring the re-sim compute is fine; dropping the bytes is
// the one non-deferrable shortcut (AuditBundle.cs) — you could never verify retroactively.
var auditDir = app.Configuration["Audit:Directory"] ?? Path.Combine(captureDir, "audit");
Directory.CreateDirectory(auditDir);

app.Use(async (ctx, next) =>
{
    ctx.Request.EnableBuffering();
    string body = "";
    if (ctx.Request.ContentLength is > 0)
    {
        using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
        body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;
    }

    var record = new
    {
        ts = DateTimeOffset.UtcNow,
        method = ctx.Request.Method,
        path = ctx.Request.Path.Value,
        query = ctx.Request.QueryString.Value,
        headers = ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
        body
    };
    var line = JsonSerializer.Serialize(record);
    lock (captureLock) File.AppendAllText(captureFile, line + Environment.NewLine);

    app.Logger.LogInformation("REQ {Method} {Path}{Query} ({Len} body bytes)",
        ctx.Request.Method, ctx.Request.Path, ctx.Request.QueryString, body.Length);

    await next();
});

app.MapGet("/__capture", () => $"MQEL capture host — requests -> {captureFile}");   // (/ now serves the admin dashboard)

// Smoke test for the IAccountRepository black box — round-trips a full account GRAPH (account+wallet+hero+
// gear+spell) through EF. (The per-account handler refactor is next; today's handlers still use gameState.)
app.MapGet("/__dbtest", async (IAccountRepository repo) =>
{
    const long testId = 999;
    var acct = await repo.GetAsync(testId);
    if (acct is null)
    {
        acct = new Account { AccountId = testId, DisplayName = "DbTest", SelectedHeroClass = 3 };
        acct.Wallets.Add(new Wallet { CurrencyType = 2, Amount = 1000, Capacity = 2000 });
        acct.Heroes.Add(new Hero
        {
            HeroClass = 3, Level = 2, Xp = 124,
            Gear = { new HeroGearItem { Slot = "MainHand", TemplateId = 124, ArchetypeId = 2 } },
            Spells = { new HeroSpell { SpellId = 158, Level = 1, SlotIndex = 3 } }
        });
        await repo.SaveAsync(acct);
        acct = await repo.GetAsync(testId);   // reload from the DB to prove the round-trip
    }
    return Results.Json(new
    {
        ok = acct is not null, provider = storageOptions.Provider,
        account = new
        {
            acct!.AccountId, acct.DisplayName,
            wallet = acct.Wallets.Select(w => new { w.CurrencyType, w.Amount, w.Capacity }),
            heroes = acct.Heroes.Select(h => new { h.HeroClass, h.Level, h.Xp,
                gear = h.Gear.Select(g => new { g.Slot, g.TemplateId }),
                spells = h.Spells.Select(s => new { s.SpellId, s.SlotIndex }) })
        }
    });
});

// ── Per-account persistence + request serialization ──────────────────────────────────────────────────
// Every gameserver request resolves WHICH account it acts as (the identity seam), loads that account from the
// DB (creating a first-run account on first contact), hands the working AccountState to the handlers below via
// ctx.Items, then saves it back after they run. A per-account lock serializes concurrent same-account requests
// so an overlapping load->mutate->save can't clobber; different accounts run fully in parallel. The transient
// combat scratch is cached per account (it must survive the StartAttack->EndAttack request pair).
var resolver = app.Services.GetRequiredService<IAccountResolver>();
// Force-resolve the DI-singleton catalogs at startup (eager load + count logs); both are passed to the
// endpoint deps records below. Shop SKUs/item templates resolve BuyHeroItemCommand; missions drive MissionManager.
var itemCatalog = app.Services.GetRequiredService<ItemCatalog>();
app.Logger.LogInformation("ItemCatalog: {Skus} SKUs, {Tpls} item templates loaded", itemCatalog.SkuCount, itemCatalog.TemplateCount);
var missionCatalog = app.Services.GetRequiredService<MissionCatalog>();
app.Logger.LogInformation("MissionCatalog: {Count} missions loaded", missionCatalog.Count);
var assignmentCatalog = app.Services.GetRequiredService<AssignmentCatalog>();
var castleRenovationCatalog = app.Services.GetRequiredService<CastleRenovationCatalog>();
var accountLocks = new System.Collections.Concurrent.ConcurrentDictionary<long, SemaphoreSlim>();
var attackScratch = new System.Collections.Concurrent.ConcurrentDictionary<long, AttackScratch>();
// Per-account in-memory mission progress (objectiveId -> per-condition counters). Lets a mission span multiple
// raids within a session (mirrors attackScratch). Not yet persisted across reconnects — see MissionManager.cs.
var missionProgress = new System.Collections.Concurrent.ConcurrentDictionary<long, Dictionary<int, int[]>>();
SemaphoreSlim GateFor(long id) => accountLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
AttackScratch ScratchFor(long id) => attackScratch.GetOrAdd(id, _ => new AttackScratch());
static Account NewFirstRun(long id) => new()
{
    AccountId = id, DisplayName = "Champion", InventorySeq = 0x10,
    Wallets =
    {
        new Wallet { AccountId = id, CurrencyType = 2, Amount = 1000, Capacity = 2000 },  // Gold/IGC
        new Wallet { AccountId = id, CurrencyType = 4, Amount = 0,    Capacity = 2000 },  // LifeForce
    },
};
// Launcher boot endpoints + diagnostics act on no account; everything else is an account-bearing call.
// (Blocklist, not allowlist — the game RPC surface is still being discovered, so unknown paths default to
// account-bearing. The cast in the catch-all now falls through safely if state is somehow absent — §2.1.)
static bool IsAccountRequest(string path) =>
    path.Length > 1 &&
    !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
    !path.Equals("/health", StringComparison.OrdinalIgnoreCase) &&
    !path.Contains("GetRMLauncher", StringComparison.OrdinalIgnoreCase) &&
    !path.Contains("/launcher/", StringComparison.OrdinalIgnoreCase) &&
    !path.Contains("__dbtest", StringComparison.OrdinalIgnoreCase) &&
    // §2.1/§2.5 — keep stray static/telemetry-adjacent hits (favicon, images) off the account gate: they'd
    // otherwise take the semaphore and load the whole account graph just to fall through to a stub 200.
    !path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) &&
    !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
    !path.StartsWith("/static/", StringComparison.OrdinalIgnoreCase);

app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (!IsAccountRequest(path)) { await next(); return; }
    var token = ctx.Request.Headers["t"].FirstOrDefault();
    long? ovr = long.TryParse(ctx.Request.Query["__account"].FirstOrDefault(), out var oa) ? oa : null;
    var accountId = resolver.Resolve(token, ovr);
    var gate = GateFor(accountId);
    await gate.WaitAsync();
    try
    {
        var repo = ctx.RequestServices.GetRequiredService<IAccountRepository>();
        var entity = await repo.GetAsync(accountId) ?? NewFirstRun(accountId);
        var state = AccountMapper.ToAccountState(entity, ScratchFor(accountId));
        ctx.Items["account"] = state;
        await next();
        AccountMapper.ApplyTo(state, entity);
        await repo.SaveAsync(entity);
    }
    finally { gate.Release(); }
});
// Serialize generated bodies with LITERAL & + < > (not \uXXXX). The reference JSON uses literal chars and the
// game's old Argo (Chromium-27-era) parser chokes on System.Text.Json's default \uXXXX escapes → boot network error.
var jsonOpts = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
// Wire trace — ONE channel (§1.8/§2.4). Path from config, defaulting under the capture dir: no hardcoded
// drive letter (the old d:\ path broke every off-box / Docker run), appends serialized under a lock so
// concurrent request threads can't interleave or throw. /api/logs reads this same file.
var traceFile = app.Configuration["Trace:File"] ?? Path.Combine(captureDir, "wire-trace.log");
var traceLock = new object();
void WireLog(string msg)
{
    try { lock (traceLock) File.AppendAllText(traceFile, $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}"); } catch { }
}
try { lock (traceLock) File.WriteAllText(traceFile, $"=== session start {DateTime.Now:HH:mm:ss} ==={Environment.NewLine}"); } catch { }
string RespFile(string rel)
{
    var p = Path.Combine(Directory.GetCurrentDirectory(), "responses", rel);
    return File.Exists(p) ? p : Path.Combine(AppContext.BaseDirectory, "responses", rel);
}

// ── Admin/dashboard API (/health + /api/*) — extracted to AdminEndpoints.cs. The game client never calls these;
// the shared state the handlers need is passed in via AdminDeps rather than captured.
var adminToken = builder.Configuration["Admin:Token"] ?? "";
app.MapAdminApi(new AdminDeps(adminToken, GateFor, NewFirstRun, attackScratch, itemCatalog, storageOptions, traceFile, traceLock));

// Shared services the game .hqs handlers need (passed to GameEndpoints rather than captured).
var gameDeps = new GameDeps(jsonOpts, RespFile, WireLog, itemCatalog, missionCatalog, missionProgress, auditDir, app.Logger, assignmentCatalog, castleRenovationCatalog);

// Dispatcher. The capture middleware above already logged the request. We answer the launcher's boot-gate
// endpoints just enough to advance, and 200 everything else so the client keeps revealing the flow.
// The backend speaks ".hqs" RPC services with .NET-contract ($type) JSON; see ../../code-analysis/.
app.Map("/{**catchAll}", async (HttpContext ctx) =>
{
    var path = ctx.Request.Path.Value ?? "";
    // DIAGNOSTIC: trace the full client->server protocol (skip telemetry spam). For SendCommands, capture the command
    // batch body — that's the client-authoritative channel where a level-up / account-refresh trigger would live.
    try {
        if (!path.Contains("Tracking", StringComparison.OrdinalIgnoreCase) && !path.Contains("ClientIdle", StringComparison.OrdinalIgnoreCase) && !path.Contains("__state", StringComparison.OrdinalIgnoreCase)) {
            string _b = "";
            if (path.Contains("SendCommands", StringComparison.OrdinalIgnoreCase) || path.Contains("EndAttack", StringComparison.OrdinalIgnoreCase) || path.Contains("StartAttack", StringComparison.OrdinalIgnoreCase)) { ctx.Request.Body.Position = 0; using var _sr = new System.IO.StreamReader(ctx.Request.Body, leaveOpen: true); _b = "  " + (await _sr.ReadToEndAsync()); ctx.Request.Body.Position = 0; }
            WireLog($"{path}{_b}");
        }
    } catch { }

    // Launcher/patcher boot shims (version checks + Steam login page) — extracted to LauncherEndpoints.cs.
    // None touch account state; they're checked before the account cast below.
    if (await LauncherEndpoints.TryHandle(path, ctx, jsonOpts) is { } launcherResult)
        return launcherResult;

    // ── Account-bearing gameserver call: the per-account middleware has loaded the working state (creating a
    //    first-run account on first contact) into ctx.Items, and will save + unlock it after this returns.
    // §2.1 — if there is NO account on the request (a non-account path, or a future endpoint the predicate
    // doesn't classify as account-bearing), fall through to the static dispatcher instead of NRE-ing the cast.
    if (ctx.Items["account"] is not AccountState gameState)
        return await DispatchStatic();

    // Account-bearing game RPC services (GAI/attack/commands/...) — extracted to GameEndpoints.cs.
    if (await GameEndpoints.TryHandle(ctx, path, gameState, gameDeps) is { } gameResult)
        return gameResult;

    return await DispatchStatic();

    // The non-account tail (extracted so the account cast above can fall through here). Handles the file-backed
    // static .hqs responses and the STUB-200 discovery fallback — neither touches account state.
    async Task<IResult> DispatchStatic()
    {
        // FILE-BACKED static service responses: responses/<Service>.hqs/<Method>.json — drop a JSON file to add a
        // service, no code change (hot-loaded per request). Mined from the MQELOffline_cpp reference: CastleForSale
        // (GetCastlesForSale/GetCastleForSaleBuildInfo), AttackSelection, SeasonalCompetition, AccountService, …
        var segs = (path ?? "").Trim('/').Split('/');
        if (segs.Length >= 2 && segs[^2].EndsWith(".hqs", StringComparison.OrdinalIgnoreCase)
            && !segs[^1].Contains('.'))
        {
            var rel = Path.Combine("responses", segs[^2], segs[^1] + ".json");
            var ff = Path.Combine(Directory.GetCurrentDirectory(), rel);
            if (!File.Exists(ff)) ff = Path.Combine(AppContext.BaseDirectory, rel);
            if (File.Exists(ff)) return Results.Content(await File.ReadAllTextAsync(ff), "application/json");
        }

        // §2.3 — nothing matched: deliberately 200 empty to keep the client revealing flow, but log it so the set of
        // "endpoints we silently 200'd this session" is a reviewable list (the unimplemented-surface inventory) instead
        // of archaeology. Skip the known-noisy telemetry pings so the list stays signal.
        if (!string.IsNullOrEmpty(path)
            && !path.Contains("Tracking", StringComparison.OrdinalIgnoreCase) && !path.Contains("ClientIdle", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("KeepAlive", StringComparison.OrdinalIgnoreCase) && !path.Contains("__state", StringComparison.OrdinalIgnoreCase))
            WireLog($"STUB 200 {path}");
        return Results.Ok();
    }
});

app.Run();

// Exposes the implicit top-level Program class to WebApplicationFactory<Program> so the wire-golden test
// harness can boot the real pipeline in-process. (Top-level statements generate an internal Program; this
// makes it public + partial without changing anything at runtime.)
public partial class Program { }

// Account state model lives in AccountState.cs (AccountState / HeroState) — the GAI body is GENERATED from it.

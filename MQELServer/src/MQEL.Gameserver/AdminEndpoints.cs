using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MQEL.Core.Model;
using MQEL.Data.Persistence;

// The admin/dashboard API (/health + /api/*). The game client never calls these — they back the wwwroot
// dashboard only — so they live entirely off the per-account game path. Extracted from the Program.cs god-file
// (fableReview §3.1). The shared state the handlers need is passed in via AdminDeps rather than captured, so the
// module has no hidden coupling to Program.cs.
sealed record AdminDeps(
    string AdminToken,
    Func<long, SemaphoreSlim> GateFor,
    Func<long, Account> NewFirstRun,
    ConcurrentDictionary<long, AttackScratch> AttackScratch,
    ItemCatalog ItemCatalog,
    StorageOptions StorageOptions,
    string TraceFile,
    object TraceLock);

static class AdminEndpoints
{
    public static void MapAdminApi(this WebApplication app, AdminDeps d)
    {
        var devAccountId = d.StorageOptions.DefaultAccountId;

        // Auth = X-Admin-Token vs config "Admin:Token" (empty ⇒ open, local).
        bool Authed(HttpContext c) => string.IsNullOrEmpty(d.AdminToken) || (string?)c.Request.Headers["X-Admin-Token"] == d.AdminToken;
        // §1.3 — admin writes hold the SAME per-account gate as the game middleware, so a dashboard edit and an
        // in-flight game request can't clobber each other's whole-graph save. Auth stays outside; only the DB write is serialized.
        async Task<IResult> Gated(long id, Func<Task<IResult>> body)
        {
            var gate = d.GateFor(id);
            await gate.WaitAsync();
            try { return await body(); }
            finally { gate.Release(); }
        }
        void UpsertWalletApi(Account a, int type, long amount)
        {
            var w = a.Wallets.FirstOrDefault(x => x.CurrencyType == type);
            if (w is null) { w = new Wallet { AccountId = a.AccountId, CurrencyType = type, Capacity = 2000 }; a.Wallets.Add(w); }
            w.Amount = amount;
        }
        // §3.5 — snapshots load through the SAME shared include list as the game path (AccountGraph), so the two can't drift.
        static IQueryable<Account> SnapGraph(IQueryable<Account> q) => AccountGraph.Includes(q);

        app.MapGet("/health", () => Results.Ok(new { ok = true }));

        app.MapGet("/api/status", async (GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            return Results.Json(new
            {
                // §2.7 — report the ACTUAL port the request arrived on and the configured DB, not hardcoded constants.
                running = true, port = ctx.Connection.LocalPort, db = d.StorageOptions.ConnectionString,
                accounts = await db.Accounts.CountAsync(a => !a.IsTemplate),
                snapshots = await db.Accounts.CountAsync(a => a.IsTemplate),
                skus = d.ItemCatalog.SkuCount, templates = d.ItemCatalog.TemplateCount,
            });
        });

        app.MapGet("/api/accounts", async (GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            return Results.Json(await db.Accounts.Where(a => !a.IsTemplate)
                .Select(a => new { a.AccountId, a.DisplayName,
                    heroClass = a.Heroes.Select(h => h.HeroClass).FirstOrDefault(),
                    heroLevel = a.Heroes.Select(h => h.Level).FirstOrDefault() })
                .ToListAsync());
        });

        app.MapGet("/api/accounts/{id:long}", async (long id, GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            var a = await db.Accounts.Include(x => x.Wallets)
                .Include(x => x.Heroes).ThenInclude(h => h.Gear)
                .Include(x => x.Heroes).ThenInclude(h => h.Spells)
                .Include(x => x.CompletedAssignments)
                .Include(x => x.Objectives)
                .Include(x => x.CraftingMaterials)
                .FirstOrDefaultAsync(x => x.AccountId == id);
            if (a is null) return Results.NotFound();
            var hero = a.Heroes.FirstOrDefault();
            return Results.Json(new
            {
                a.AccountId, a.DisplayName, a.CastleRenovationLevel,
                gold = a.Wallets.FirstOrDefault(w => w.CurrencyType == 2)?.Amount ?? 0,
                lifeForce = a.Wallets.FirstOrDefault(w => w.CurrencyType == 4)?.Amount ?? 0,
                heroClass = hero?.HeroClass ?? 0, heroLevel = hero?.Level ?? 1, heroXp = hero?.Xp ?? 0,
                gear = hero is null ? Array.Empty<object>() : hero.Gear.Select(g => (object)new { g.Slot, g.TemplateId }).ToArray(),
                spells = hero is null ? Array.Empty<object>() : hero.Spells.Select(s => (object)new { s.SpellId, s.SlotIndex }).ToArray(),
                completedAssignments = a.CompletedAssignments.OrderBy(c => c.CompletedUtc).Select(c => c.AssignmentId).ToArray(),
                objectives = a.Objectives.Select(o => (object)new { o.ObjectiveId, o.Status }).ToArray(),
                craftingMaterials = a.CraftingMaterials.Select(m => (object)new { m.MaterialId, m.Quantity }).ToArray(),
            });
        });

        app.MapPost("/api/accounts/{id:long}", async (long id, AccountEdit edit, GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            return await Gated(id, async () =>
            {
                var a = await db.Accounts.Include(x => x.Wallets).Include(x => x.Heroes).Include(x => x.CompletedAssignments)
                    .FirstOrDefaultAsync(x => x.AccountId == id);
                if (a is null) return Results.NotFound();
                if (edit.DisplayName is { } dn) a.DisplayName = dn;
                // §1.5 — apply wallet/hero fields ONLY when present; an omitted field must not zero the wallet or de-level
                // the hero. Clamp to [0, int.MaxValue]: the game reads Wallet.Amount as (int) on load.
                if (edit.Gold is { } gold) UpsertWalletApi(a, 2, Math.Clamp(gold, 0, int.MaxValue));
                if (edit.LifeForce is { } lf) UpsertWalletApi(a, 4, Math.Clamp(lf, 0, int.MaxValue));
                if (a.Heroes.FirstOrDefault() is { } h)
                {
                    if (edit.HeroLevel is { } lvl) h.Level = lvl;
                    if (edit.HeroXp is { } xp) h.Xp = xp;
                }
                if (edit.CompletedAssignments is { } keep)
                {
                    a.CompletedAssignments.RemoveAll(c => !keep.Contains(c.AssignmentId));
                    foreach (var aid in keep)
                        if (a.CompletedAssignments.All(c => c.AssignmentId != aid))
                            a.CompletedAssignments.Add(new CompletedAssignment { AccountId = id, AssignmentId = aid, CompletedUtc = DateTime.UtcNow });
                }
                await db.SaveChangesAsync();
                return Results.Ok(new { ok = true });
            });
        });

        // Reset an account to a FRESH FIRST-RUN starter. Reuses the restore path — ApplyTo overwrites the live graph
        // by key — only the source is a freshly-built NewFirstRun instead of a template. Relaunch the game after.
        app.MapPost("/api/accounts/{id:long}/reset", async (long id, GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            return await Gated(id, async () =>
            {
                var live = await SnapGraph(db.Accounts).FirstOrDefaultAsync(a => a.AccountId == id && !a.IsTemplate);
                if (live is null) { live = new Account { AccountId = id }; db.Accounts.Add(live); }
                var fresh = AccountMapper.ToAccountState(d.NewFirstRun(id), new AttackScratch());
                AccountMapper.ApplyTo(fresh, live);
                live.IsTemplate = false; live.SnapshotName = null;
                await db.SaveChangesAsync();
                d.AttackScratch.TryRemove(id, out _);   // drop the transient combat scratch so no stale loot survives the reset
                return Results.Ok(new { ok = true });
            });
        });

        app.MapGet("/api/logs", (HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            int n = int.TryParse(ctx.Request.Query["lines"], out var x) ? x : 200;
            try
            {
                string[] all;
                lock (d.TraceLock) all = File.Exists(d.TraceFile) ? File.ReadAllLines(d.TraceFile) : Array.Empty<string>();
                return Results.Json(new { lines = all.Where(l => l.Length < 1500).TakeLast(n).ToArray() });
            }
            catch { return Results.Json(new { lines = Array.Empty<string>() }); }
        });

        // ── Save-states (template-account snapshots). Clone the live dev account's durable graph to/from a named
        //    template via the SAME mapper the game uses, so a restore drops you straight back into that tutorial step.
        app.MapGet("/api/snapshots", async (GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            return Results.Json(await db.Accounts.Where(a => a.IsTemplate)
                .Select(a => new { name = a.SnapshotName, a.DisplayName,
                    heroClass = a.Heroes.Select(h => h.HeroClass).FirstOrDefault(),
                    heroLevel = a.Heroes.Select(h => h.Level).FirstOrDefault() })
                .ToListAsync());
        });

        app.MapPost("/api/snapshots", async (SnapshotReq req, GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "name required" });
            return await Gated(devAccountId, async () =>   // snapshot reads the LIVE graph — don't capture it torn mid game-save
            {
                var src = await SnapGraph(db.Accounts).FirstOrDefaultAsync(a => a.AccountId == devAccountId && !a.IsTemplate);
                if (src is null) return Results.NotFound(new { error = "no live account to snapshot — boot the game first" });
                var state = AccountMapper.ToAccountState(src, new AttackScratch());
                var tpl = await SnapGraph(db.Accounts).FirstOrDefaultAsync(a => a.IsTemplate && a.SnapshotName == req.Name);
                if (tpl is null)
                {
                    var minId = await db.Accounts.Where(a => a.IsTemplate).Select(a => (long?)a.AccountId).MinAsync() ?? 0;
                    tpl = new Account { AccountId = Math.Min(minId, 0) - 1 };   // templates live in the negative id space
                    db.Accounts.Add(tpl);
                }
                AccountMapper.ApplyTo(state, tpl);
                tpl.IsTemplate = true; tpl.SnapshotName = req.Name;
                await db.SaveChangesAsync();
                return Results.Ok(new { ok = true });
            });
        });

        app.MapPost("/api/snapshots/{name}/restore", async (string name, GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            return await Gated(devAccountId, async () =>   // restore overwrites the LIVE graph — serialize against an in-flight game save
            {
                var tpl = await SnapGraph(db.Accounts).FirstOrDefaultAsync(a => a.IsTemplate && a.SnapshotName == name);
                if (tpl is null) return Results.NotFound();
                var state = AccountMapper.ToAccountState(tpl, new AttackScratch());
                var live = await SnapGraph(db.Accounts).FirstOrDefaultAsync(a => a.AccountId == devAccountId && !a.IsTemplate);
                if (live is null) { live = new Account { AccountId = devAccountId }; db.Accounts.Add(live); }
                AccountMapper.ApplyTo(state, live);
                live.IsTemplate = false; live.SnapshotName = null;
                await db.SaveChangesAsync();
                return Results.Ok(new { ok = true });
            });
        });

        app.MapDelete("/api/snapshots/{name}", async (string name, GameDbContext db, HttpContext ctx) =>
        {
            if (!Authed(ctx)) return Results.Unauthorized();
            var tpl = await db.Accounts.FirstOrDefaultAsync(a => a.IsTemplate && a.SnapshotName == name);
            if (tpl is not null) { db.Accounts.Remove(tpl); await db.SaveChangesAsync(); }
            return Results.Ok(new { ok = true });
        });
    }
}

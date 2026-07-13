using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MQEL.Core.Model;
using MQEL.Data.Persistence;
using Xunit;

namespace MQEL.Tests;

// Persistence round-trip tests: state -> entity (ApplyTo) -> SQLite -> repository reload (WithGraph) ->
// state (ToAccountState). These exercise the exact seam where the two bugs the review confirmed live.
// A single round-trip test here would have caught both fableReview §1.1 and §1.6/§2.1 immediately.
public class AccountMapperRoundTripTests
{
    // One shared in-memory SQLite connection per test; a SECOND DbContext on it forces a real DB load
    // (not a first-level-cache hit), so the include graph is genuinely exercised.
    static SqliteConnection OpenDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    static GameDbContext Ctx(SqliteConnection conn) =>
        new(new DbContextOptionsBuilder<GameDbContext>().UseSqlite(conn).Options);

    [Fact]  // §1.1 — reward materials MUST survive a save->reload; without the WithGraph Include they load empty.
    public async Task CraftingMaterials_survive_repository_reload()
    {
        using var conn = OpenDb();

        using (var ctx = Ctx(conn))
        {
            var state = new AccountState { AccountId = 42, DisplayName = "T" };
            state.CraftingMaterials[1002] = 3;              // Defenderidium x3, earned from an objective
            var entity = new Account { AccountId = 42 };
            AccountMapper.ApplyTo(state, entity);
            ctx.Accounts.Add(entity);
            await ctx.SaveChangesAsync();
        }

        using var ctx2 = Ctx(conn);
        var loaded = await new EfAccountRepository(ctx2).GetAsync(42);
        Assert.NotNull(loaded);
        var reState = AccountMapper.ToAccountState(loaded!, new AttackScratch());
        Assert.Equal(3, reState.CraftingMaterials.GetValueOrDefault(1002));   // 0 here == the §1.1 data-loss bug
    }

    [Fact]  // §2.1 — a consumable inbox item round-trips as {StackCount,TemplateId} / ItemType 4, never as gear.
    public async Task Consumable_inbox_item_round_trips_as_consumable()
    {
        using var conn = OpenDb();
        const string oid = "0000000000000000000000aa";

        using (var ctx = Ctx(conn))
        {
            var state = new AccountState { AccountId = 7, DisplayName = "T" };
            state.Inbox[oid] = new JsonObject { ["StackCount"] = 5, ["TemplateId"] = 1002 };
            var entity = new Account { AccountId = 7 };
            AccountMapper.ApplyTo(state, entity);

            var row = Assert.Single(entity.Inventory);   // persisted shape must be a consumable, not gear
            Assert.Equal(4, row.ItemType);
            Assert.Equal(5, row.StackCount);

            ctx.Accounts.Add(entity);
            await ctx.SaveChangesAsync();
        }

        using var ctx2 = Ctx(conn);
        var loaded = await new EfAccountRepository(ctx2).GetAsync(7);
        var reState = AccountMapper.ToAccountState(loaded!, new AttackScratch());

        Assert.True(reState.Inbox.ContainsKey(oid));
        var item = reState.Inbox[oid];
        Assert.Equal(5, item["StackCount"]!.GetValue<int>());     // StackCount preserved (the old path dropped it)
        Assert.Equal(1002, item["TemplateId"]!.GetValue<int>());
        Assert.Null(item["PrimaryStatsModifiers"]);               // NOT reconstituted as a HeroEquipmentItem
    }

    [Fact]  // gear inbox items keep round-tripping as gear (guards the §2.1 branch from over-reaching).
    public async Task Gear_inbox_item_round_trips_as_gear()
    {
        using var conn = OpenDb();
        const string oid = "0000000000000000000000bb";

        using (var ctx = Ctx(conn))
        {
            var state = new AccountState { AccountId = 9, DisplayName = "T" };
            state.Inbox[oid] = new JsonObject
            {
                ["Type"] = "HeroEquipmentItem", ["ItemLevel"] = 4, ["ArchetypeId"] = 111,
                ["PrimaryStatsModifiers"] = new JsonArray(1.0, 2.0, 3.0), ["TemplateId"] = 555, ["DyeTemplateId"] = 0,
            };
            var entity = new Account { AccountId = 9 };
            AccountMapper.ApplyTo(state, entity);
            Assert.Equal(3, Assert.Single(entity.Inventory).ItemType);
            ctx.Accounts.Add(entity);
            await ctx.SaveChangesAsync();
        }

        using var ctx2 = Ctx(conn);
        var loaded = await new EfAccountRepository(ctx2).GetAsync(9);
        var item = AccountMapper.ToAccountState(loaded!, new AttackScratch()).Inbox[oid];
        Assert.Equal(555, item["TemplateId"]!.GetValue<int>());
        Assert.NotNull(item["PrimaryStatsModifiers"]);
    }
}

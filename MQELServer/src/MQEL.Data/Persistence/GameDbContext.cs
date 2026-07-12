using Microsoft.EntityFrameworkCore;
using MQEL.Core.Model;

namespace MQEL.Data.Persistence;

/// <summary>
/// The relational model — entities → tables, mapped here via the Fluent API (the entities stay POCO, so
/// MQEL.Core carries no EF dependency). Provider-agnostic: the same model maps to SQLite or PostgreSQL.
/// </summary>
public class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Hero> Heroes => Set<Hero>();
    public DbSet<HeroGearItem> HeroGear => Set<HeroGearItem>();
    public DbSet<HeroSpell> HeroSpells => Set<HeroSpell>();
    public DbSet<HeroConsumable> HeroConsumables => Set<HeroConsumable>();
    public DbSet<HeroInventoryItem> HeroInventory => Set<HeroInventoryItem>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Castle> Castles => Set<Castle>();
    public DbSet<CastleRoom> CastleRooms => Set<CastleRoom>();
    public DbSet<CastleBuilding> CastleBuildings => Set<CastleBuilding>();
    public DbSet<CompletedAssignment> CompletedAssignments => Set<CompletedAssignment>();
    public DbSet<Objective> Objectives => Set<Objective>();
    public DbSet<CraftingMaterial> CraftingMaterials => Set<CraftingMaterial>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.HasKey(x => x.AccountId);
            e.Property(x => x.AccountId).ValueGeneratedNever();          // ids are assigned by us
            e.HasIndex(x => x.SteamId).IsUnique();                       // nullable -> multiple NULLs allowed (SQLite + PG)
            e.HasMany(x => x.Wallets).WithOne().HasForeignKey(w => w.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Heroes).WithOne().HasForeignKey(h => h.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Inventory).WithOne().HasForeignKey(i => i.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Castle).WithOne().HasForeignKey<Castle>(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.CompletedAssignments).WithOne().HasForeignKey(a => a.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Objectives).WithOne().HasForeignKey(o => o.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CraftingMaterials).WithOne().HasForeignKey(m => m.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Wallet>().HasKey(x => new { x.AccountId, x.CurrencyType });

        b.Entity<Hero>(e =>
        {
            e.HasKey(x => x.HeroId);
            e.HasIndex(x => new { x.AccountId, x.HeroClass }).IsUnique();
            e.HasMany(x => x.Gear).WithOne().HasForeignKey(g => g.HeroId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Spells).WithOne().HasForeignKey(s => s.HeroId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Consumables).WithOne().HasForeignKey(c => c.HeroId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Inventory).WithOne().HasForeignKey(iv => iv.HeroId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<HeroGearItem>().HasKey(x => new { x.HeroId, x.Slot });
        b.Entity<HeroSpell>().HasKey(x => new { x.HeroId, x.SpellId });
        b.Entity<HeroConsumable>().HasKey(x => new { x.HeroId, x.TemplateId });
        b.Entity<HeroInventoryItem>().HasKey(x => new { x.HeroId, x.Slot });

        b.Entity<InventoryItem>().HasKey(x => x.ObjectId);

        b.Entity<Castle>(e =>
        {
            e.HasKey(x => x.CastleId);
            e.HasMany(x => x.Rooms).WithOne().HasForeignKey(r => r.CastleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CastleRoom>(e =>
        {
            e.HasKey(x => x.RoomId);
            e.HasMany(x => x.Buildings).WithOne().HasForeignKey(bl => bl.RoomId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CastleBuilding>().HasKey(x => x.BuildingId);

     b.Entity<CompletedAssignment>().HasKey(x => new { x.AccountId, x.AssignmentId });
        b.Entity<Objective>().HasKey(x => new { x.AccountId, x.ObjectiveId });
        b.Entity<CraftingMaterial>().HasKey(x => new { x.AccountId, x.MaterialId });
    }
}

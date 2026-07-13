using Microsoft.EntityFrameworkCore;
using MQEL.Core.Model;

namespace MQEL.Data.Persistence;

/// <summary>
/// THE single definition of "load the whole account aggregate". Both the game-path repository
/// (<see cref="EfAccountRepository"/>) and the admin snapshot/reset endpoints load through this, so the
/// include list exists exactly once. A missing Include here can no longer silently diverge between the two
/// paths — that drift is precisely what dropped CraftingMaterials from the game-path load (fableReview §1.1/§3.5).
/// AsSplitQuery avoids the cartesian blow-up of many Includes on one query.
/// </summary>
public static class AccountGraph
{
    public static IQueryable<Account> Includes(IQueryable<Account> q) => q
        .Include(a => a.Wallets)
        .Include(a => a.Heroes).ThenInclude(h => h.Gear)
        .Include(a => a.Heroes).ThenInclude(h => h.Spells)
        .Include(a => a.Heroes).ThenInclude(h => h.Consumables)
        .Include(a => a.Heroes).ThenInclude(h => h.Inventory)
        .Include(a => a.Inventory)
        .Include(a => a.Castle!).ThenInclude(c => c.Rooms).ThenInclude(r => r.Buildings)
        .Include(a => a.CompletedAssignments)
        .Include(a => a.Objectives)
        .Include(a => a.CraftingMaterials)
        .AsSplitQuery();
}

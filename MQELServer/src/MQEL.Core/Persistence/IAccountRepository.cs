using MQEL.Core.Model;

namespace MQEL.Core.Persistence;

/// <summary>
/// The storage black box for the account aggregate. The server calls ONLY this — the database behind it
/// (EF Core/SQLite today, PostgreSQL later) is swappable without touching any caller. <see cref="GetAsync"/>
/// returns a tracked graph: mutate it in place and call <see cref="SaveAsync"/> (adds/updates/deletes are
/// all persisted). Metagame queries (PvP target search, leaderboards) will be added here as typed methods.
/// </summary>
public interface IAccountRepository
{
    /// <summary>Load the full account graph (wallets, heroes+gear/spells/consumables, inventory, castle,
    /// progress), tracked for in-place mutation. Null if absent.</summary>
    Task<Account?> GetAsync(long accountId, CancellationToken ct = default);

    /// <summary>Load by external identity (the SteamID), same full graph.</summary>
    Task<Account?> GetBySteamIdAsync(string steamId, CancellationToken ct = default);

    /// <summary>Persist the account — inserts if new, otherwise flushes tracked changes on the graph.</summary>
    Task SaveAsync(Account account, CancellationToken ct = default);

    Task<bool> ExistsAsync(long accountId, CancellationToken ct = default);
}

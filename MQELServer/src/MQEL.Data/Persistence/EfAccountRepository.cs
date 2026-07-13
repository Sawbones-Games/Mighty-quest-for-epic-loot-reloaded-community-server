using Microsoft.EntityFrameworkCore;
using MQEL.Core.Model;
using MQEL.Core.Persistence;

namespace MQEL.Data.Persistence;

/// <summary>EF Core implementation of the account black box. Scoped (uses the per-request DbContext).</summary>
public sealed class EfAccountRepository : IAccountRepository
{
    readonly GameDbContext _db;
    public EfAccountRepository(GameDbContext db) => _db = db;

    // Load the whole aggregate through the single shared include list (AccountGraph) so this path and the
    // admin snapshot path can't drift apart (the drift that lost CraftingMaterials — fableReview §1.1/§3.5).
    static IQueryable<Account> WithGraph(IQueryable<Account> q) => AccountGraph.Includes(q);

    public Task<Account?> GetAsync(long accountId, CancellationToken ct = default) =>
        WithGraph(_db.Accounts).FirstOrDefaultAsync(a => a.AccountId == accountId, ct);

    public Task<Account?> GetBySteamIdAsync(string steamId, CancellationToken ct = default) =>
        WithGraph(_db.Accounts).FirstOrDefaultAsync(a => a.SteamId == steamId, ct);

    public async Task SaveAsync(Account account, CancellationToken ct = default)
    {
        if (_db.Entry(account).State == EntityState.Detached)
        {
            // A freshly-built account (not loaded via GetAsync) -> insert the whole graph.
            account.CreatedUtc = account.UpdatedUtc = DateTime.UtcNow;
            _db.Accounts.Add(account);
        }
        else if (!_db.ChangeTracker.HasChanges())
        {
            return;   // read-only request -> nothing changed on the tracked graph, skip the write
        }
        else
        {
            // Tracked (loaded via GetAsync) with real changes: EF sees the in-place adds/updates/deletes.
            account.UpdatedUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> ExistsAsync(long accountId, CancellationToken ct = default) =>
        _db.Accounts.AnyAsync(a => a.AccountId == accountId, ct);
}

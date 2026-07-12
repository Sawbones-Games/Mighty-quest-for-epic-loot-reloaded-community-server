using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MQEL.Data.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations</c> build the context without spinning up the host app. Migrations are a
/// design-time, SQLite-shaped artifact; runtime uses the provider chosen by config (see AddDataLayer).
/// </summary>
public sealed class GameDbContextFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlite("Data Source=mqel.db")
            .Options;
        return new GameDbContext(options);
    }
}

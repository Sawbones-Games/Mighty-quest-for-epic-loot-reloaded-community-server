using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MQEL.Core.Persistence;

namespace MQEL.Data.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the data layer: <see cref="GameDbContext"/> on the configured provider, plus the repository
    /// black box the server depends on. To move to Postgres: add the Npgsql package and a <c>case "postgres"</c>
    /// below — the repositories and every caller stay untouched.
    /// </summary>
    public static IServiceCollection AddDataLayer(this IServiceCollection services, StorageOptions options)
    {
        services.AddDbContext<GameDbContext>(o =>
        {
            switch (options.Provider.ToLowerInvariant())
            {
                case "sqlite":
                    // §3.3 — DefaultTimeout is Microsoft.Data.Sqlite's lock-retry budget: a writer that hits a
                    // locked DB WAITS up to this long instead of failing instantly with "database is locked"
                    // (the dashboard polls every 3s while game requests write). WAL is enabled once at startup.
                    o.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(options.ConnectionString)
                    { DefaultTimeout = 5 }.ConnectionString);
                    break;
                // case "postgres": o.UseNpgsql(options.ConnectionString); break;  // drop-in: callers untouched
                default: throw new NotSupportedException($"Storage provider '{options.Provider}' is not supported.");
            }
        });
        services.AddScoped<IAccountRepository, EfAccountRepository>();
        services.AddSingleton<IAccountResolver>(new DevAccountResolver(options.DefaultAccountId));
        return services;
    }
}

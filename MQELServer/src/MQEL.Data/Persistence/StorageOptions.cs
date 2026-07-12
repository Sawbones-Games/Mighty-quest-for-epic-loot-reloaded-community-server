namespace MQEL.Data.Persistence;

/// <summary>Binds the "Storage" config section. Keep SQLite until/unless we genuinely need Postgres.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>"Sqlite" today. "Postgres" later maps to <c>UseNpgsql</c> with the same connection string.</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>Provider connection string. SQLite default = a relative, cross-platform file path.</summary>
    public string ConnectionString { get; set; } = "Data Source=mqel.db";

    /// <summary>
    /// The dev test account every request routes to until per-identity routing is turned on. The identity
    /// seam (IAccountResolver) already runs per request; this is just its current default.
    /// </summary>
    public long DefaultAccountId { get; set; } = 3123971;
}

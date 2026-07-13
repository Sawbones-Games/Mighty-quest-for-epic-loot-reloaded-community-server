using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MQEL.Tests;

// Boots the REAL server pipeline in-process (TestServer, so Kestrel/HTTPS/cert config in appsettings is inert)
// on a throwaway SQLite file and temp capture/audit/trace dirs. Content root + CWD are pointed at the
// MQEL.Gameserver project dir so the app resolves responses/ and walks up to the repo-root spec DB exactly as
// it does in production. Each instance = a fresh first-run account, so responses are a pure function of the
// request sequence — which is what lets the golden files pin behaviour across the Program.cs refactor.
public sealed class WireGoldenFactory : WebApplicationFactory<Program>
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mqel-wiretest-{Guid.NewGuid():N}.db");
    readonly string _capDir = Path.Combine(Path.GetTempPath(), $"mqel-wiretest-{Guid.NewGuid():N}");

    public static string GameserverDir([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "src", "MQEL.Gameserver"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var gs = GameserverDir();
        Directory.SetCurrentDirectory(gs);   // RespFile()/castle files/spec-DB walk-up all resolve relative to CWD
        Directory.CreateDirectory(_capDir);
        builder.UseContentRoot(gs);
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:ConnectionString", $"Data Source={_dbPath}");
        builder.UseSetting("Storage:Provider", "Sqlite");
        builder.UseSetting("Capture:Directory", _capDir);   // Trace:File + Audit:Directory default under this
        builder.UseSetting("Admin:Token", "");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_capDir, recursive: true); } catch { }
    }
}

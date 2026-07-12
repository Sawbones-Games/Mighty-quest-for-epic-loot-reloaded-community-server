using System.Text.Json;
using System.Text.Json.Nodes;

// Launcher/patcher boot shims — the pre-game handshake (version checks + the Steam login page). None of these
// touch account state, so they live outside the game catch-all's per-account machinery.
static class LauncherEndpoints
{
    /// <summary>Handle a launcher/patcher path; returns null if <paramref name="path"/> isn't one of ours.</summary>
    public static async Task<IResult?> TryHandle(string path, HttpContext ctx, JsonSerializerOptions jsonOpts)
    {
        // "Checking Game Packages Version": POST GetRMLauncherAndPackagesVersion -> response contract
        // `RMLauncherAndServerPackagesVersion` (launcher version + server's designated package versions; the
        // client compares to its own). Echo the client's own rmClientPackagesVersion back as the server's
        // designated set so everything matches -> up to date. (Check this BEFORE GetRMLauncherVersion: distinct.)
        if (path.Contains("GetRMLauncherAndPackagesVersion", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Request.Body.Position = 0;
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            var reqJson = await reader.ReadToEndAsync();
            var lver = "276072";
            var env = "mqel-live";
            var clientLabel = "0.36.1.34.0";
            try
            {
                using var doc = JsonDocument.Parse(reqJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("launcherVersionName", out var lv)) lver = lv.GetString() ?? lver;
                if (root.TryGetProperty("environmentName", out var en)) env = en.GetString() ?? env;
                if (root.TryGetProperty("rmClientPackagesVersion", out var cpv)
                    && cpv.TryGetProperty("ClientGamePublicationLabel", out var cl))
                    clientLabel = cl.GetString() ?? clientLabel;
            }
            catch { /* keep defaults; capture already logged the raw body */ }

            // Response contract (from the PublicLauncher.exe serializers):
            //   RMLauncherAndServerPackagesVersion { RMLauncherPatch, RMServerPackagesVersion }
            //   RMServerPackagesVersion            { BranchName, GamePublicationLabel, RMPackagePatches[] }
            //   RMPackagePatch { FullDownloadSize, FullInstallUrl, PatchDownloadSize, PatchInstallUrl,
            //                    RMPackagePatchFlags(int enum), RMPackageVersion }
            //   RMPackagePatchFlags: None=0, CanInstallFullInstall=1, CanInstallPatch=2, ClientVersionUpToDate=4,
            //                    PackageDeleted=8, UpdateVersionNumberOnly=16
            //
            // The Steam-distributed launcher delegates all game-file updates to Steam and ignores a non-empty
            // RMPackagePatches (it shows a "update via the Steam client" message box instead), so we report
            // everything up-to-date (empty RMPackagePatches, echo the client's own publication label) to show
            // Play cleanly.
            var body = new JsonObject
            {
                ["RMLauncherPatch"] = new JsonObject { ["VersionName"] = lver },
                ["RMServerPackagesVersion"] = new JsonObject
                {
                    ["BranchName"] = env,
                    ["GamePublicationLabel"] = clientLabel,   // echo client's label -> matches -> up to date
                    ["RMPackagePatches"] = new JsonArray(),   // nothing to patch
                },
            };
            return Results.Content(body.ToJsonString(jsonOpts), "application/json");
        }

        // TaskCheckLauncherVersion: response deserializes into contract `RMLauncherPatch`; the field it reads is
        // `VersionName`. Echo the launcher's own version so it reads 2SigVersionUpToDate and advances.
        if (path.Contains("GetRMLauncherVersion", StringComparison.OrdinalIgnoreCase))
        {
            var v = ctx.Request.Query["versionName"].FirstOrDefault() ?? "276072";
            return Results.Content(new JsonObject { ["VersionName"] = v }.ToJsonString(jsonOpts), "application/json");
        }

        // Login page (loaded into the launcher's #remote-launcher-pages iframe). The launcher reads
        // window.userIsLoggedIn + cookie "t" (LoginToken), then calls _onUserLoggedIn({LoginToken, SGToken, UserEmail}).
        // We accept the Steam identity, mint a session token, set the cookies, and report logged-in.
        // TODO(correctness): validate the Steam ticket via the Steam Web API; persist the minted token so the
        // gameserver can authenticate the game's connection against it.
        if (path.Contains("/launcher/load", StringComparison.OrdinalIgnoreCase))
        {
            // steamID lands in a cookie value; strip anything non-alphanumeric so a crafted query string can't
            // inject cookie attributes / break the header (Steam IDs are numeric, so this is loss-free for real input).
            var steamIdRaw = ctx.Request.Query["steamID"].FirstOrDefault() ?? "unknown";
            var steamId = new string(steamIdRaw.Where(char.IsLetterOrDigit).ToArray());
            if (steamId.Length == 0) steamId = "unknown";
            var cookie = new CookieOptions { Path = "/", HttpOnly = false };   // JS must read these via document.cookie
            // Token format: the game's ServerSignInManager copies the ConnectionToken into a fixed 0x19 (=25-byte)
            // slot (mgr+0x410) — a 24-char token + NUL. A 32-char Guid("N") overflows that slot, so mint 24.
            ctx.Response.Cookies.Append("t", Guid.NewGuid().ToString("N")[..24], cookie);                       // LoginToken (24 hex)
            ctx.Response.Cookies.Append("hyperquest_launcher_session", Guid.NewGuid().ToString("N")[..24], cookie); // SGToken (24 hex, by analogy)
            ctx.Response.Cookies.Append("email", $"{steamId}@mqel.local", cookie);
            const string html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>" +
                                "<body><form></form><script>window.userIsLoggedIn = true;</script></body></html>";
            return Results.Content(html, "text/html; charset=utf-8");
        }

        return null;   // not a launcher path
    }
}

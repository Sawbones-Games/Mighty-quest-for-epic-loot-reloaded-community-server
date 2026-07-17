using System.Text.Json.Nodes;

// Locates + loads the server's OWN packed game data (gamedata/*.json, built by tools/pack_gamedata.py and
// committed). This is the ONLY game-design data source the runtime touches: nothing here — or in any catalog —
// may read game-data/settings-extracted, which is a local, gitignored decrypt of Ubisoft's settings.bin and is
// absent on every machine but the one that ran the decrypt.
//
// Why this class exists at all: the catalogs used to walk up from Directory.GetCurrentDirectory() looking for
// that extraction, and a miss returned an EMPTY catalog with no log line. On a fresh clone that silently
// disabled every objective — the "all the boxes get checked but no reward is given" report. So the two rules
// here are: the pack ships next to the exe (AppContext.BaseDirectory, copied by the .csproj), and a missing or
// unreadable pack is FATAL. A gameserver that cannot score objectives must not boot pretending it can.
static class GameData
{
    // Bumped when the pack's shape changes; tools/pack_gamedata.py stamps FORMAT_VERSION into every file.
    // A mismatch means the exe and the committed pack are out of step — fatal, not a warning.
    public const int FormatVersion = 1;

    const string DirName = "gamedata";

    // BaseDirectory first: the pack is a build output copied next to the exe, so this is correct regardless of
    // where the server is launched from. The CWD probe is only a dev convenience (running against the source
    // tree without a rebuild) and is deliberately second.
    public static string FindDir()
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (root is null) continue;
            var candidate = Path.Combine(root, DirName);
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new GameDataException(
            $"packed game data not found — looked for a '{DirName}' directory in:\n" +
            $"  {Path.Combine(AppContext.BaseDirectory, DirName)}\n" +
            $"  {Path.Combine(Directory.GetCurrentDirectory(), DirName)}\n" +
            "The pack is committed and copied to the output dir by MQEL.Gameserver.csproj; a missing pack\n" +
            "usually means an incomplete build or a partial copy. Rebuild, or regenerate it with:\n" +
            "  python tools/pack_gamedata.py");
    }

    // Parse one pack file and enforce its FormatVersion. Callers get the root object and read their own section.
    public static JsonObject Load(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new GameDataException($"missing pack file: {path}\nRegenerate with: python tools/pack_gamedata.py");

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new GameDataException($"pack file is not a JSON object: {path}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new GameDataException($"pack file is corrupt: {path}\n{ex.Message}");
        }

        var version = (int?)root["FormatVersion"] ?? 0;
        if (version != FormatVersion)
            throw new GameDataException(
                $"pack format mismatch in {path}: file is v{version}, server expects v{FormatVersion}.\n" +
                "Re-run: python tools/pack_gamedata.py");
        return root;
    }
}

// Fatal, actionable game-data failure. Thrown during startup catalog load; never caught into an empty catalog.
sealed class GameDataException : Exception
{
    public GameDataException(string message) : base(message) { }
}

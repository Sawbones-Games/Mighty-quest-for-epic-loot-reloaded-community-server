using System.Text.Json.Nodes;

// Resolves an (AssignmentId, ActionIndex) pair back to the actual AssignmentActionSpec from the decrypted spec
// DB. Needed because the client's ExecuteAssignmentActionCommand carries ONLY {AssignmentId, ActionIndex}
// (command-queue.md §5.7) — never the action's payload — so the server must already know what that action IS to
// react to it (e.g. SetCastleRenovationLevelAssignmentActionSpec{CastleRenovationLevel}). Folder names encode the
// id ("005007 - Castle_Crafting_1st_Floor_Complete"); parsed once at load. Files are parsed lazily + cached on
// first lookup — 100+ assignments exist, most are never queried.
sealed class AssignmentCatalog
{
    readonly Dictionary<int, string> _pathById = new();
    readonly Dictionary<int, JsonArray> _actionsById = new();

    public static AssignmentCatalog Load(string specRoot)
    {
        var c = new AssignmentCatalog();
        var dir = Path.Combine(specRoot, "GameplaySettings", "Assignments");
        foreach (var folder in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(folder);
            int sep = name.IndexOf(" - ", StringComparison.Ordinal);
            var idPart = sep >= 0 ? name[..sep] : name;
            if (int.TryParse(idPart, out var id))
                c._pathById[id] = folder;
        }
        return c;
    }

    // Returns the JsonObject at Actions[actionIndex] for the given assignment, or null if the assignment/index
    // is unknown. Callers check its "$type" before acting on any field.
    public JsonObject? GetAction(int assignmentId, int actionIndex)
    {
        if (!_actionsById.TryGetValue(assignmentId, out var actions))
        {
            if (!_pathById.TryGetValue(assignmentId, out var folder)) return null;
            var file = Path.Combine(folder, "GAMEPLAY.JSON");
            actions = File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonArray doc
                ? (doc[0]?["Actions"] as JsonArray ?? new JsonArray())
                : new JsonArray();
            _actionsById[assignmentId] = actions;
        }
        return actionIndex >= 0 && actionIndex < actions.Count ? actions[actionIndex] as JsonObject : null;
    }

    public static string? FindSpecRoot() => ItemCatalog.FindSpecRoot();
}

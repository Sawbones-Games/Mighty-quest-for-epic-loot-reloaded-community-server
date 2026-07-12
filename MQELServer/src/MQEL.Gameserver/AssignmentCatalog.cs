using System.Text.Json.Nodes;

// Resolves an (AssignmentId, ActionIndex) pair back to the actual AssignmentActionSpec. Needed because the
// client's ExecuteAssignmentActionCommand carries ONLY {AssignmentId, ActionIndex} — never the action's
// payload — so the server must already know what that action IS to react to it (e.g.
// SetCastleRenovationLevelAssignmentActionSpec{CastleRenovationLevel}). Each assignment is one file under
// data/assignments/<id>.json (the assignment's original gameplay array); ids are read from the file names
// once at load. Files are parsed lazily + cached on first lookup — most assignments are never queried.
sealed class AssignmentCatalog
{
    readonly Dictionary<int, string> _pathById = new();
    readonly Dictionary<int, JsonArray> _actionsById = new();

    public static AssignmentCatalog Load(string dataRoot)
    {
        var c = new AssignmentCatalog();
        var dir = Path.Combine(dataRoot, "assignments");
        if (!Directory.Exists(dir)) return c;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            if (int.TryParse(Path.GetFileNameWithoutExtension(file), out var id))
                c._pathById[id] = file;
        return c;
    }

    // Returns the JsonObject at Actions[actionIndex] for the given assignment, or null if the assignment/index
    // is unknown. Callers check its "$type" before acting on any field.
    public JsonObject? GetAction(int assignmentId, int actionIndex)
    {
        if (!_actionsById.TryGetValue(assignmentId, out var actions))
        {
            if (!_pathById.TryGetValue(assignmentId, out var file)) return null;
            actions = File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonArray doc
                ? (doc[0]?["Actions"] as JsonArray ?? new JsonArray())
                : new JsonArray();
            _actionsById[assignmentId] = actions;
        }
        return actionIndex >= 0 && actionIndex < actions.Count ? actions[actionIndex] as JsonObject : null;
    }

    public static string? FindDataRoot() => ItemCatalog.FindDataRoot();
}

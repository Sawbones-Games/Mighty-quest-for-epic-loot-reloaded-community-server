using System.Text.Json.Nodes;

// Resolves an (AssignmentId, ActionIndex) pair back to an assignment action, from the server's packed game data
// (gamedata/assignments.json — see GameData). Needed because the client's ExecuteAssignmentActionCommand carries
// ONLY {AssignmentId, ActionIndex} (command-queue.md §5.7) — never the action's payload — so the server must
// already know what that action IS to react to it (e.g. SetCastleRenovationLevelAssignmentActionSpec).
//
// Actions are POSITIONAL: ActionIndex indexes the packed array, so the packer emits an entry for EVERY action
// (bare {"Type"} for ones we don't model) rather than filtering — filtering would shift later indices. Only the
// action types the server acts on carry a payload; add fields in tools/pack_gamedata.py and re-pack if that grows.
sealed class AssignmentCatalog
{
    // One action: its spec type (short name, e.g. "SetCastleRenovationLevelAssignmentActionSpec") plus the
    // payload fields the server reads. Null payload = an action we don't model; callers check Type first.
    public sealed record Action(string Type, string? CastleRenovationLevel);

    readonly Dictionary<int, IReadOnlyList<Action>> _actionsById = new();

    public int Count => _actionsById.Count;

    public static AssignmentCatalog Load(string dataDir)
    {
        var c = new AssignmentCatalog();
        var root = GameData.Load(dataDir, "assignments.json");

        foreach (var kv in root["Assignments"]?.AsObject() ?? new JsonObject())
        {
            if (kv.Value is not JsonObject a || !int.TryParse(kv.Key, out var id)) continue;
            var actions = new List<Action>();
            foreach (var an in a["Actions"]?.AsArray() ?? new JsonArray())
                actions.Add(an is JsonObject ao
                    ? new Action((string?)ao["Type"] ?? "", (string?)ao["CastleRenovationLevel"])
                    : new Action("", null));
            c._actionsById[id] = actions;
        }
        return c;
    }

    // The action at Actions[actionIndex] for the given assignment, or null if the assignment/index is unknown.
    public Action? GetAction(int assignmentId, int actionIndex) =>
        _actionsById.TryGetValue(assignmentId, out var actions)
        && actionIndex >= 0 && actionIndex < actions.Count
            ? actions[actionIndex]
            : null;
}

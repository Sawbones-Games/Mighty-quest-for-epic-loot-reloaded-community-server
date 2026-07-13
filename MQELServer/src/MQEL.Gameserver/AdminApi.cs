// Request body for POST /api/accounts/{id} — the admin-dashboard account editor.
// Bound from JSON by the minimal API (property names match case-insensitively).
// §1.5 — every field is NULLABLE and applied only when present. A partial-update POST (e.g. a hand-rolled
// curl that sends just DisplayName) must NOT bind the omitted numeric fields to 0 and thereby zero the
// wallet / de-level the hero. The dashboard sends every field, so the non-null path is unchanged.
public record AccountEdit(
    string? DisplayName,
    long? Gold,
    long? LifeForce,
    int? HeroLevel,
    int? HeroXp,
    int[]? CompletedAssignments);

// Request body for POST /api/snapshots — capture the live account under this name.
public record SnapshotReq(string? Name);

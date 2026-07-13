using System.Text.Json;

namespace MQEL.Core.Verification;

/// <summary>
/// Everything needed to (eventually) re-simulate and verify a completed attack.
///
/// Combat runs client-side and deterministically from <see cref="AttackRandomSeed"/>, so for now the
/// server's only obligation is to <b>persist</b> this bundle — the verification compute is stubbed
/// (see <see cref="IVerificationService"/>). Deferring the compute is fine; dropping the bundle would be
/// the one real shortcut (you could never verify retroactively), so the audit substrate is built from day 1.
///
/// The opaque <see cref="JsonElement"/> members hold shapes we have not yet captured from the real client.
/// They are stored verbatim rather than guessed; once First Contact captures the real traffic they get
/// promoted to strongly-typed models.
/// </summary>
public sealed record AuditBundle
{
    public required long AttackId { get; init; }
    public required long AttackerAccountId { get; init; }
    public long? DefenderAccountId { get; init; }

    /// <summary>The seed the server issued for this attack; the fight is a pure function of it.</summary>
    public required long AttackRandomSeed { get; init; }

    /// <summary>Defender castle layout exactly as served at attack time (opaque until captured).</summary>
    public JsonElement CastleSnapshot { get; init; }

    /// <summary>Attacker hero / gear / consumables as served at attack time (opaque until captured).</summary>
    public JsonElement AttackerLoadout { get; init; }

    /// <summary>Outcome the client reported: stars, completion, stolen amounts (opaque until captured).</summary>
    public JsonElement ClaimedResult { get; init; }

    /// <summary>Recorded input stream / replay that reproduces the fight (opaque until captured).</summary>
    public JsonElement Replay { get; init; }

    public required DateTimeOffset SubmittedAtUtc { get; init; }
}

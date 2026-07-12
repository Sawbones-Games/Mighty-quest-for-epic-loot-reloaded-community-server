using Microsoft.Extensions.Logging;
using MQEL.Core.Verification;

namespace MQEL.Verification;

/// <summary>
/// Step-1 stub: accepts the full <see cref="AuditBundle"/>, performs NO checking, returns valid=true.
/// The seam is real, so swapping in genuine replay re-simulation later changes no caller and no schema.
/// </summary>
public sealed class StubVerificationService : IVerificationService
{
    private readonly ILogger<StubVerificationService> _log;

    public StubVerificationService(ILogger<StubVerificationService> log) => _log = log;

    public Task<VerificationVerdict> VerifyAsync(AuditBundle bundle, CancellationToken ct = default)
    {
        _log.LogInformation(
            "AUDIT received: AttackId={AttackId} Attacker={Attacker} Defender={Defender} Seed={Seed} at {At}. " +
            "Stub verification — returning valid=true (no checking performed).",
            bundle.AttackId, bundle.AttackerAccountId, bundle.DefenderAccountId,
            bundle.AttackRandomSeed, bundle.SubmittedAtUtc);

        return Task.FromResult(VerificationVerdict.Ok("stub: no checking performed"));
    }
}

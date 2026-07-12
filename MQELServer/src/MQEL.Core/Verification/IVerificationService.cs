namespace MQEL.Core.Verification;

/// <summary>
/// Audits a completed attack. Today this is a stub that returns <c>Valid=true</c>
/// (see <c>MQEL.Verification.StubVerificationService</c>).
///
/// It is deliberately a clean seam so the trusting-now / verify-later swap needs <b>no</b> protocol or
/// schema change. A future implementation can become its own service that spins up native-simulation
/// instances, replays the recorded inputs from <see cref="AuditBundle.AttackRandomSeed"/>, and confirms
/// the claimed result. The gameserver persists the <see cref="AuditBundle"/> regardless of the verdict.
/// </summary>
public interface IVerificationService
{
    Task<VerificationVerdict> VerifyAsync(AuditBundle bundle, CancellationToken ct = default);
}

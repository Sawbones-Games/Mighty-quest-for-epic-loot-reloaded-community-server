namespace MQEL.Core.Verification;

/// <summary>Result of auditing an <see cref="AuditBundle"/>.</summary>
public sealed record VerificationVerdict(bool Valid, string? Reason = null)
{
    public static VerificationVerdict Ok(string? reason = null) => new(true, reason);
    public static VerificationVerdict Reject(string reason) => new(false, reason);
}

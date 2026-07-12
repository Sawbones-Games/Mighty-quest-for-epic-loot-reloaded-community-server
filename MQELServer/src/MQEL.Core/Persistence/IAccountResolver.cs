namespace MQEL.Core.Persistence;

/// <summary>
/// The multi-user seam: decides WHICH account a request acts as. Runs per request, so the architecture is
/// already multi-user; only the mapping behind it is dialled up over time. Today it returns a single dev
/// account; next it maps the session token (the launcher's <c>t</c> LoginToken) — or a Steam identity — to a
/// per-player account, which is the only change needed to drive two clients against two accounts (e.g. to
/// test cross-account castle visibility + raiding). Takes plain inputs, not HttpContext, so MQEL.Core stays
/// free of any web dependency — the host extracts them from the request.
/// </summary>
public interface IAccountResolver
{
    /// <param name="sessionToken">The request's <c>t</c> LoginToken (per-client identity); used for routing later.</param>
    /// <param name="devOverride">An explicit account id for manual/test calls (e.g. a <c>?__account=</c> query).</param>
    long Resolve(string? sessionToken, long? devOverride = null);
}

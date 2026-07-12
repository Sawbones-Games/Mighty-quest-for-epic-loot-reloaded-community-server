using MQEL.Core.Persistence;

namespace MQEL.Data.Persistence;

/// <summary>
/// Current identity policy: route everything to one configured dev account (an explicit dev override still
/// wins, for manual/test calls). The per-client routing slots in here later — a session-token → account map
/// (populated at login from the Steam identity) — with no change to any caller, since handlers already
/// resolve through <see cref="IAccountResolver"/> per request.
/// </summary>
public sealed class DevAccountResolver : IAccountResolver
{
    readonly long _devAccountId;
    public DevAccountResolver(long devAccountId) => _devAccountId = devAccountId;

    public long Resolve(string? sessionToken, long? devOverride = null)
        => devOverride ?? _devAccountId;   // TODO multi-user: map sessionToken -> account (session/Steam registry)
}

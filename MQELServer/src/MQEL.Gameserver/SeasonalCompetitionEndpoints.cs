using System.Text.Json.Nodes;

// SeasonalCompetitionService.hqs — the in-game leaderboard (PanelNames.leaderboard = 24, the
// seasonalCompetition UI module). Reachable from the lobby, so a player can open it at any time — including by
// accident, which is how it was reported: "if you accidentally click on the leaderboard, and try to close it, it
// will just immediately pop back up and never go away. You have to restart the game."
//
// We do not run a seasonal competition: nothing scores one, and a private server has no population to rank. The
// honest answer is therefore an EMPTY-but-well-formed leaderboard, generated from the real account — never a
// canned production capture (which would ship real players' names and scores, and be wrong on every field the
// account owns).
//
// "Well-formed" is the load-bearing word. The client's own JS/templates are the contract here: an older
// reference body predates this client build and sends it into an infinite loop. Everything below is pinned to
// what the client UI actually dereferences:
//
//   seasonalCompetitionModule.js  _onGetLeaderboardEntries(a)      — the response consumer
//   Html/en/Index.html            #leaderboard-league-header-template, -progress-bar-, -filters-,
//                                 -top-players-, -league-info-, -event-time-remaining-,
//                                 #leaderboard-container-detailed-template-leaderboard-subpanel
//
// Two fields are dereferenced with NO exists() guard and so may never be omitted: LeaderboardProgressBarModel
// and FilterModel (both read in the IsUnrankedInfoVisible line). Omitting either throws a TypeError inside the
// callback, and the handler's tail never runs — including this._$leaderboardTabs.unlock(), which leaves the lock
// set by openPanel() in place across every later open/close cycle. That alone breaks the panel permanently.
static class SeasonalCompetitionEndpoints
{
    // The account's avatar, matching what GetAccountInformation tells the client
    // (responses/account-information-firstrun.json). AccountState doesn't model an avatar yet; when it does,
    // read it from there instead of this constant.
    const int AvatarId = 10;

    // Leagues/subleagues were BACKEND-owned data — they are not in the decrypted spec DB, and the client ships
    // no league names or icons (the icon URLs resolve against gamedata/Generated/Web, a CDN path with no local
    // asset). So we can't source a real league table from anywhere; we describe the one degenerate league the
    // account is in. Every icon field is "" — each template guards on `IconUrl != ""`, so empty renders as
    // text-only rather than a broken image. Do not invent icon paths: they'd 404 into the UI.
    const int LeagueId = 1;
    const int SubLeagueId = 1;

    public static IResult? TryHandle(string path, AccountState st, GameDeps deps)
    {
        if (!path.Contains("SeasonalCompetitionService.hqs", StringComparison.OrdinalIgnoreCase))
            return null;

        // The panel poll. Static-file-backed until now, and it unconditionally asserted a LeagueUpdated (type 65)
        // + NewsAdded (type 22) on EVERY call — a permanent "your league changed!" that a poll re-delivers
        // forever. Type 65 has no JS handler (it's consumed natively, and native owns the panel stack), which is
        // the leading explanation for the panel re-opening the instant it's closed and never clearing until
        // restart. We have no league to update and no rewards to hand out, so: nothing to notify.
        if (path.Contains("CheckSeasonalCompetitionRewards", StringComparison.OrdinalIgnoreCase))
        {
            deps.WireLog("CheckSeasonalCompetitionRewards -> no rewards (no seasonal competition)");
            return Results.Json(new JsonObject { ["Notifications"] = new JsonArray() }, deps.JsonOpts);
        }

        if (path.Contains("GetSeasonalCompetition", StringComparison.OrdinalIgnoreCase))
        {
            deps.WireLog($"GetSeasonalCompetition -> empty leaderboard for {st.AccountId}");
            return Results.Json(new JsonObject { ["Result"] = BuildLeaderboard(st) }, deps.JsonOpts);
        }

        return null;
    }

    static JsonObject AccountSummary(AccountState st) => new()
    {
        ["Id"] = st.AccountId,
        ["DisplayName"] = st.DisplayName,
        ["CountryCode"] = "",                            // not modelled on the account; only read for Entries rows
        ["AvatarId"] = AvatarId,
        ["LeagueId"] = LeagueId,
        ["SubLeagueId"] = SubLeagueId,
        ["CastleLevel"] = st.CastleRenovationLevel + 1,  // CastleRenovationLevel is 0-based on the wire
        ["IsCastleAttackable"] = false,                  // no PvP defence yet
    };

    static JsonObject SubLeagueModel() => new()
    {
        ["Id"] = SubLeagueId,
        ["LeagueId"] = LeagueId,
        ["Name"] = "Wooden Spoon",     // the starting league's display name; text-only (no icon asset exists)
        ["PrefixName"] = "",
        ["LargeIconUrl"] = "",
        ["SmallIconUrl"] = "",
    };

    // The player, as both the current user and the world leader. On a server with one account and no scoring,
    // "the best score in the world" IS this account — degenerate, but true, and it keeps every template branch
    // on a defined object. See the progress-bar note in BuildLeaderboard for why that matters.
    static JsonObject Competitor(AccountState st) => new()
    {
        ["AccountSummary"] = AccountSummary(st),
        ["SubLeagueModel"] = SubLeagueModel(),   // header template opens with CurrentUser.SubLeagueModel.LargeIconUrl
        ["Score"] = 0,                           // nothing scores a seasonal competition today
        ["AvatarUrl"] = "",
        ["IsCastleAttackable"] = false,
        ["IsDemoted"] = false,
        ["IsPromoted"] = false,
        ["Seconds"] = 0,
    };

    static JsonObject BuildLeaderboard(AccountState st) => new()
    {
        ["CurrentUser"] = Competitor(st),

        // BestWorldUser must be the SAME account id as CurrentUser, and must not be null. _onGetLeaderboardEntries
        // takes its first branch when the ids match (progress = 100%) and never touches the progress-bar models.
        // Null would instead fall through to the Score===0 branch — which looks safe, but the progress-bar
        // template then renders its `{{if !LeaderboardProgressBarModel.NextSubLeagueDetailedModel}}` block and
        // dereferences BestWorldUser.AvatarUrl on null. Keeping it defined keeps every branch total.
        ["BestWorldUser"] = Competitor(st),

        // No podium and no ranked rows. Both are first-class empty states in the UI: the top-players template is
        // wrapped in `{{if Leaders.length > 0}}`, and the entries sub-panel renders "There are no players in this
        // league" for an empty Entries[] and pads the rest via addEmptyEntries().
        ["Leaders"] = new JsonArray(),
        ["Entries"] = new JsonArray(),

        // Never omit (dereferenced unguarded). Null Previous/Next = no promotion or demotion target, which is
        // accurate: there is one league and no scoring to move between them.
        ["LeaderboardProgressBarModel"] = new JsonObject
        {
            ["PreviousSubLeagueDetailedModel"] = null,
            ["NextSubLeagueDetailedModel"] = null,
        },

        // Never omit (SelectedLeagueId is read unguarded). Both lists empty => the filters block is skipped
        // entirely and no dropdown renders — correct, as there's nothing to filter by.
        ["FilterModel"] = new JsonObject
        {
            ["Filters"] = new JsonArray(),
            ["LeagueFilterModels"] = new JsonArray(),
            ["SelectedLeagueId"] = LeagueId,
            ["SelectedSubLeagueId"] = SubLeagueId,
            ["SelectedFilterCode"] = "",
        },

        ["TotalCount"] = 0,
        ["FirstEntryRank"] = 0,
        ["PageSize"] = 20,
        ["IsPreviousPageAvailable"] = false,
        ["IsNextPageAvailable"] = false,
        ["IsFirstPageAvailable"] = false,
        ["IsRankPageAvailable"] = false,

        // SECONDS, not a duration string. The header renders it through ~timeFromSecondsRounded(RemainingTime),
        // so the old canned "20h35m10s" produced NaN. 0 is falsy, so `{{if RemainingTime}}` skips the countdown
        // entirely — right, since no season is running.
        ["RemainingTime"] = 0,

        ["ActiveZones"] = new JsonArray(),
        ["ActiveCountries"] = new JsonArray(),
        ["PreviousLeagueInfo"] = null,
        ["NextSeasonStartingDate"] = null,
    };
}

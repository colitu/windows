using System.Text.Json;
using System.Text.Json.Nodes;

namespace v2rayN.Services;

/// <summary>Achievements: each gives a title. Secret ones show as "???" until earned. Titles stay in English (like a badge).</summary>
public sealed record PassportAchievement(string Id, string Title, bool Secret)
{
    public static readonly PassportAchievement First = new("first", "First Connection", false);
    public static readonly PassportAchievement Globetrotter = new("globe", "Globetrotter", false);
    public static readonly PassportAchievement Origin = new("origin", "Origin Collector", false);
    public static readonly PassportAchievement NordicMaster = new("nordic_master", "Nordic Master", false);
    public static readonly PassportAchievement Night = new("night", "Night Explorer", true);
    public static readonly PassportAchievement Early = new("early", "Early Bird", true);
    public static readonly PassportAchievement LongHaul = new("longhaul", "Long Haul", true);
    public static readonly PassportAchievement Diamond = new("diamond", "Diamond Collector", true);
    public static readonly PassportAchievement Pathfinder = new("pathfinder", "Pathfinder", false);
    public static readonly PassportAchievement LegacyGold = new("legacy_gold", "Golden Passport", false);
    public static readonly PassportAchievement LegacyDiamond = new("legacy_diamond", "Diamond Passport", false);

    public static readonly IReadOnlyList<PassportAchievement> All =
        [First, Globetrotter, Origin, NordicMaster, Night, Early, LongHaul, Diamond, Pathfinder, LegacyGold, LegacyDiamond];

    public static PassportAchievement? Of(string? id) => All.FirstOrDefault(a => a.Id == id);
}

/// <summary>Passport cover looks. Classic for everyone, Premium with the plan, the rest from sets.</summary>
public enum CoverTheme
{
    Classic,
    Premium,
    Nordic,
    Baltic,
    Atlantic,
    Pacific
}

public static class CoverThemes
{
    public static readonly CoverTheme[] All = Enum.GetValues<CoverTheme>();

    public static string Key(this CoverTheme theme) => theme.ToString().ToLowerInvariant();

    public static CoverTheme? FromKey(string? key) => All.Cast<CoverTheme?>().FirstOrDefault(t => t!.Value.Key() == key);
}

/// <summary>
/// Collection sets. Their countries may belong to a later chapter: those stamps are recorded
/// already, so a set can be finished before its chapter opens.
/// </summary>
public sealed record PassportCollection(string Id, string Title, IReadOnlyList<string> Codes, bool Secret, CoverTheme? Cover)
{
    public static readonly PassportCollection Nordic = new("nordic", "Nordic", ["SE", "FI", "NO"], false, CoverTheme.Nordic);
    public static readonly PassportCollection Baltic = new("baltic", "Baltic", ["LV", "EE", "LT"], false, CoverTheme.Baltic);
    public static readonly PassportCollection Atlantic = new("atlantic", "Across the Atlantic", ["US", "GB", "CA"], false, CoverTheme.Atlantic);
    public static readonly PassportCollection Heart = new("heart", "Heart of Europe", ["DE", "FR", "NL", "PL", "CH"], false, null);
    public static readonly PassportCollection Pacific = new("pacific", "Pacific Rim", ["JP", "SG", "HK", "AU"], false, CoverTheme.Pacific);
    public static readonly PassportCollection GoldRush = new("gold_rush", "Gold Rush", [], true, null);

    public static readonly IReadOnlyList<PassportCollection> All = [Nordic, Baltic, Atlantic, Heart, Pacific, GoldRush];

    public static PassportCollection? Of(string? id) => All.FirstOrDefault(c => c.Id == id);
}

/// <summary>
/// Achievements, collections and the passport identity (nickname, collector number, favourite
/// stamp, title, cover). All on the device, like the stamps.
/// </summary>
public sealed class PassportGoals(ColituPassport passport)
{
    public const int GoldRushCount = 3;
    public const int DiamondCount = 3;

    private ColituPassportStore Store => passport.Store;

    public string Nickname => Store.GetString("nick") ?? "";
    public string? Favorite => Store.GetString("favorite");
    public string? Title => Store.GetString("title");
    public CoverTheme? Cover => CoverThemes.FromKey(Store.GetString("cover"));

    /// <summary>Six digits made on this device, not tied to the account.</summary>
    public string CollectorNo
    {
        get
        {
            var saved = Store.GetString("collector_no");
            if (saved != null) return saved;
            var made = Random.Shared.Next(100_000, 1_000_000).ToString(CultureInfo.InvariantCulture);
            Store.Set("collector_no", made);
            return made;
        }
    }

    public void UpdateNickname(string value)
    {
        var nick = value.Trim();
        Store.Set("nick", nick.Length == 0 ? null : nick[..Math.Min(20, nick.Length)]);
    }

    public void UpdateFavorite(string? code) => Store.Set("favorite", code);

    public void UpdateTitle(string? id)
    {
        Store.Set("title", id);
        passport.Weekly.Bump(ExpeditionGoal.Customize);
    }

    public void UpdateCover(CoverTheme? theme)
    {
        Store.Set("cover", theme?.Key());
        passport.Weekly.Bump(ExpeditionGoal.Customize);
    }

    /// <summary>Every country with a record: this chapter, the sets and whatever the tick saw.</summary>
    private IEnumerable<string> AllCodes() =>
        ColituPassport.Stamps.Select(s => s.Code)
            .Concat(PassportCollection.All.SelectMany(c => c.Codes))
            .Concat(passport.SeenCodes)
            .Where(code => code.Length == 2)
            .Distinct();

    /// <summary>Stamps of <paramref name="chapter"/> at <paramref name="level"/> or higher (Chapter 03 — Legacy).</summary>
    public int Legacy(StampLevel level, IEnumerable<ColituStamp> chapter) =>
        chapter.Count(s => passport.ProgressOf(s.Code).Level is { } l && l >= level);

    /// <summary>Chapter 03 opens with Chapter 02 complete.</summary>
    public bool Chapter3Open => passport.CountIn(ColituPassport.Chapter2) == ColituPassport.Chapter2.Count;

    private int CountAtLeast(StampLevel level) => AllCodes().Count(code => passport.ProgressOf(code).Level is { } l && l >= level);

    public bool Achieved(PassportAchievement a)
    {
        if (a == PassportAchievement.First) return AllCodes().Any(passport.UnlockedAnywhere);
        if (a == PassportAchievement.Origin) return ColituPassport.Stamps.All(s => passport.UnlockedAnywhere(s.Code));
        if (a == PassportAchievement.NordicMaster) return PassportCollection.Nordic.Codes.All(code => passport.ProgressOf(code).Level is { } l && l >= StampLevel.Gold);
        if (a == PassportAchievement.Diamond) return CountAtLeast(StampLevel.Diamond) >= DiamondCount;
        if (a == PassportAchievement.Pathfinder) return passport.Weekly.Total >= PassportWeekly.PathfinderAt;
        if (a == PassportAchievement.LegacyGold) return Legacy(StampLevel.Gold, ColituPassport.Stamps) >= ColituPassport.Stamps.Count;
        if (a == PassportAchievement.LegacyDiamond) return Legacy(StampLevel.Diamond, ColituPassport.Stamps) >= ColituPassport.Stamps.Count;
        return Store.GetLong($"a_{a.Id}") > 0;
    }

    /// <summary>Stamps of the set already collected, and how many it needs.</summary>
    public (int Have, int Need) Progress(PassportCollection c) => c == PassportCollection.GoldRush
        ? (Math.Min(CountAtLeast(StampLevel.Gold), GoldRushCount), GoldRushCount)
        : (c.Codes.Count(passport.UnlockedAnywhere), c.Codes.Count);

    public bool Completed(PassportCollection c)
    {
        var (have, need) = Progress(c);
        return have >= need;
    }

    /// <summary>When the set or achievement was first seen complete (0 when not yet).</summary>
    public long EarnedAt(string key) => Store.GetLong($"seen_{key}");

    /// <summary>Covers this user may pick.</summary>
    public IReadOnlyList<CoverTheme> Covers(bool premium)
    {
        var list = new List<CoverTheme> { CoverTheme.Classic };
        if (premium) list.Add(CoverTheme.Premium);
        foreach (var c in PassportCollection.All)
        {
            if (c.Cover is { } cover && Completed(c)) list.Add(cover);
        }
        return list;
    }

    /// <summary>The cover shown: the chosen one while still allowed, else Premium or Classic.</summary>
    public CoverTheme ActiveCover(bool premium)
    {
        var allowed = Covers(premium);
        return Cover is { } chosen && allowed.Contains(chosen) ? chosen : premium ? CoverTheme.Premium : CoverTheme.Classic;
    }

    public string? TitleOf(string? id) => PassportAchievement.Of(id) is { } a && Achieved(a) ? a.Title : null;

    /// <summary>Ring around the avatar: Diamond with Diamond Collector, gold with any Gold stamp.</summary>
    public StampLevel? AvatarFrame() =>
        Achieved(PassportAchievement.Diamond) ? StampLevel.Diamond : CountAtLeast(StampLevel.Gold) > 0 ? StampLevel.Gold : null;

    /// <summary>
    /// Chapters, expeditions, sets and achievements reached since the last call, each once (the app
    /// plays their reveal). Chapter 02 opening also replays the stamps of its countries recorded so far.
    /// </summary>
    public IReadOnlyList<StampEvent> CheckNew()
    {
        var now = passport.NowMillis;
        var list = new List<StampEvent>();
        if (passport.Chapter2Open && EarnedAt("ch_2") == 0)
        {
            Store.Set("seen_ch_2", now);
            list.Add(new StampEvent.Chapter("2"));
            list.AddRange(ColituPassport.Chapter2.Where(s => passport.UnlockedAnywhere(s.Code)).Select(s => new StampEvent.Unlocked(s.Code)));
        }
        if (Chapter3Open && EarnedAt("ch_3") == 0)
        {
            Store.Set("seen_ch_3", now);
            list.Add(new StampEvent.Chapter("3"));
        }
        list.AddRange(passport.Weekly.TakeNewlyDone().Select(goal => new StampEvent.Expedition(goal.Id)));
        foreach (var c in PassportCollection.All)
        {
            if (Completed(c) && EarnedAt($"c_{c.Id}") == 0)
            {
                Store.Set($"seen_c_{c.Id}", now);
                list.Add(new StampEvent.Collected(c.Id));
            }
        }
        foreach (var a in PassportAchievement.All)
        {
            if (Achieved(a) && EarnedAt($"a_{a.Id}") == 0)
            {
                Store.Set($"seen_a_{a.Id}", now);
                list.Add(new StampEvent.Achieved(a.Id));
            }
        }
        return list;
    }
}

/// <summary>One kind of weekly expedition and how many steps it takes.</summary>
public sealed record ExpeditionGoal(string Id, int Target)
{
    public static readonly ExpeditionGoal NewCountry = new("new_country", 1);
    public static readonly ExpeditionGoal Share = new("share", 1);
    public static readonly ExpeditionGoal Customize = new("customize", 1);
    public static readonly ExpeditionGoal Days10 = new("days10", 3);
    public static readonly ExpeditionGoal SetStamp = new("set_stamp", 1);
    public static readonly ExpeditionGoal Pieces = new("pieces", 4);

    public static readonly IReadOnlyList<ExpeditionGoal> All = [NewCountry, Share, Customize, Days10, SetStamp, Pieces];

    public static ExpeditionGoal? Of(string? id) => All.FirstOrDefault(g => g.Id == id);
}

/// <summary>
/// Weekly expeditions: three small goals a week, picked by a fixed rotation on the ISO week
/// number (no randomness). Missing a week costs nothing; rewards are cosmetic visa stamps and,
/// after five, the Pathfinder title.
/// </summary>
public sealed class PassportWeekly(ColituPassport passport)
{
    public const int PathfinderAt = 5;

    private ColituPassportStore Store => passport.Store;

    public static string WeekKey(DateTime date) =>
        $"{ISOWeek.GetYear(date)}-W{ISOWeek.GetWeekOfYear(date)}";

    public string WeekKey() => WeekKey(passport.Now);

    /// <summary>The three goals of the week of <paramref name="date"/> (same rotation as the phones).</summary>
    public static IReadOnlyList<ExpeditionGoal> GoalsOf(DateTime date)
    {
        var week = ISOWeek.GetYear(date) * 53 + ISOWeek.GetWeekOfYear(date);
        var all = ExpeditionGoal.All;
        return Enumerable.Range(0, 3).Select(i => all[(week * 3 + i) % all.Count]).ToList();
    }

    public IReadOnlyList<ExpeditionGoal> Goals() => GoalsOf(passport.Now);

    private void SyncWeek()
    {
        var key = WeekKey();
        if (Store.GetString("wk_key") != key)
        {
            foreach (var goal in ExpeditionGoal.All) Store.Remove($"wk_{goal.Id}");
            Store.Set("wk_key", key);
        }
    }

    public int Progress(ExpeditionGoal goal) => Store.GetString("wk_key") != WeekKey() ? 0 : Store.GetInt($"wk_{goal.Id}");

    /// <summary>One step towards <paramref name="goal"/>; counts only while it is one of this week's goals.</summary>
    public void Bump(ExpeditionGoal goal)
    {
        SyncWeek();
        if (Goals().Contains(goal)) Store.Set($"wk_{goal.Id}", Store.GetInt($"wk_{goal.Id}") + 1);
    }

    public bool Done(ExpeditionGoal goal) => Progress(goal) >= goal.Target;

    /// <summary>Expeditions finished over all weeks.</summary>
    public int Total => Store.GetInt("expeditions_done");

    /// <summary>Goals of this week finished since the last call, each once.</summary>
    internal IReadOnlyList<ExpeditionGoal> TakeNewlyDone()
    {
        var key = WeekKey();
        var done = Goals().Where(goal => Done(goal) && !Store.GetBool($"x_{key}_{goal.Id}")).ToList();
        foreach (var goal in done)
        {
            Store.Set($"x_{key}_{goal.Id}", true);
            Store.Set("expeditions_done", Total + 1);
        }
        return done;
    }
}

/// <summary>A friend's passport as read from their link (no account data, only what their share card shows).</summary>
public sealed record PassportFriend(string Nick, string Collector, string? Title, string Levels, long AddedAt)
{
    public int Level(string code)
    {
        var index = ColituPassport.AllStamps.ToList().FindIndex(s => s.Code == code);
        return index >= 0 && index < Levels.Length && char.IsAsciiDigit(Levels[index]) ? Levels[index] - '0' : 0;
    }

    public int CountIn(IEnumerable<ColituStamp> chapter) => chapter.Count(s => Level(s.Code) > 0);

    /// <summary>Sum of levels: the tie-breaker of the friends ranking.</summary>
    public int Score => Levels.Where(char.IsAsciiDigit).Sum(c => c - '0');
}

/// <summary>What a passport QR / link carries: no account data, only what the share card shows.</summary>
public sealed record DuelPassport(string Nick, string Collector, string? Title, IReadOnlyDictionary<string, int> Levels)
{
    public int CountIn(IEnumerable<ColituStamp> chapter) => chapter.Count(s => Levels.GetValueOrDefault(s.Code) > 0);

    public PassportFriend ToFriend(long addedAt) =>
        new(Nick, Collector, Title, string.Concat(ColituPassport.AllStamps.Select(s => Levels.GetValueOrDefault(s.Code).ToString(CultureInfo.InvariantCulture))), addedAt);

    public static DuelPassport From(PassportFriend f) =>
        new(f.Nick, f.Collector, f.Title, ColituPassport.AllStamps.ToDictionary(s => s.Code, s => f.Level(s.Code)));
}

/// <summary>
/// Passport Duel link: https://colitu.com/p#&lt;base64url&gt; of "1|nick|collector|title|levels",
/// the same format as the phones. The part after # never reaches a server.
/// </summary>
public static class PassportDuel
{
    public const string Prefix = "https://colitu.com/p#";

    public static string Link(string nick, string collector, string? title, string levels)
    {
        var raw = string.Join("|", "1", nick.Replace("|", ""), collector, title ?? "", levels);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static DuelPassport? Parse(string? text)
    {
        try
        {
            var value = (text ?? "").Trim();
            if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            var data = value[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            data = data.PadRight(data.Length + (4 - data.Length % 4) % 4, '=');
            var p = Encoding.UTF8.GetString(Convert.FromBase64String(data)).Split('|');
            if (p.Length < 5 || p[0] != "1") return null;
            var levels = ColituPassport.AllStamps.Select((s, i) => (s.Code, Level: i < p[4].Length && char.IsAsciiDigit(p[4][i]) ? p[4][i] - '0' : 0))
                .ToDictionary(x => x.Code, x => x.Level);
            return new DuelPassport(p[1], p[2], p[3].Length == 0 ? null : p[3], levels);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Friends, the Companion stamp and the passport's own settings. Everything stays on this device.</summary>
public sealed class PassportSocial(ColituPassport passport)
{
    public const string Companion = "companion";

    private ColituPassportStore Store => passport.Store;

    public bool ShowRanking { get => Store.GetBool("friends_ranking", true); set => Store.Set("friends_ranking", value); }
    public bool Sound { get => Store.GetBool("sound", true); set => Store.Set("sound", value); }

    /// <summary>Whether the first-visit intro has been shown.</summary>
    public bool IntroSeen { get => Store.GetBool("intro_seen"); set => Store.Set("intro_seen", value); }

    /// <summary>My passport as a duel link.</summary>
    public string MyLink()
    {
        var levels = string.Concat(ColituPassport.AllStamps.Select(s => passport.ProgressOf(s.Code).DuelLevel.ToString(CultureInfo.InvariantCulture)));
        return PassportDuel.Link(passport.Goals.Nickname, passport.Goals.CollectorNo, passport.Goals.Title, levels);
    }

    public IReadOnlyList<PassportFriend> Friends => LoadFriends();

    /// <summary>Adds or refreshes a friend (one per collector number); never yourself.</summary>
    public void AddFriend(PassportFriend friend)
    {
        if (friend.Collector == passport.Goals.CollectorNo) return;
        SaveFriends(LoadFriends().Where(f => f.Collector != friend.Collector).Append(friend).OrderByDescending(f => f.AddedAt).ToList());
    }

    public void RemoveFriend(string collector) => SaveFriends(LoadFriends().Where(f => f.Collector != collector).ToList());

    private void SaveFriends(List<PassportFriend> friends)
    {
        var array = new JsonArray();
        foreach (var f in friends)
        {
            var o = new JsonObject { ["nick"] = f.Nick, ["collector"] = f.Collector, ["levels"] = f.Levels, ["added"] = f.AddedAt };
            if (f.Title != null) o["title"] = f.Title;
            array.Add(o);
        }
        Store.Set("friends", array.ToJsonString());
    }

    private List<PassportFriend> LoadFriends()
    {
        try
        {
            if (Store.GetString("friends") is not { } raw || JsonNode.Parse(raw) is not JsonArray array) return [];
            return array.OfType<JsonObject>()
                .Where(o => o["collector"] != null)
                .Select(o => new PassportFriend(
                    o["nick"]?.GetValue<string>() ?? "",
                    o["collector"]!.GetValue<string>(),
                    o["title"]?.GetValue<string>(),
                    o["levels"]?.GetValue<string>() ?? "",
                    o["added"]?.GetValue<long>() ?? 0))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }

    /// <summary>Optional anonymous counters (off by default).</summary>
    public bool Metrics { get => Store.GetBool("metrics"); set => Store.Set("metrics", value); }

    /// <summary>
    /// Today's counters for POST /passport/metrics, once a day and only when turned on; null when off
    /// or already sent today. Totals only: no account, device or connection data.
    /// </summary>
    public Dictionary<string, object>? MetricsPayload()
    {
        if (!Metrics || Store.GetString("metrics_day") == passport.Day) return null;
        var progress = passport.Progress.Values.ToList();
        return new Dictionary<string, object>
        {
            ["v"] = 1,
            ["stamps"] = progress.Count,
            ["chapter"] = passport.Goals.Chapter3Open ? 3 : passport.Chapter2Open ? 2 : 1,
            ["levels"] = StampLevels.All.Select(level => progress.Count(p => p.Level == level)).ToArray(),
            ["expeditions"] = passport.Weekly.Total,
            ["pieces_today"] = Math.Clamp(passport.Today.Pieces, 0, 2),
            ["opened_today"] = true,
            ["platform"] = "windows"
        };
    }

    public void MetricsSent() => Store.Set("metrics_day", passport.Day);

    public bool HasCompanion => Store.GetLong($"special_{Companion}") > 0;

    /// <summary>The Companion stamp: whoever signed up with an invite code and whoever invited someone. Queued for its reveal.</summary>
    public void MarkCompanion()
    {
        if (HasCompanion) return;
        Store.Set($"special_{Companion}", passport.NowMillis);
        passport.Enqueue([new StampEvent.Special(Companion)]);
    }
}

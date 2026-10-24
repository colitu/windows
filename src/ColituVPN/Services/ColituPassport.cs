namespace v2rayN.Services;

/// <summary>One passport stamp: the country it stands for and its printed number.</summary>
public sealed record ColituStamp(string Code, int Number)
{
    public string NumberText => Number.ToString("00", CultureInfo.InvariantCulture);
}

/// <summary>Stamp material; every level is one 12-piece puzzle.</summary>
public enum StampLevel
{
    Bronze,
    Silver,
    Gold,
    Diamond
}

public static class StampLevels
{
    public static readonly StampLevel[] All = [StampLevel.Bronze, StampLevel.Silver, StampLevel.Gold, StampLevel.Diamond];

    public static string Key(this StampLevel level) => level switch
    {
        StampLevel.Bronze => "bronze",
        StampLevel.Silver => "silver",
        StampLevel.Gold => "gold",
        _ => "diamond"
    };

    public static StampLevel? FromKey(string? key) => All.Cast<StampLevel?>().FirstOrDefault(level => level!.Value.Key() == key);
}

/// <summary>What the device knows about one stamp. Unlocked once <see cref="UnlockedAt"/> is set.</summary>
public sealed record StampProgress(int Pieces, int Minutes, string? City, long UnlockedAt)
{
    public const int PiecesPerLevel = 12;

    public static readonly StampProgress None = new(0, 0, null, 0);

    public bool Unlocked => UnlockedAt > 0;

    public StampLevel? Level => Unlocked ? StampLevels.All[Math.Min(Pieces / PiecesPerLevel, StampLevels.All.Length - 1)] : null;

    /// <summary>Diamond with its puzzle full: nothing left to collect.</summary>
    public bool Mastered => Pieces >= PiecesPerLevel * StampLevels.All.Length;

    /// <summary>Filled pieces (0-12) of the current level's puzzle.</summary>
    public int LevelPieces => Mastered ? PiecesPerLevel : Pieces % PiecesPerLevel;

    public int ToNext => PiecesPerLevel - LevelPieces;

    /// <summary>Level code of the passport QR: 0 locked, 1-4 Bronze..Diamond, 5 Diamond complete.</summary>
    public int DuelLevel => !Unlocked ? 0 : Mastered ? 5 : (int)Level!.Value + 1;
}

/// <summary>A reward waiting for the app to show it (earned while the window was hidden too).</summary>
public abstract record StampEvent(string Code)
{
    public sealed record Unlocked(string Code) : StampEvent(Code);
    public sealed record Piece(string Code, int LevelPieces) : StampEvent(Code);
    public sealed record LevelUp(string Code, StampLevel Level) : StampEvent(Code);
    public sealed record Mastered(string Code) : StampEvent(Code);
    /// <summary>A hidden or visible achievement; <c>Code</c> is its id.</summary>
    public sealed record Achieved(string Code) : StampEvent(Code);
    /// <summary>A collection set completed; <c>Code</c> is its id.</summary>
    public sealed record Collected(string Code) : StampEvent(Code);
    /// <summary>A chapter opened; <c>Code</c> is its number ("2").</summary>
    public sealed record Chapter(string Code) : StampEvent(Code);
    /// <summary>A weekly expedition done; <c>Code</c> is the goal id.</summary>
    public sealed record Expedition(string Code) : StampEvent(Code);
    /// <summary>A special stamp outside the chapters (the Companion stamp).</summary>
    public sealed record Special(string Code) : StampEvent(Code);

    internal string Encode() => this switch
    {
        Unlocked => $"U|{Code}",
        Piece p => $"P|{Code}|{p.LevelPieces}",
        LevelUp l => $"L|{Code}|{l.Level.Key()}",
        Mastered => $"M|{Code}",
        Achieved => $"A|{Code}",
        Collected => $"C|{Code}",
        Chapter => $"H|{Code}",
        Expedition => $"X|{Code}",
        _ => $"S|{Code}"
    };

    internal static StampEvent? Decode(string text)
    {
        var p = text.Split('|');
        if (p.Length < 2 || p[1].Length == 0) return null;
        var code = p[1];
        return p[0] switch
        {
            "U" => new Unlocked(code),
            "P" => new Piece(code, p.Length > 2 && int.TryParse(p[2], out var n) ? n : 0),
            "L" => StampLevels.FromKey(p.Length > 2 ? p[2] : null) is { } level ? new LevelUp(code, level) : null,
            "M" => new Mastered(code),
            "A" => new Achieved(code),
            "C" => new Collected(code),
            "H" => new Chapter(code),
            "X" => new Expedition(code),
            "S" => new Special(code),
            _ => null
        };
    }
}

/// <summary>
/// Colitu Passport, the same rules as the phones (colitu-android data/ColituPassport.kt). Made and
/// kept only on this device; nothing about it is sent anywhere. The app calls <see cref="Tick"/>
/// once a minute while the tunnel runs (also with the window hidden in the tray); the rewards wait
/// in a queue until the window shows them with their animations.
///
/// Rules: five minutes in a row in a country unlock its Bronze stamp. Every day the 10th and the
/// 60th minute of use each add one puzzle piece to the stamp of the country connected at that
/// minute, at most two a day across all servers. Tester accounts (they see the panel's alpha
/// nodes) get a piece every ten minutes without the daily cap.
/// </summary>
public sealed class ColituPassport
{
    public const int UnlockMinutes = 5;
    public static readonly int[] PieceMinutes = [10, 60];
    private const int TesterEvery = 10;

    private static readonly Lazy<ColituPassport> Shared = new(() =>
        new ColituPassport(new ColituPassportStore(Utils.GetConfigPath("colitu-passport.json"))));

    public static ColituPassport Instance => Shared.Value;

    /// <summary>Chapter 01 — Origin. Other countries are recorded too, for the next chapter.</summary>
    public static readonly IReadOnlyList<ColituStamp> Stamps =
        new[] { "SE", "FI", "DE", "NL", "GB", "PL", "LV", "FR", "US", "JP" }.Select((code, i) => new ColituStamp(code, i + 1)).ToList();

    /// <summary>Chapter 02 — Horizons; opens once Chapter 01 is complete. No Russia or Kazakhstan (owner decision).</summary>
    public static readonly IReadOnlyList<ColituStamp> Chapter2 =
        new[] { "NO", "EE", "LT", "CH", "CA", "TR", "SG", "HK", "BR", "AU" }.Select((code, i) => new ColituStamp(code, i + 11)).ToList();

    public static readonly IReadOnlyList<ColituStamp> AllStamps = Stamps.Concat(Chapter2).ToList();

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;

    public ColituPassport(ColituPassportStore store, Func<DateTime>? clock = null)
    {
        Store = store;
        _clock = clock ?? (() => DateTime.Now);
        Goals = new PassportGoals(this);
        Weekly = new PassportWeekly(this);
        Social = new PassportSocial(this);
    }

    internal ColituPassportStore Store { get; }
    public PassportGoals Goals { get; }
    public PassportWeekly Weekly { get; }
    public PassportSocial Social { get; }

    /// <summary>Local time now (the passport's day starts at local midnight).</summary>
    internal DateTime Now => _clock();

    internal long NowMillis => new DateTimeOffset(_clock()).ToUnixTimeMilliseconds();

    internal string Day => Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal long EpochDay => DateOnly.FromDateTime(Now).DayNumber - DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;

    public static ColituStamp? StampOf(string? code) => AllStamps.FirstOrDefault(stamp => stamp.Code == code);

    /// <summary>The stamp of a server's country code ("UK" counts as GB); null for countries without one.</summary>
    public static ColituStamp? StampFor(string? countryCode) => Normalize(countryCode) is { } code ? StampOf(code) : null;

    /// <summary>Upper-case ISO code, UK as GB; null when not two letters.</summary>
    public static string? Normalize(string? code)
    {
        var value = (code ?? "").Trim().ToUpperInvariant();
        if (value == "UK") value = "GB";
        return value.Length == 2 && value.All(char.IsAsciiLetterUpper) ? value : null;
    }

    public StampProgress ProgressOf(string code) => new(
        Store.GetInt($"p_{code}"),
        Store.GetInt($"m_{code}"),
        Store.GetString($"city_{code}"),
        Store.GetLong($"t_{code}"));

    public bool UnlockedAnywhere(string code) => Store.GetLong($"t_{code}") > 0;

    /// <summary>Chapter 01 stamps collected (the passport's main count).</summary>
    public int Count => CountIn(Stamps);

    public bool Complete => Count == Stamps.Count;

    public int CountIn(IEnumerable<ColituStamp> chapter) => chapter.Count(stamp => UnlockedAnywhere(stamp.Code));

    /// <summary>Chapter 02 is open: every Chapter 01 stamp collected.</summary>
    public bool Chapter2Open => Stamps.All(stamp => UnlockedAnywhere(stamp.Code));

    /// <summary>Minutes of use today and pieces already earned today.</summary>
    public (int Minutes, int Pieces) Today =>
        Store.GetString("day") == Day ? (Store.GetInt("day_min"), Store.GetInt("day_pieces")) : (0, 0);

    /// <summary>Unlocked stamps (all chapters) with their progress.</summary>
    public IReadOnlyDictionary<string, StampProgress> Progress =>
        AllStamps.Select(stamp => (stamp.Code, Progress: ProgressOf(stamp.Code)))
            .Where(item => item.Progress.Unlocked)
            .ToDictionary(item => item.Code, item => item.Progress);

    /// <summary>No daily cap: the account sees the panel's alpha nodes.</summary>
    public bool Tester
    {
        get => Store.GetBool("tester");
        set => Store.Set("tester", value);
    }

    /// <summary>Tray notification for new stamps and pieces.</summary>
    public bool Notify
    {
        get => Store.GetBool("notify", true);
        set => Store.Set("notify", value);
    }

    /// <summary>The app remembers the city shown under a stamp (the tick only knows the country).</summary>
    public void RememberCity(string? countryCode, string? city)
    {
        if (Normalize(countryCode) is { } code && !string.IsNullOrWhiteSpace(city))
        {
            Store.Set($"city_{code}", city.Trim());
        }
    }

    /// <summary>One minute of a running tunnel to <paramref name="countryCode"/>; returns the rewards it produced (also queued).</summary>
    public IReadOnlyList<StampEvent> Tick(string? countryCode)
    {
        if (Normalize(countryCode) is not { } code)
        {
            return [];
        }
        var events = new List<StampEvent>();
        lock (_gate)
        {
            Store.Batch(store =>
            {
                var now = NowMillis;
                var day = Day;
                if (store.GetString("day") != day)
                {
                    store.Set("day", day);
                    store.Set("day_min", 0);
                    store.Set("day_pieces", 0);
                }
                // Minutes in a row in one country; a gap over 90 s or another country starts over.
                var last = store.GetLong("last_tick");
                var sameRun = store.GetString("run_cc") == code && now - last is >= 1 and <= 90_000;
                var run = sameRun ? store.GetInt("run_min") + 1 : 1;
                store.Set("run_cc", code);
                store.Set("run_min", run);
                store.Set("last_tick", now);
                store.Set($"m_{code}", store.GetInt($"m_{code}") + 1);

                if (store.GetLong($"t_{code}") == 0 && run >= UnlockMinutes)
                {
                    store.Set($"t_{code}", now);
                    events.Add(new StampEvent.Unlocked(code));
                    WeeklyUnlock(code);
                }

                var minutes = store.GetInt("day_min") + 1;
                store.Set("day_min", minutes);
                CountForAchievements(code, minutes, now);
                if (minutes == 10) Weekly.Bump(ExpeditionGoal.Days10);
                var earns = Tester ? minutes % TesterEvery == 0 : PieceMinutes.Contains(minutes);
                if (earns)
                {
                    // The piece goes to this country, or to the last unlocked one when this stamp is still locked.
                    var target = store.GetLong($"t_{code}") > 0 ? code : store.GetString("last_unlocked");
                    if (target != null)
                    {
                        events.AddRange(AddPiece(target));
                        store.Set("day_pieces", store.GetInt("day_pieces") + 1);
                    }
                }
                if (store.GetLong($"t_{code}") > 0) store.Set("last_unlocked", code);
                Enqueue(events);
            });
        }
        return events;
    }

    /// <summary>
    /// Counters behind the time-based achievements: night (00-05) and early (05-07) minutes per
    /// day, the countries used in the last seven days and the days that reached 60 minutes.
    /// </summary>
    private void CountForAchievements(string code, int minutes, long now)
    {
        var day = Day;
        var hour = Now.Hour;
        void DayCounter(string key)
        {
            if (Store.GetString($"{key}_day") != day)
            {
                Store.Set($"{key}_day", day);
                Store.Set($"{key}_min", 0);
            }
            var n = Store.GetInt($"{key}_min") + 1;
            Store.Set($"{key}_min", n);
            if (n >= 10 && Store.GetLong($"a_{key}") == 0) Store.Set($"a_{key}", now);
        }
        if (hour < 5) DayCounter("night");
        else if (hour < 7) DayCounter("early");

        var today = EpochDay;
        Store.Set($"d_{code}", today);
        var codes = (Store.GetString("codes") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Append(code).Distinct().ToList();
        Store.Set("codes", string.Join(",", codes));
        var recent = codes.Count(c => today - Store.GetLong($"d_{c}", -100) < 7);
        if (recent >= 5 && Store.GetLong("a_globe") == 0) Store.Set("a_globe", now);

        if (minutes == 60)
        {
            var days = Store.GetInt("days60") + 1;
            Store.Set("days60", days);
            if (days >= 7 && Store.GetLong("a_longhaul") == 0) Store.Set("a_longhaul", now);
        }
    }

    /// <summary>Every country code the passport has seen (collections span chapters).</summary>
    internal IEnumerable<string> SeenCodes => (Store.GetString("codes") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Rewards earned since the app last looked, oldest first; clears the queue.</summary>
    public IReadOnlyList<StampEvent> TakePending()
    {
        lock (_gate)
        {
            var raw = Store.GetString("pending") ?? "";
            Store.Remove("pending");
            return raw.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(StampEvent.Decode).OfType<StampEvent>().ToList();
        }
    }

    internal void Enqueue(IReadOnlyCollection<StampEvent> events)
    {
        if (events.Count == 0) return;
        var old = Store.GetString("pending") ?? "";
        var added = string.Join(";", events.Select(e => e.Encode()));
        Store.Set("pending", old.Length == 0 ? added : $"{old};{added}");
    }

    private void WeeklyUnlock(string code)
    {
        Weekly.Bump(ExpeditionGoal.NewCountry);
        if (PassportCollection.All.Any(set => set.Codes.Contains(code))) Weekly.Bump(ExpeditionGoal.SetStamp);
    }

    private List<StampEvent> AddPiece(string code)
    {
        Weekly.Bump(ExpeditionGoal.Pieces);
        var before = ProgressOf(code);
        var after = before with { Pieces = before.Pieces + 1 };
        Store.Set($"p_{code}", after.Pieces);
        var events = new List<StampEvent> { new StampEvent.Piece(code, after.LevelPieces == 0 ? StampProgress.PiecesPerLevel : after.LevelPieces) };
        if (after.Mastered && !before.Mastered) events.Add(new StampEvent.Mastered(code));
        else if (after.Level != before.Level) events.Add(new StampEvent.LevelUp(code, after.Level!.Value));
        return events;
    }

    // ── Demo (COLITU_PASSPORT_DEMO=1 only: screenshots and animation checks) ──

    public static bool DemoEnabled => Environment.GetEnvironmentVariable("COLITU_PASSPORT_DEMO") == "1";

    /// <summary>One more piece for <paramref name="code"/> (unlocking it first), through the same queue as real rewards.</summary>
    public void DebugPiece(string code)
    {
        lock (_gate)
        {
            Store.Batch(_ =>
            {
                var events = new List<StampEvent>();
                if (Store.GetLong($"t_{code}") == 0)
                {
                    Store.Set($"t_{code}", NowMillis);
                    events.Add(new StampEvent.Unlocked(code));
                    WeeklyUnlock(code);
                }
                else
                {
                    events.AddRange(AddPiece(code));
                }
                Enqueue(events);
            });
        }
    }

    /// <summary>A demo passport with every level, or an empty one when stamps exist.</summary>
    public void DebugCycle()
    {
        lock (_gate)
        {
            var keep = (Tester, Notify, Store.GetString("collector_no"), Store.GetBool("intro_seen"));
            Store.Batch(store =>
            {
                if (Count > 0)
                {
                    store.Clear();
                }
                else
                {
                    var now = NowMillis;
                    var set = new Dictionary<string, int>
                    {
                        ["SE"] = 48, ["FI"] = 40, ["DE"] = 28, ["NL"] = 37, ["GB"] = 15, ["PL"] = 12, ["LV"] = 11, ["FR"] = 2, ["US"] = 0,
                        // Next chapter's countries: Nordic complete, Baltic 2 of 3.
                        ["NO"] = 3, ["EE"] = 1
                    };
                    store.Set("a_night", now);
                    foreach (var (code, pieces) in set)
                    {
                        store.Set($"t_{code}", now);
                        store.Set($"p_{code}", pieces);
                        store.Set($"m_{code}", 30 + pieces * 40);
                    }
                    store.Set("codes", string.Join(",", set.Keys));
                }
                Tester = keep.Item1;
                Notify = keep.Item2;
                if (keep.Item3 != null) store.Set("collector_no", keep.Item3);
                store.Set("intro_seen", keep.Item4);
            });
        }
    }
}

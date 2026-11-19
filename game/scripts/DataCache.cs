using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Cache;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2Godot.Ui;

namespace Ms2Godot;

/// <summary>
/// The tables the game derives from the player's client, kept per install (built by the setup screen, read at field
/// entry instead of parsed): user://uigen/data-KEY/, where KEY hashes the archives they are read from (Xml.m2d and its
/// index). Each file's own key adds the load's configuration and the build of the code (<see cref="CacheStore"/>), so a
/// changed client, language or build reads as a missing file and that table is parsed again (and its file rewritten).
/// MS2_UICACHE=off parses as without the cache; =check reads both and logs the first difference.
/// </summary>
public static class DataCache
{
    const string InputsFile = "inputs.txt";
    static readonly object gate = new();
    static string storeData;
    static CacheStore store;

    /// <summary>The client archives the tables are read from.</summary>
    static IEnumerable<string> Inputs(string dataDir) => new[] { "Xml.m2d", "Xml.m2h" }.Select(f => System.IO.Path.Combine(dataDir, f));

    /// <summary>The store of a data folder, or null when its archives can't be hashed (the tables are then parsed).</summary>
    public static CacheStore Store(string dataDir)
    {
        lock (gate)
        {
            if (storeData == dataDir) return store;
            CacheStore s = null;
            try
            {
                var hashes = Inputs(dataDir).Select(UiCode.Stamps.Hash).ToArray();
                var key = System.Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", hashes))));
                var dir = System.IO.Path.Combine(UiCode.RootPath, "data-" + key[..16].ToLowerInvariant());
                System.IO.Directory.CreateDirectory(dir);
                var inputs = System.IO.Path.Combine(dir, InputsFile);
                if (!System.IO.File.Exists(inputs)) System.IO.File.WriteAllLines(inputs, hashes);
                s = new CacheStore(dir);
            }
            catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException)
            {
                GD.PushWarning("table cache: " + e.Message);
            }
            storeData = dataDir;
            return store = s;
        }
    }

    /// <summary>The archive hashes a data-KEY folder was made for, or null when it is not one.</summary>
    public static string[] InputsOf(string dir)
    {
        if (!System.IO.Path.GetFileName(dir).StartsWith("data-", System.StringComparison.Ordinal)) return null;
        var f = System.IO.Path.Combine(dir, InputsFile);
        try { return System.IO.File.Exists(f) ? System.IO.File.ReadAllLines(f).Where(l => l.Length == 40).ToArray() is { Length: > 0 } h ? h : null : null; }
        catch (System.IO.IOException) { return null; }
    }

    /// <summary>The session's settings a quest table load depends on (the string languages, the locale, the features off).</summary>
    public sealed class QuestConfig
    {
        public string[] Languages;
        public string Locale;
        public IReadOnlyCollection<string> FeaturesOff;

        /// <summary>What the HUD and the NPC dialogs load with (read on the main thread: the options are Godot's).</summary>
        public static QuestConfig Current(PackSet packs) => new()
        {
            Languages = new GameStrings(packs, GameOptions.StringLanguage).Languages,
            Locale = StubHudData.Instance.Locale,
            FeaturesOff = StubHudData.Instance.FeaturesOff,
        };
    }

    /// <summary>The quest tables, from the cache when it holds this load (else parsed and cached); the outcome goes to the perf log.</summary>
    public static QuestTable Quests(PackSet packs, QuestConfig c)
    {
        var t = QuestTable.Load(packs, c.Languages, c.Locale, c.FeaturesOff, Store(packs.DataDir), out var outcome);
        if (outcome != null && (PerfLog.On || outcome.Diff != null)) GD.Print("table cache: " + outcome);
        return t;
    }

    /// <summary>The setup step (a worker thread): writes the files of the tables that are missing; the names of those built.</summary>
    public static List<string> Build(PackSet packs, QuestConfig quests, System.Action<string, double> progress)
    {
        var built = new List<string>();
        var s = Store(packs.DataDir);
        if (s == null || CacheStore.Mode == "off") return built;
        progress?.Invoke("Reading the client's quest data", 0);
        if (QuestTable.BuildCache(packs, quests.Languages, quests.Locale, quests.FeaturesOff, s)) built.Add("quests");
        progress?.Invoke("Reading the client's quest data", 1);
        return built;
    }
}

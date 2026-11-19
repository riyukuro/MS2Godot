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

    // The caches are in place before the first archive opens (the Xml index is read before any UI asset exists), whichever
    // scene the game starts in. Only the hooks are set here; nothing is read or hashed until a pack set asks.
    // (CA2255 is for libraries; this assembly is the game Godot loads.)
#pragma warning disable CA2255
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() => UiCode.UseCaches();
#pragma warning restore CA2255

    /// <summary>Points the tables the libraries read once per pack set (<see cref="CacheStore.Of"/>) at this install's store.</summary>
    public static void Use()
    {
        CacheStore.Of = p => Store(p.DataDir);
        CacheStore.HashOf = f => UiCode.Stamps.Hash(f);
        CacheStore.Logged = Log;
    }

    static void Log(CacheStore.Outcome outcome)
    {
        if (outcome != null && (PerfLog.On || outcome.Diff != null)) GD.Print("table cache: " + outcome);
    }

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
                var hashes = Inputs(dataDir).Select(f => UiCode.Stamps.Hash(f)).ToArray();
                var key = System.Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", hashes))));
                var dir = System.IO.Path.Combine(UiCode.RootPath, "data-" + key[..16].ToLowerInvariant());
                System.IO.Directory.CreateDirectory(dir);
                var inputs = System.IO.Path.Combine(dir, InputsFile);
                // a list missing or cut short (a process stopped while writing it before it was written this way) is written again
                if (!(InputsOf(dir) is { } had && had.SequenceEqual(hashes))) WriteInputs(inputs, hashes);
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

    /// <summary>Writes the folder's archive hashes whole or not at all (a temporary file renamed into place): a folder whose
    /// list is cut short would read as made for other archives and be pruned with its tables.</summary>
    internal static void WriteInputs(string path, string[] hashes)
    {
        var tmp = path + ".tmp" + System.Environment.ProcessId;
        try
        {
            // the temporary files of earlier writes that did not finish (their process gone)
            var name = System.IO.Path.GetFileName(path) + ".tmp";
            foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(path), name + "*"))
                if (int.TryParse(System.IO.Path.GetFileName(f).AsSpan(name.Length), out int pid) && !Ms2.Formats.Cache.CacheStore.Running(pid))
                    try { System.IO.File.Delete(f); } catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { }
            System.IO.File.WriteAllLines(tmp, hashes);
            System.IO.File.Move(tmp, path, true);
        }
        catch
        {
            try { System.IO.File.Delete(tmp); } catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>The archives' bytes hashed again (not by stamp) are the ones the folder <paramref name="dir"/> is named for.</summary>
    static bool InputsMatch(string dataDir, string dir)
    {
        try { return InputsOf(dir) is { } had && Inputs(dataDir).Select(f => UiCode.Stamps.Hash(f, reread: true)).SequenceEqual(had); }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { return true; }
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
        /// <summary>The string language the UI's game tables load with (<see cref="Ms2.Ui.GameTables.Shared"/>).</summary>
        public string Language;
        public string Locale;
        public IReadOnlyCollection<string> FeaturesOff;

        /// <summary>What the HUD and the NPC dialogs load with (read on the main thread: the options are Godot's).</summary>
        public static QuestConfig Current(PackSet packs) => new()
        {
            Languages = new GameStrings(packs, GameOptions.StringLanguage).Languages,
            Language = GameOptions.StringLanguage,
            Locale = StubHudData.Instance.Locale,
            FeaturesOff = StubHudData.Instance.FeaturesOff,
        };
    }

    /// <summary>The quest tables, from the cache when it holds this load (else parsed and cached); the outcome goes to the perf log.</summary>
    public static QuestTable Quests(PackSet packs, QuestConfig c)
    {
        var t = QuestTable.Load(packs, c.Languages, c.Locale, c.FeaturesOff, Store(packs.DataDir), out var outcome);
        Log(outcome);
        return t;
    }

    /// <summary>The setup step (a worker thread): writes the files of the tables that are missing; the names of those built.</summary>
    public static List<string> Build(PackSet packs, QuestConfig quests, System.Action<string, double> progress) =>
        Build(packs, quests, progress, again: true);

    static List<string> Build(PackSet packs, QuestConfig quests, System.Action<string, double> progress, bool again)
    {
        var built = new List<string>();
        var s = Store(packs.DataDir);
        if (s == null || CacheStore.Mode == "off") return built;
        s.RemoveLeftovers();
        const string phase = "Reading the client's quest data";
        progress?.Invoke(phase, 0);
        built.AddRange(QuestMapGraph.BuildCache(packs, s));
        progress?.Invoke(phase, 0.1);
        if (QuestTable.BuildCache(packs, quests.Languages, quests.Locale, quests.FeaturesOff, s)) built.Add("quests");
        progress?.Invoke(phase, 0.7);
        if (FieldMissions.BuildCache(packs, quests.Languages, quests.Locale, quests.FeaturesOff, s)) built.Add("missions");
        progress?.Invoke(phase, 0.9);
        if (Ms2.Ui.GameTables.BuildCache(packs, quests.Language, s)) built.Add("gametables");
        progress?.Invoke(phase, 0.95);
        if (Ms2.Ui.Guide.GuideTable.BuildCache(packs, s)) built.Add("guide events");
        progress?.Invoke(phase, 0.97);
        if (Ms2.Ui.Guide.GuideStrings.BuildCache(packs, quests.Language, s)) built.Add("guide strings");
        progress?.Invoke(phase, 0.99);
        // a table built: the archive was read, so its bytes are hashed again; a folder named for other bytes than the ones
        // its files were built from (a file changed under its stamp) is removed and the tables are built in the folder of
        // the file as it is, so no session reads tables of other bytes
        if (built.Count > 0 && !InputsMatch(packs.DataDir, s.Dir))
        {
            GD.PushWarning("table cache: the client's tables changed under their stamp; building them again");
            try { System.IO.Directory.Delete(s.Dir, true); } catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { }
            lock (gate) { storeData = null; store = null; }
            if (again) return Build(packs, quests, progress, again: false);
            return built;
        }
        // the archives' file lists (each keyed by its own index file): a session opens them from these, not the .m2h
        var archives = PackIndex.Archives(packs.DataDir).ToList();
        for (var i = 0; i < archives.Count; i++)
        {
            if (PackIndex.BuildCache(archives[i], System.IO.Path.Combine(packs.DataDir, archives[i] + ".m2d"), s)) built.Add("index " + archives[i]);
            progress?.Invoke(phase, 0.99 + 0.01 * (i + 1) / archives.Count);
        }
        // the movies' layouts (keyed by the Gfx archive, in the UI code's folder): a dialog's first open reads its file
        const string layoutPhase = "Reading the client's UI layouts";
        progress?.Invoke(layoutPhase, 0);
        int layouts = LayoutCache.BuildAll(packs, f => progress?.Invoke(layoutPhase, f));
        if (layouts > 0) built.Add(layouts + " layouts");
        // the UI's atlases, decoded to their pixels (in the same folder): a dialog's first open reads its atlases' files
        const string atlasPhase = "Reading the client's UI images";
        progress?.Invoke(atlasPhase, 0);
        int atlases = AtlasCache.BuildAll(packs, f => progress?.Invoke(atlasPhase, f));
        if (atlases > 0) built.Add(atlases + " atlases");
        return built;
    }
}

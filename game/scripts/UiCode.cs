using System.Linq;
using Godot;
using Ms2.Avm2;
using Ms2.UiGen;

namespace Ms2Godot;

/// <summary>
/// The UI code built from the player's own client (<see cref="UiGenerator"/>): the UI movies' ActionScript translated to
/// C# and compiled into user://uigen/KEY/Ms2.UiCode.dll, where KEY is the Gfx.m2d hash with the translator version. It is
/// built on the first launch with a new Gfx.m2d (the setup screen) and loaded at startup; without it the movies run in
/// the interpreter. Nothing of it is in the repo. The other per-install files (<see cref="DataCache"/>) are under the
/// same folder; the client files' hashes are remembered in one stamp file per path, so two client folders keep theirs.
/// </summary>
public static class UiCode
{
    const string Root = "user://uigen";
    public static string RootPath => ProjectSettings.GlobalizePath(Root);
    static bool loaded;

    /// <summary>The hashes of the client files the per-install files are built from (each file hashed once, then by its stamp).</summary>
    public static readonly Ms2.Formats.Cache.Stamps Stamps = new(System.IO.Path.Combine(RootPath, "stamps.tsv"));

    /// <summary>The assembly path for a data folder (hashes Gfx.m2d the first time; then its stamp).</summary>
    public static string DllPath(string dataDir) =>
        System.IO.Path.Combine(RootPath, UiGenerator.KeyOf(Stamps.Hash(System.IO.Path.Combine(dataDir, "Resource", "Gfx.m2d"))), UiGenerator.DllName);

    static string XmlStampFile => System.IO.Path.Combine(RootPath, "xml-stamp.txt");

    /// <summary>
    /// The additional effect table cache (<see cref="Ms2.Ui.Skills.AdditionalCache"/>) next to the assembly:
    /// user://uigen/KEY/additional-XMLKEY-vN.bin. With <paramref name="hash"/> (the setup step) a stale Xml.m2d stamp is
    /// hashed again; without it (the game) a stale stamp gives null, so the tables parse the XML until the next setup.
    /// </summary>
    public static string AdditionalCachePath(string dataDir, bool hash = false)
    {
        if (string.IsNullOrEmpty(dataDir)) return null;
        var key = Ms2.Ui.Skills.AdditionalCache.XmlKey(dataDir, XmlStampFile, hash);
        return key == null ? null : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DllPath(dataDir)), Ms2.Ui.Skills.AdditionalCache.FileName(key));
    }

    /// <summary>Points the game tables at this install's caches (before the first table is built).</summary>
    public static void UseCaches() => Ms2.Ui.GameTables.AdditionalCacheOf = d => AdditionalCachePath(d);

    /// <summary>Builds the additional effect table cache when it is missing (a worker thread), then removes the ones built for
    /// other Xml.m2d keys or versions. Returns the build time in seconds, or -1 when it was there.</summary>
    public static double BuildTables(string dataDir)
    {
        var path = AdditionalCachePath(dataDir, true);
        if (path == null || System.IO.File.Exists(path)) return -1;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var packs = new Ms2.Formats.Pack.PackSet(dataDir)) Ms2.Ui.Skills.AdditionalCache.Build(packs, path);
        var dir = System.IO.Path.GetDirectoryName(path);
        foreach (var f in System.IO.Directory.GetFiles(dir, "additional-*"))
            if (f != path) try { System.IO.File.Delete(f); } catch (System.Exception e) { GD.PushWarning($"UI tables: {f}: {e.Message}"); }
        return sw.Elapsed.TotalSeconds;
    }

    /// <summary>The assembly for this data folder is built.</summary>
    public static bool Built(string dataDir) => System.IO.File.Exists(DllPath(dataDir));

    /// <summary>Loads the built assembly once; false when there is none.</summary>
    public static bool Load(string dataDir)
    {
        if (loaded) return true;
        // MS2_PERF_INTERP (with MS2_PERF): run the movies interpreted, to time them against the translated code
        if (Ms2Godot.Ui.PerfLog.On && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_PERF_INTERP"))) return false;
        var dll = DllPath(dataDir);
        if (!System.IO.File.Exists(dll)) return false;
        int n = GeneratedCode.LoadAssembly(dll);
        GD.Print($"UI code: {n} blocks from {dll}");
        return loaded = true;
    }

    /// <summary>
    /// Where the game's own assemblies are on disk (Godot loads them from memory, so they report no location): the editor
    /// build output, then the folders next to the executable (an exported build's data_* folder).
    /// </summary>
    static System.Collections.Generic.IEnumerable<string> AssemblyDirs()
    {
        foreach (var c in new[] { "Debug", "ExportDebug", "ExportRelease", "Release" })
            yield return ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/" + c);
        yield return System.AppContext.BaseDirectory;
        var exe = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());
        if (System.IO.Directory.Exists(exe))
            foreach (var d in System.IO.Directory.GetDirectories(exe, "data_*")) yield return d;
    }

    /// <summary>Builds the assembly (a worker thread), then removes the per-install files no client on file is for.</summary>
    public static UiGenerator.Result Build(string dataDir, System.Action<string, double> progress)
    {
        var dll = DllPath(dataDir);
        var r = UiGenerator.Build(dataDir, dll, null, progress, AssemblyDirs());
        if (r.Ok) Prune();
        return r;
    }

    /// <summary>
    /// Removes the folders under user://uigen built for client files that are no longer there or have changed (no stamp
    /// line's hash names them) or by another translator version; those of every client folder still on disk stay.
    /// </summary>
    public static void Prune()
    {
        if (!System.IO.Directory.Exists(RootPath)) return;
        var known = Stamps.Known();
        bool Known(string prefix) => known.Any(h => h.StartsWith(prefix, System.StringComparison.Ordinal));
        foreach (var d in System.IO.Directory.GetDirectories(RootPath))
        {
            var name = System.IO.Path.GetFileName(d);
            bool keep = UiGenerator.HashOfKey(name) is { } gfx ? Known(gfx) : DataCache.InputsOf(d) is { } inputs && inputs.All(Known);
            if (!keep)
                try { System.IO.Directory.Delete(d, true); } catch (System.Exception e) { GD.PushWarning($"per-install files: {d}: {e.Message}"); }
        }
        try { System.IO.File.Delete(System.IO.Path.Combine(RootPath, "gfx-stamp.txt")); } catch (System.IO.IOException) { }   // the stamp file of the first layout
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Loader;
using Godot;

[assembly: Ms2Godot.PrecompiledFirst]

namespace Ms2Godot;

/// <summary>
/// The libraries' machine code compiled ahead of time (ReadyToRun), so a first call does not wait for the JIT. Godot loads
/// the game's assemblies from a copy in memory (its plugin load context reads each file into a stream), and the runtime
/// uses an image's precompiled code only when the image is mapped from its file; so before any library is bound, each
/// one with a precompiled image is loaded from that file into the same context, and Godot's later lookups by name find
/// it there. The images are the build's (r2r/ next to the assemblies, made by the build, see MS2Godot.csproj) or the
/// assembly files themselves when they are precompiled (an exported build). An image is used only when it is of the same
/// assembly (its module version id); the runtime itself falls back to the JIT for any method whose image does not fit
/// the loaded runtime or the other assemblies. Ms2.UiCode.dll, built per install, is compiled by the setup step
/// (<see cref="Compile"/>). MS2_R2R=off loads no image and compiles none (the JIT alone, to time against).
/// </summary>
public static class Precompiled
{
    static string summary = "not run";
    static string exportSummaryDir;

    /// <summary>What <see cref="Preload"/> did, for the log (it runs before the log is up).</summary>
    public static string Summary => Describe(countImages: false);

    /// <summary>
    /// What <see cref="Preload"/> did, for the log. In an exported build, <paramref name="countImages"/> scans the
    /// export folder's DLL headers to count ReadyToRun images (16-80 ms on a self-contained runtime); normal starts
    /// pass false, and --verbose (OS.IsStdOutVerbose()) / MS2_PERF starts pass true.
    /// </summary>
    public static string Describe(bool countImages)
    {
        if (summary != null) return summary;
        if (!countImages) return $"precompiled in place from {exportSummaryDir}";
        return summary = ExportSummary(exportSummaryDir);
    }

    static string ExportSummary(string dir)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int r2r = 0;
        foreach (var file in Directory.GetFiles(dir, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == Self || name.StartsWith("Godot", StringComparison.Ordinal)) continue;
            if (Mvid(file) is { r2r: true }) r2r++;
        }
        return $"{r2r} precompiled in place from {dir} in {sw.Elapsed.TotalMilliseconds:F0} ms";
    }

    /// <summary>
    /// The libraries with an image that were bound before <see cref="Preload"/> ran (from memory: their code is the JIT's).
    /// Empty when the images went in first; a launch warns of any, and the perf runs fail on any (MS2_PERF_QUIT exits 3).
    /// </summary>
    public static IReadOnlyList<string> Early { get; private set; } = Array.Empty<string>();

    // The one module initializer: the images first, then the cache hooks (whose code binds Ms2.Formats and Ms2.Ui).
    // Nothing here names a type of a library, so the JIT binds none of them before the images are in. It runs before the
    // first code of this assembly: Godot's first is the constructor of PrecompiledFirstAttribute (below), ahead of
    // AssemblyHasScripts, whose script types' fields bind Ms2.Formats and Ms2.Quest as the attribute is made.
    // (CA2255 is for libraries; this assembly is the game Godot loads.)
#pragma warning disable CA2255
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        Preload();
        DataCache.Init();
    }
#pragma warning restore CA2255

    /// <summary>The folder Godot loaded this assembly from: the editor build output or an exported build's data folder.</summary>
    public static string AssemblyDir { get; private set; }

    /// <summary>
    /// Where the game's own assemblies can be on disk (Godot loads them from memory, so they report no location): the editor
    /// build output, then the folders next to the executable (an exported build's data_* folder). Read from the engine once,
    /// after the bind: the setup step's worker thread reads the list again and makes no engine call (<see cref="SetupScreen"/>
    /// reads it first on the main thread).
    /// </summary>
    public static IReadOnlyList<string> AssemblyDirs() => assemblyDirs ??= FindAssemblyDirs().ToList();
    static List<string> assemblyDirs;

    static IEnumerable<string> FindAssemblyDirs()
    {
        if (AssemblyDir != null) yield return AssemblyDir;
        foreach (var c in new[] { "Debug", "ExportDebug", "ExportRelease", "Release" })
            yield return ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/" + c);
        yield return AppContext.BaseDirectory;
        var exe = Path.GetDirectoryName(OS.GetExecutablePath());
        if (Directory.Exists(exe))
            foreach (var d in Directory.GetDirectories(exe, "data_*")) yield return d;
    }

    static readonly string Self = typeof(Precompiled).Assembly.GetName().Name;

    static readonly bool Off = string.Equals(System.Environment.GetEnvironmentVariable("MS2_R2R"), "off", StringComparison.OrdinalIgnoreCase);

    static void Preload()
    {
        var loc = typeof(Precompiled).Assembly.Location;
        var ctx = AssemblyLoadContext.GetLoadContext(typeof(Precompiled).Assembly);
        // An exported build boots through this assembly itself (GodotPlugins.Game.Main.InitializeFromGameProject),
        // loaded from disk by hostfxr into an isolated load context (not GodotPlugins.PluginLoadContext): its module
        // initializer runs before Godot wires up its native callbacks, so no Godot API may be called here, and the
        // runtime already maps the ReadyToRun images from disk. Only the editor / --path runs load from memory via
        // GodotPlugins.PluginLoadContext (after GodotPlugins has initialized Godot's native callbacks).
        if (!string.IsNullOrEmpty(loc) || ctx == null || ctx == AssemblyLoadContext.Default
            || !string.Equals(ctx.GetType().FullName, "GodotPlugins.PluginLoadContext", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(loc)) { summary = "no build folder"; return; }
            AssemblyDir = Path.GetDirectoryName(loc);
            if (AssemblyDir == null || !Directory.Exists(AssemblyDir)) { AssemblyDir = null; summary = "no build folder"; return; }
            // No PE scan at module init: Describe(false) reports "precompiled in place from {dir}" on a normal start
            // and Describe(true) counts ReadyToRun headers only when the caller asks (--verbose / MS2_PERF).
            exportSummaryDir = AssemblyDir;
            summary = null;
            return;
        }
        if (Engine.IsEditorHint()) { summary = "editor"; return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var mvid = typeof(Precompiled).Assembly.ManifestModule.ModuleVersionId;
        // this build's folder: the one whose copy of this assembly is the loaded one
        foreach (var d in FindAssemblyDirs())
            if (Mvid(Path.Combine(d, Self + ".dll")) is { } m && m.id == mvid) { AssemblyDir = d; break; }
        if (AssemblyDir == null) { summary = "no build folder"; return; }
        if (Off) { summary = "off"; return; }
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in ctx.Assemblies) loaded.Add(a.GetName().Name);
        int n = 0;
        var early = new List<string>();
        var stale = new List<string>();
        foreach (var file in Directory.GetFiles(AssemblyDir, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == Self || name.StartsWith("Godot", StringComparison.Ordinal)) continue;
            if (Mvid(file) is not { } il) continue;
            var image = il.r2r ? file : Path.Combine(AssemblyDir, "r2r", name + ".dll");
            if (!il.r2r && (Mvid(image) is not { } m || !m.r2r || m.id != il.id))
            {
                // an image of another build of the library (its copy or the image left from an interrupted build)
                if (File.Exists(image)) stale.Add(name);
                continue;
            }
            if (loaded.Contains(name)) { early.Add(name); continue; }
            try { ctx.LoadFromAssemblyPath(image); n++; }
            catch (Exception e) when (e is IOException or BadImageFormatException) { GD.PushWarning($"precompiled code: {image}: {e.Message}"); }
        }
        Early = early;
        // a launch says it too (the perf runs fail on it): a library bound before the images runs its code under the JIT
        if (early.Count > 0) GD.PushWarning($"precompiled code: bound from memory before the images, their code runs under the JIT: {string.Join(", ", early)}");
        var runtime = PrecompiledImage.RuntimeMismatch(Path.Combine(AssemblyDir, "r2r"), PrecompiledImage.Runtime);
        summary = $"{n} precompiled from {AssemblyDir} in {sw.Elapsed.TotalMilliseconds:F0} ms" + (runtime != null ? "; " + runtime : "")
            + (early.Count > 0 ? $"; already loaded from memory: {string.Join(", ", early)}" : "")
            + (stale.Count > 0 ? $"; not of their library's build, run under the JIT: {string.Join(", ", stale)}" : "");
    }

    static (Guid id, bool r2r)? Mvid(string path) => PrecompiledImage.Mvid(path);

    /// <summary>The precompiled image of a per-install assembly, next to it (<see cref="PrecompiledImage.ImageOf"/>): of the
    /// same module and this runtime. A file of it for another runtime or build is logged.</summary>
    public static string ImageOf(string dll)
    {
        if (Off) return null;
        var image = PrecompiledImage.ImageOf(dll, PrecompiledImage.Runtime, out var note);
        if (note != null) GD.Print("precompiled code: " + note);
        return image;
    }

    /// <summary>The compiler of the images, next to the assemblies (crossgen2/, copied there by the build).</summary>
    static string Compiler()
    {
        var exe = OperatingSystem.IsWindows() ? "crossgen2.exe" : "crossgen2";
        if (AssemblyDir != null && File.Exists(Path.Combine(AssemblyDir, "crossgen2", exe))) return Path.Combine(AssemblyDir, "crossgen2", exe);
        return null;
    }

    /// <summary>How long the compile of the UI code may take (about 10 s on 8 cores) before it is stopped.</summary>
    static readonly TimeSpan CompileTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The setup step (a worker thread): compiles <paramref name="dll"/> (the per-install UI code) into its image next to it,
    /// when it has none (<see cref="PrecompiledImage.Compile"/>). Returns the seconds taken, -1 when the image was there, or
    /// null when it could not be made (the game then runs the assembly's IL); it never throws.
    /// </summary>
    public static double? Compile(string dll)
    {
        if (Off) return null;
        if (AssemblyDir == null) { EngineCalls.Warn("precompiled code: no build folder"); return null; }
        var rtDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        // Cosmetic in a self-contained export where RuntimeDirectory == AssemblyDir (crossgen2 accepts a repeated -r pattern).
        var refs = string.Equals(Path.GetFullPath(rtDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 Path.GetFullPath(AssemblyDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 StringComparison.OrdinalIgnoreCase)
            ? new[] { Path.Combine(AssemblyDir, "*.dll") }
            : new[] { Path.Combine(rtDir, "*.dll"), Path.Combine(AssemblyDir, "*.dll") };
        return PrecompiledImage.Compile(Compiler(), dll, PrecompiledImage.Runtime, refs,
            CompileTimeout, w => EngineCalls.Warn("precompiled code: " + w));
    }
}

/// <summary>
/// Makes the module initializer (the images' preload, <see cref="Precompiled"/>) run before Godot binds a library: Godot's first look at
/// this assembly makes all its assembly attributes, in their order in the metadata, and this one is ahead of the generated
/// AssemblyHasScripts (generated sources come after the project's), whose script types' field types (Ms2.Formats,
/// Ms2.Quest) are loaded as it is made; this one's constructor is code of this assembly, so the initializer runs first.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PrecompiledFirstAttribute : Attribute { }

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Godot;

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
    /// <summary>What <see cref="Preload"/> did, for the log (it runs before the log is up).</summary>
    public static string Summary { get; private set; } = "not run";

    // The one module initializer: the images first, then the cache hooks (whose code binds Ms2.Formats and Ms2.Ui).
    // Nothing here names a type of a library, so the JIT binds none of them before the images are in.
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
    /// build output, then the folders next to the executable (an exported build's data_* folder).
    /// </summary>
    public static IEnumerable<string> AssemblyDirs()
    {
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
        if (Engine.IsEditorHint()) { Summary = "editor"; return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ctx = AssemblyLoadContext.GetLoadContext(typeof(Precompiled).Assembly);
        var mvid = typeof(Precompiled).Assembly.ManifestModule.ModuleVersionId;
        // this build's folder: the one whose copy of this assembly is the loaded one
        foreach (var d in AssemblyDirs())
            if (Mvid(Path.Combine(d, Self + ".dll")) is { } m && m.id == mvid) { AssemblyDir = d; break; }
        if (ctx == null || ctx == AssemblyLoadContext.Default || AssemblyDir == null) { Summary = "no build folder"; return; }
        if (Off) { Summary = "off"; return; }
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in ctx.Assemblies) loaded.Add(a.GetName().Name);
        int n = 0;
        var early = new List<string>();
        foreach (var file in Directory.GetFiles(AssemblyDir, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == Self || name.StartsWith("Godot", StringComparison.Ordinal)) continue;
            if (Mvid(file) is not { } il) continue;
            var image = il.r2r ? file : Path.Combine(AssemblyDir, "r2r", name + ".dll");
            if (!il.r2r && (Mvid(image) is not { } m || !m.r2r || m.id != il.id)) continue;
            if (loaded.Contains(name)) { early.Add(name); continue; }
            try { ctx.LoadFromAssemblyPath(image); n++; }
            catch (Exception e) when (e is IOException or BadImageFormatException) { GD.PushWarning($"precompiled code: {image}: {e.Message}"); }
        }
        Summary = $"{n} precompiled from {AssemblyDir} in {sw.Elapsed.TotalMilliseconds:F0} ms" + (early.Count > 0 ? $"; already loaded from memory: {string.Join(", ", early)}" : "");
    }

    /// <summary>A managed file's module version id and whether it carries precompiled code; null for no such file.</summary>
    static (Guid id, bool r2r)? Mvid(string path)
    {
        try
        {
            using var pe = new PEReader(File.OpenRead(path));
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            return (md.GetGuid(md.GetModuleDefinition().Mvid), pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size > 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { return null; }
    }

    /// <summary>The precompiled image of a per-install assembly, next to it: valid when it is of the same module.</summary>
    public static string ImageOf(string dll)
    {
        if (Off) return null;
        var image = Path.ChangeExtension(dll, ".r2r.dll");
        return Mvid(dll) is { } il && Mvid(image) is { } m && m.r2r && m.id == il.id ? image : null;
    }

    /// <summary>The compiler of the images, next to the assemblies (crossgen2/, copied there by the build).</summary>
    static string Compiler()
    {
        var exe = OperatingSystem.IsWindows() ? "crossgen2.exe" : "crossgen2";
        if (AssemblyDir != null && File.Exists(Path.Combine(AssemblyDir, "crossgen2", exe))) return Path.Combine(AssemblyDir, "crossgen2", exe);
        return null;
    }

    /// <summary>
    /// The setup step (a worker thread): compiles <paramref name="dll"/> (the per-install UI code) into its image next to it,
    /// when it has none; written under a temporary name and renamed, so a killed setup leaves no image. Returns the seconds
    /// taken, -1 when the image was there, or null when it could not be made (the game then runs the assembly's IL); it
    /// never throws.
    /// </summary>
    public static double? Compile(string dll)
    {
        if (Off) return null;
        if (ImageOf(dll) != null) return -1;
        var compiler = Compiler();
        if (compiler == null || AssemblyDir == null) { GD.PushWarning("precompiled code: no compiler next to the assemblies"); return null; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var image = Path.ChangeExtension(dll, ".r2r.dll");
        var tmp = image + ".tmp" + System.Environment.ProcessId;
        var psi = new System.Diagnostics.ProcessStartInfo(compiler) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { dll, "-o", tmp, "-O", "--targetos", OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux",
                     "--targetarch", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                     "-r", Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"), "-r", Path.Combine(AssemblyDir, "*.dll") })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            var err = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || Mvid(tmp) is not { r2r: true })
            {
                GD.PushWarning($"precompiled code: {Path.GetFileName(dll)}: exit {p.ExitCode}: {(err.Result + output).Trim()}");
                File.Delete(tmp);
                return null;
            }
            File.Move(tmp, image, true);
            return sw.Elapsed.TotalSeconds;
        }
        catch (Exception e)
        {
            // whatever fails here, the setup step goes on (its tables are built after this) and the game runs the IL
            GD.PushWarning($"precompiled code: {Path.GetFileName(dll)}: {e.GetType().Name}: {e.Message}");
            try { File.Delete(tmp); } catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
            return null;
        }
    }
}

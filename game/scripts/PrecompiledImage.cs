using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;

namespace Ms2Godot;

/// <summary>
/// The precompiled (ReadyToRun) images of <see cref="Precompiled"/>, without Godot (the tests build this file too): which
/// file is an assembly's image, and the compile of a per-install assembly. An image of a per-install assembly is named for
/// the runtime it was compiled against and its assembly's module version id (Ms2.UiCode.r2r-8.0.11-0123456789ab.dll), so
/// a new runtime or a new build of the assembly reads as no image, and a new image never replaces a file a running game
/// has mapped: the old ones are removed by a later start (<see cref="Prune"/>). Nothing here names a library's type: the
/// module initializer runs it before the libraries' images are in.
/// </summary>
public static class PrecompiledImage
{
    /// <summary>A managed file's module version id and whether it carries precompiled code; null for no such file.</summary>
    public static (Guid id, bool r2r)? Mvid(string path)
    {
        try
        {
            using var pe = new PEReader(File.OpenRead(path));
            if (!pe.HasMetadata) return null;
            var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            return (md.GetGuid(md.GetModuleDefinition().Mvid), pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size > 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// The runtime the images are compiled against and run on, by its major and minor version, the same rule as the build's
    /// library images (<see cref="RuntimeMismatch"/>): precompiled code stays valid across a servicing update of the same
    /// major and minor version (the runtime itself rejects a method whose dependencies changed and runs the JIT for it), so
    /// a patch update keeps the images and a new major or minor version compiles them again.
    /// </summary>
    public static string Runtime => $"{Environment.Version.Major}.{Environment.Version.Minor}";

    static string Prefix(string dll) => Path.GetFileNameWithoutExtension(dll) + ".r2r-";

    /// <summary>The name of the image of <paramref name="dll"/> (module version id <paramref name="mvid"/>) for <paramref name="runtime"/>.</summary>
    public static string NameOf(string dll, Guid mvid, string runtime) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dll)), Prefix(dll) + runtime + "-" + mvid.ToString("N")[..12] + ".dll");

    /// <summary>
    /// The image of <paramref name="dll"/> for <paramref name="runtime"/>: its file there, precompiled and of the same module;
    /// else null, and <paramref name="note"/> says why when there are images of it for another runtime or build.
    /// </summary>
    public static string ImageOf(string dll, string runtime, out string note)
    {
        note = null;
        if (Mvid(dll) is not { } il) return null;
        var image = NameOf(dll, il.id, runtime);
        if (Mvid(image) is { r2r: true } m && m.id == il.id) return image;
        var others = Others(dll, image);
        if (others.Count > 0)
            note = $"no image of {Path.GetFileName(dll)} for runtime {runtime} and build {il.id.ToString("N")[..12]}; there: {string.Join(", ", others.ConvertAll(Path.GetFileName))}";
        return null;
    }

    /// <summary>The images of <paramref name="dll"/> but <paramref name="keep"/> (and the files of their unfinished writes).</summary>
    static List<string> Others(string dll, string keep)
    {
        var list = new List<string>();
        var dir = Path.GetDirectoryName(Path.GetFullPath(dll));
        try
        {
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, Prefix(dll) + "*"))
                    if (!string.Equals(f, keep, StringComparison.OrdinalIgnoreCase)) list.Add(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>
    /// Removes the images of <paramref name="dll"/> but <paramref name="keep"/>: those of an old build or runtime, and the
    /// unfinished writes of a stopped compile. One a running game has mapped (Windows refuses it) stays for a later start.
    /// </summary>
    public static int Prune(string dll, string keep)
    {
        int n = 0;
        foreach (var f in Others(dll, keep))
            try { File.Delete(f); n++; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return n;
    }

    /// <summary>The target operating system's name for the compiler.</summary>
    public static string TargetOs => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";

    /// <summary>
    /// Compiles <paramref name="dll"/> into its image for <paramref name="runtime"/> with <paramref name="compiler"/>
    /// (crossgen2; <paramref name="references"/> are its -r patterns), when it has none: written under a temporary name and
    /// renamed, so a stopped or failed compile leaves no image and no temporary file. A compile that runs past
    /// <paramref name="timeout"/> is stopped. Returns the seconds taken, -1 when the image was there, or null when it
    /// could not be made (<paramref name="warn"/> says why; the game then runs the assembly's IL); it never throws.
    /// </summary>
    public static double? Compile(string compiler, string dll, string runtime, IEnumerable<string> references, TimeSpan timeout, Action<string> warn)
    {
        var name = Path.GetFileName(dll);
        if (Mvid(dll) is not { } il) { warn?.Invoke($"{name}: not an assembly"); return null; }
        var image = NameOf(dll, il.id, runtime);
        if (ImageOf(dll, runtime, out _) != null) { Prune(dll, image); return -1; }
        Prune(dll, image);
        if (compiler == null || !File.Exists(compiler)) { warn?.Invoke($"{name}: no compiler next to the assemblies"); return null; }
        // Godot's Linux export copies files from the publish folder without preserving the execute bit.
        if (!OperatingSystem.IsWindows())
            try
            {
                var mode = File.GetUnixFileMode(compiler);
                if ((mode & UnixFileMode.UserExecute) == 0) File.SetUnixFileMode(compiler, mode | UnixFileMode.UserExecute);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tmp = image + ".tmp" + Environment.ProcessId;
        var psi = new System.Diagnostics.ProcessStartInfo(compiler) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { dll, "-o", tmp, "-O", "--targetos", TargetOs,
                     "--targetarch", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() })
            psi.ArgumentList.Add(a);
        foreach (var r in references) { psi.ArgumentList.Add("-r"); psi.ArgumentList.Add(r); }
        System.Diagnostics.Process p = null;
        try
        {
            p = System.Diagnostics.Process.Start(psi);
            var err = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
            {
                try { p.Kill(true); p.WaitForExit(5000); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                warn?.Invoke($"{name}: the compiler ran past {timeout.TotalSeconds:F0} s and was stopped");
                Delete(tmp);
                return null;
            }
            p.WaitForExit();   // the output read to its end
            if (p.ExitCode != 0 || Mvid(tmp) is not { r2r: true } m || m.id != il.id)
            {
                warn?.Invoke($"{name}: exit {p.ExitCode}: {(err.Result + output.Result).Trim()}");
                Delete(tmp);
                return null;
            }
            File.Move(tmp, image, true);
            return sw.Elapsed.TotalSeconds;
        }
        catch (Exception e)
        {
            warn?.Invoke($"{name}: {e.GetType().Name}: {e.Message}");
            Delete(tmp);
            return null;
        }
        finally { p?.Dispose(); }
    }

    static void Delete(string f)
    {
        try { File.Delete(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The runtime version the build's library images (r2r/) were compiled against: the file the build writes next to them.</summary>
    public const string RuntimeFile = "runtime.txt";

    /// <summary>
    /// The difference of the build's images' runtime (<see cref="RuntimeFile"/> in <paramref name="dir"/>) from
    /// <paramref name="runtime"/> in the major and minor version, or null when they match (or the file is not there).
    /// </summary>
    public static string RuntimeMismatch(string dir, string runtime)
    {
        string built;
        try { built = File.ReadAllText(Path.Combine(dir, RuntimeFile)).Trim(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        if (!Version.TryParse(built, out var b) || !Version.TryParse(runtime, out var r)) return $"images built for runtime '{built}', running {runtime}";
        return b.Major == r.Major && b.Minor == r.Minor ? null : $"images built for runtime {built}, running {runtime} (the JIT runs instead)";
    }
}

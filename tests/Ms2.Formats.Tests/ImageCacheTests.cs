using Ms2.Formats.Cache;
using Ms2Godot;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The per-install cache files' launch and setup checks, the atlas build's rebuild decision and the precompiled images.</summary>
public class ImageCacheTests
{
    string dir;

    [SetUp]
    public void MakeDir()
    {
        dir = Path.Combine(Path.GetTempPath(), "ms2-imagecache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void RemoveDir()
    {
        CacheStore.CheckBodies = false;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    /// <summary>A w by h A8R8G8B8 DDS file whose pixel i is i in every byte.</summary>
    static byte[] Dds(int w, int h)
    {
        var d = new byte[128 + w * h * 4];
        "DDS "u8.CopyTo(d);
        BitConverter.GetBytes(124).CopyTo(d, 4);
        BitConverter.GetBytes(0x100f).CopyTo(d, 8);
        BitConverter.GetBytes(h).CopyTo(d, 12);
        BitConverter.GetBytes(w).CopyTo(d, 16);
        BitConverter.GetBytes(32).CopyTo(d, 76);
        BitConverter.GetBytes(0x41).CopyTo(d, 80);
        BitConverter.GetBytes(32).CopyTo(d, 88);
        BitConverter.GetBytes(0x00ff0000u).CopyTo(d, 92);
        BitConverter.GetBytes(0x0000ff00u).CopyTo(d, 96);
        BitConverter.GetBytes(0x000000ffu).CopyTo(d, 100);
        BitConverter.GetBytes(0xff000000u).CopyTo(d, 104);
        for (int i = 0; i < w * h * 4; i++) d[128 + i] = (byte)(i / 4);
        return d;
    }

    static void Damage(string path, Func<byte[], byte[]> f) => File.WriteAllBytes(path, f(File.ReadAllBytes(path)));

    [Test]
    public void ALaunchReadsTheHeaderAndTheSetupStepTheBody()
    {
        var store = new CacheStore(dir);
        var key = CacheStore.Key("atlas-a.dds", "cfg", "b1");
        store.Write("atlas-a.dds", key, new byte[] { 1, 2, 3, 4 });
        Assert.That((store.Has("atlas-a.dds", key), store.IsWhole("atlas-a.dds", key), store.Current("atlas-a.dds", key)), Is.EqualTo((true, true, true)));
        var path = store.PathOf("atlas-a.dds", key);
        Damage(path, b => { b[^1] ^= 1; return b; });
        Assert.That(store.Has("atlas-a.dds", key), Is.True, "the header is whole");
        Assert.That(store.IsWhole("atlas-a.dds", key), Is.False, "the body fails its checksum");
        Assert.That(store.Current("atlas-a.dds", key), Is.True, "a launch reads no body");
        CacheStore.CheckBodies = true;
        Assert.That(store.Current("atlas-a.dds", key), Is.False, "the setup step reads every body");
        CacheStore.CheckBodies = false;
        Damage(path, b => b[..^1]);
        Assert.That(store.Has("atlas-a.dds", key), Is.False, "a short file is not current at a launch either");
    }

    [Test]
    public void ADamagedBodyReadsNullAndItsFileIsRemoved()
    {
        var store = new CacheStore(dir);
        var key = CacheStore.Key("atlas-a.dds", "cfg", "b1");
        store.Write("atlas-a.dds", key, ImageCache.Encode(Dds(4, 2)));
        Assert.That(ImageCache.Read(store, "atlas-a.dds", key, out int size, out _, out var diff)?.w, Is.EqualTo(4));
        Assert.That((size > 0, diff), Is.EqualTo((true, (string)null)));
        var path = store.PathOf("atlas-a.dds", key);
        Damage(path, b => { b[^1] ^= 1; return b; });
        Assert.That(ImageCache.Read(store, "atlas-a.dds", key, out _, out _, out diff), Is.Null);
        Assert.That(diff, Is.EqualTo("damaged"));
        Assert.That(File.Exists(path), Is.False, "removed, so the next build makes it");
        Assert.That(ImageCache.Read(store, "atlas-a.dds", key, out _, out _, out diff), Is.Null);
        Assert.That(diff, Is.Null, "a missing file is a plain miss");
    }

    [Test]
    public void AStaleBuildKeyIsAMissAndItsFileIsKept()
    {
        var store = new CacheStore(dir);
        var old = CacheStore.Key("atlas-a.dds", "cfg", "b1");
        var now = CacheStore.Key("atlas-a.dds", "cfg", "b2");
        store.Write("atlas-a.dds", old, new byte[] { 1 });
        Assert.That((store.Has("atlas-a.dds", now), store.Read("atlas-a.dds", now)), Is.EqualTo((false, (byte[])null)));
        Assert.That(ImageCache.Read(store, "atlas-a.dds", now, out _, out _, out var diff), Is.Null);
        Assert.That(diff, Is.Null);
        Assert.That(File.Exists(store.PathOf("atlas-a.dds", old)), Is.True, "a miss removes nothing (another build's game may read it)");
    }

    [Test]
    public void ATmpFileOfARunningProcessIsRemovedOnceADayOld()
    {
        var store = new CacheStore(dir);
        var key = CacheStore.Key("atlas-a.dds", "cfg", "b1");
        var path = store.PathOf("atlas-a.dds", key);
        var mine = path + CacheFile.TmpMark + Environment.ProcessId;
        var old = path + CacheFile.TmpMark + (Environment.ProcessId == 1 ? 2 : 1);   // the id may be another process's by now
        File.WriteAllBytes(mine, new byte[] { 1 });
        File.WriteAllBytes(old, new byte[] { 1 });
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromDays(2));
        Assert.That(store.Has("atlas-a.dds", key), Is.False, "a write that did not finish is never read");
        Assert.That(store.RemoveLeftovers(), Is.EqualTo(1));
        Assert.That((File.Exists(mine), File.Exists(old)), Is.EqualTo((true, false)));
        File.SetLastWriteTimeUtc(mine, DateTime.UtcNow - TimeSpan.FromDays(2));
        store.Write("atlas-a.dds", key, new byte[] { 2 });
        Assert.That(File.Exists(mine), Is.False, "a piece's write sweeps its day-old files too");
    }

    [Test]
    public void TheBuildRebuildsADamagedOrPartialFileAndNotAWholeOne()
    {
        var store = new CacheStore(dir);
        var files = new Dictionary<string, byte[]> { ["a.dds"] = Dds(4, 4), ["b.dds"] = Dds(2, 3), ["c.dds"] = "not a dds file"u8.ToArray() };
        var reads = new List<string>();
        string Piece(string n) => "atlas-" + n;
        string Key(string n) => CacheStore.Key(Piece(n), "cfg", "b1");
        byte[] Read(string n) { lock (reads) reads.Add(n); return files[n]; }
        ImageCache.Built Run(bool setup)
        {
            reads.Clear();
            CacheStore.CheckBodies = setup;
            try { return ImageCache.BuildAll(store, files.Keys.ToList(), Piece, Key, Read, workers: 2); }
            finally { CacheStore.CheckBodies = false; }
        }
        var r = Run(true);
        Assert.That((r.Listed, r.Current, r.Done, r.Failed), Is.EqualTo((3, 0, 3, 0)));
        Assert.That(ImageCache.Read(store, Piece("c.dds"), Key("c.dds"), out _, out _, out _)?.kind, Is.EqualTo(ImageBlob.Kind.File));
        r = Run(false);
        Assert.That((r.Current, r.Done, reads.Count), Is.EqualTo((3, 0, 0)), "whole files are not built again");
        // a damaged body: a launch leaves it (its read removes it), the setup step builds it
        Damage(store.PathOf(Piece("b.dds"), Key("b.dds")), b => { b[^1] ^= 1; return b; });
        Assert.That(Run(false).Done, Is.EqualTo(0));
        r = Run(true);
        Assert.That((r.Current, r.Done, string.Join(",", reads)), Is.EqualTo((2, 1, "b.dds")));
        Assert.That(store.IsWhole(Piece("b.dds"), Key("b.dds")), Is.True);
        // a partial file (cut short) is not current even at a launch
        Damage(store.PathOf(Piece("a.dds"), Key("a.dds")), b => b[..^5]);
        r = Run(false);
        Assert.That((r.Done, string.Join(",", reads)), Is.EqualTo((1, "a.dds")));
        // one that fails is told and left; the others are built
        foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
        files["b.dds"] = Dds(2, 3)[..130];   // a DDS header whose pixels are cut short
        var failed = new List<string>();
        r = ImageCache.BuildAll(store, files.Keys.ToList(), Piece, Key, Read, failed: (n, _) => { lock (failed) failed.Add(n); });
        Assert.That((r.Done, r.Failed, string.Join(",", failed)), Is.EqualTo((2, 1, "b.dds")));
        Assert.That(File.Exists(store.PathOf(Piece("b.dds"), Key("b.dds"))), Is.False);
    }

    [Test]
    public void TheCheckModeComparesThePixelsWithTheArchiveFile()
    {
        var dds = Dds(4, 2);
        var cached = ImageBlob.Read(ImageCache.Encode(dds));
        Assert.That(cached.kind, Is.EqualTo(ImageBlob.Kind.Rgba));
        Assert.That(ImageCache.Compare(cached, dds), Is.Null);
        var other = (byte[])dds.Clone();
        other[130] ^= 1;
        Assert.That(ImageCache.Compare(cached, other), Is.EqualTo("bytes differ"));
        Assert.That(ImageCache.Compare(cached, Dds(2, 4)), Does.Contain("4x2 cached"));
        var raw = "not a dds file"u8.ToArray();
        var file = ImageBlob.Read(ImageCache.Encode(raw));
        Assert.That((file.kind, ImageCache.Compare(file, raw)), Is.EqualTo((ImageBlob.Kind.File, (string)null)));
        Assert.That(ImageCache.Compare(file, dds), Does.Contain("File 0x0 cached"));
    }

    [Test]
    public void AnImageBodyCantAskForMoreThanItsBlockHolds()
    {
        var body = ImageBlob.Write(ImageBlob.Kind.Rgba, 2, 2, new byte[16]);
        Assert.That(ImageBlob.Read(body).bytes, Has.Length.EqualTo(16));
        var wide = (byte[])body.Clone();
        BitConverter.GetBytes(ImageBlob.MaxSide + 1).CopyTo(wide, 1);
        BitConverter.GetBytes(1).CopyTo(wide, 5);
        BitConverter.GetBytes((ImageBlob.MaxSide + 1) * 4).CopyTo(wide, 9);
        Assert.Throws<InvalidDataException>(() => ImageBlob.Read(wide));
        var big = ImageBlob.Write(ImageBlob.Kind.File, 0, 0, new byte[16]);
        BitConverter.GetBytes(int.MaxValue).CopyTo(big, 9);
        Assert.Throws<InvalidDataException>(() => ImageBlob.Read(big), "a length past the block's most is refused before it is allocated");
    }

    // ---- precompiled images (game/scripts/PrecompiledImage.cs) ----

    /// <summary>A copy of a runtime library (precompiled, as the shared framework's are) as <paramref name="name"/> in the folder.</summary>
    string Copy(Type of, string name)
    {
        var path = Path.Combine(dir, name);
        File.Copy(of.Assembly.Location, path);
        return path;
    }

    [Test]
    public void AnImageOfAnotherModuleOrRuntimeIsNotUsed()
    {
        var dll = Copy(typeof(object), "Lib.dll");
        var il = PrecompiledImage.Mvid(dll);
        Assume.That(il?.r2r, Is.True, "the runtime's own library is precompiled");
        var image = PrecompiledImage.NameOf(dll, il.Value.id, "8.0");
        Assert.That(PrecompiledImage.ImageOf(dll, "8.0", out var note), Is.Null);
        Assert.That(note, Is.Null);
        File.Copy(dll, image);
        Assert.That(PrecompiledImage.ImageOf(dll, "8.0", out _), Is.EqualTo(image));
        Assert.That(PrecompiledImage.ImageOf(dll, "9.0", out note), Is.Null, "a new runtime is no image");
        Assert.That(note, Does.Contain(Path.GetFileName(image)));
        // another module under the name of this one's image (the module version id in the name is this one's)
        File.Delete(image);
        File.Copy(typeof(System.Linq.Enumerable).Assembly.Location, image);
        Assume.That(PrecompiledImage.Mvid(image)?.r2r, Is.True);
        Assert.That(PrecompiledImage.ImageOf(dll, "8.0", out _), Is.Null, "the module version id is checked");
    }

    [Test]
    public void AMissingOrFailingCompilerLeavesNoImageAndNoTmp()
    {
        var dll = Copy(typeof(object), "Lib.dll");
        var stale = PrecompiledImage.NameOf(dll, Guid.NewGuid(), "8.0");
        File.WriteAllBytes(stale, new byte[] { 1 });
        var warnings = new List<string>();
        Assert.That(PrecompiledImage.Compile(Path.Combine(dir, "none"), dll, "8.0", Array.Empty<string>(), TimeSpan.FromSeconds(30), warnings.Add), Is.Null);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(Directory.GetFiles(dir).Select(Path.GetFileName), Is.EqualTo(new[] { "Lib.dll" }), "the stale image is pruned");
        if (OperatingSystem.IsWindows()) return;   // the failing compilers below are shell scripts
        // a compiler that writes part of its output, then fails; one that never ends
        foreach (var (body, timeout) in new[] { ("printf x > \"$3\"; exit 1", 30), ("printf x > \"$3\"; exec sleep 60", 1) })
        {
            var compiler = Path.Combine(dir, "crossgen2");
            File.WriteAllText(compiler, "#!/bin/sh\n" + body + "\n");
            File.SetUnixFileMode(compiler, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            warnings.Clear();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(PrecompiledImage.Compile(compiler, dll, "8.0", Array.Empty<string>(), TimeSpan.FromSeconds(timeout), warnings.Add), Is.Null);
            Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(20));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal), Is.EqualTo(new[] { "Lib.dll", "crossgen2" }), body);
            File.Delete(compiler);
        }
    }

    [Test]
    public void NonExecutableCompilerIsMadeExecutableOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;
        var dll = Copy(typeof(object), "Lib.dll");
        var compiler = Path.Combine(dir, "crossgen2");
        // Godot's Linux export copies files from the publish folder without preserving the execute bit;
        // copying the precompiled input to the -o target ($3) produces a matching ReadyToRun output.
        File.WriteAllText(compiler, "#!/bin/sh\ncp \"$1\" \"$3\"\n");
        File.SetUnixFileMode(compiler, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var warnings = new List<string>();
        var secs = PrecompiledImage.Compile(compiler, dll, "8.0", Array.Empty<string>(), TimeSpan.FromSeconds(30), warnings.Add);
        Assert.That(secs, Is.Not.Null.And.GreaterThanOrEqualTo(0), string.Join("; ", warnings));
        Assert.That(warnings, Is.Empty);
        Assert.That(PrecompiledImage.ImageOf(dll, "8.0", out _), Is.Not.Null);
    }

    [TestCase("isolated", false)]
    [TestCase("default", false)]
    [TestCase("isolated", true)]
    public void GameModuleInitializerRunsWithoutGodotCallbacks(string mode, bool countImages)
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "MS2Godot.sln"))) root = Path.GetDirectoryName(root);
        Assume.That(root, Is.Not.Null, "solution root not found");
        var configs = new[] { "Debug", "Release", "ExportRelease", "ExportDebug" };
        var dll = configs
            .Select(c => Path.Combine(root, "game", ".godot", "mono", "temp", "bin", c, "MS2Godot.dll"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        Assume.That(dll, Is.Not.Null, "build game/MS2Godot.csproj first");
        var dllTime = File.GetLastWriteTimeUtc(dll);
        var gameDir = Path.Combine(root, "game");
        string newer = null;
        foreach (var f in Directory.EnumerateFiles(gameDir, "*.*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(gameDir, f);
            if (rel.StartsWith(".godot" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;
            if (!f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;
            if (File.GetLastWriteTimeUtc(f) > dllTime) { newer = rel; break; }
        }
        Assume.That(newer, Is.Null, $"probed {dll} is older than game/{newer}; rebuild game/MS2Godot.csproj first");
        TestContext.WriteLine($"probing {dll} (mode={mode}, countImages={countImages})");
        var runtimeConfig = Path.ChangeExtension(dll, ".runtimeconfig.json");
        Assume.That(File.Exists(runtimeConfig), Is.True, $"missing runtimeconfig next to {dll}");
        var host = Path.Combine(AppContext.BaseDirectory, "testhost.dll");
        Assume.That(File.Exists(host), Is.True, "missing testhost.dll");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet) || !File.Exists(dotnet)) dotnet = "dotnet";
        var psi = new System.Diagnostics.ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "exec", "--runtimeconfig", runtimeConfig, host }) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_STARTUP_HOOKS"] = typeof(ImageCacheTests).Assembly.Location;
        psi.Environment["MS2_TEST_MODULE_INIT"] = dll;
        psi.Environment["MS2_TEST_MODULE_INIT_MODE"] = mode;
        psi.Environment["MS2_TEST_MODULE_INIT_COUNT"] = countImages ? "1" : "0";
        using var p = System.Diagnostics.Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(30_000))
        {
            try { p.Kill(true); p.WaitForExit(5_000); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            Assert.Fail($"module initializer probe timed out (mode={mode}, countImages={countImages}, dll={dll})");
        }
        p.WaitForExit();
        Assert.That(p.ExitCode, Is.EqualTo(0), $"mode={mode} countImages={countImages} dll={dll} exit={p.ExitCode}\nstdout: {outTask.Result}\nstderr: {errTask.Result}");
        Assert.That(outTask.Result, Does.Contain("MODULE_INIT_OK"));
    }

    [Test]
    public void TheBuildsImagesRuntimeIsCompared()
    {
        Assert.That(PrecompiledImage.RuntimeMismatch(dir, "8.0.11"), Is.Null, "no file: nothing to compare");
        File.WriteAllText(Path.Combine(dir, PrecompiledImage.RuntimeFile), "8.0.6\n");
        Assert.That(PrecompiledImage.RuntimeMismatch(dir, "8.0.11"), Is.Null, "a servicing update keeps the images");
        Assert.That(PrecompiledImage.RuntimeMismatch(dir, "9.0.1"), Does.Contain("8.0.6"));
        // the images' own name follows the same rule: a servicing update keeps them
        Assert.That(PrecompiledImage.Runtime, Is.EqualTo($"{Environment.Version.Major}.{Environment.Version.Minor}"));
    }

    [Test]
    public void AnEncodeWithOrWithoutAPoolGivesTheSameBody()
    {
        var dds = Dds(64, 32);
        var pool = System.Buffers.ArrayPool<byte>.Create(1 << 20, 2);
        var pooled = ImageCache.Encode(dds, pool);
        Assert.That(ImageCache.Encode(dds, pool), Is.EqualTo(pooled), "a reused buffer encodes the same");
        Assert.That(ImageCache.Encode(dds), Is.EqualTo(pooled));
        Assert.That(ImageCache.Compare(ImageBlob.Read(pooled), dds), Is.Null);
    }

    [Test]
    public void BuildWorkersFollowMemory()
    {
        const long G = 1L << 30;
        Assert.That(ImageCache.WorkersFor(16, 32 * G), Is.EqualTo(15), "a large machine: one per core but one");
        Assert.That(ImageCache.WorkersFor(16, 4 * G), Is.EqualTo(4), "a small one: a quarter of its memory");
        Assert.That(ImageCache.WorkersFor(16, G / 2), Is.EqualTo(1));
        Assert.That(ImageCache.WorkersFor(1, 32 * G), Is.EqualTo(1));
    }
}

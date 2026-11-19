using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Ms2.Formats.Cache;
using Ms2.Formats.Pack;
using Ms2.Formats.Pixels;
using Ms2Godot.Ui;

namespace Ms2Godot;

/// <summary>
/// The UI's DDS atlases (Resource/Gfx.m2d), kept per install next to the layouts (user://uigen/KEY/): one file per atlas,
/// its pixels as R, G, B, A bytes in one LZ4 block (<see cref="ImageBlob"/>), keyed by the hashes of Gfx.m2d and Gfx.m2h
/// and the build of the pixel code. The setup step writes every atlas's file; a session reads an atlas's file instead of
/// the archive entry (decrypt, inflate and the pixel conversion), once per pack set. A missing, stale or damaged file
/// reads the archive as before and is not written here (a damaged one is removed, so the next setup step rebuilds it);
/// whatever goes wrong with a file (a read error, a reader's fault) reads the archive: the cache is never why an atlas fails.
/// MS2_UICACHE=off reads the archive; =check reads both and logs the first difference.
/// </summary>
public static class AtlasCache
{
    sealed class Set
    {
        public CacheStore Store;
        public string Config;
        public bool Ready;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Set> sets = new();

    static string Build => CacheStore.Build(typeof(ImageBlob).Assembly);

    // the archive's file names are found without case (PackSet.FindByFileName): one file per name
    static string Piece(string file) => "atlas-" + System.IO.Path.GetFileName(file).ToLowerInvariant();

    static Set Of(PackSet packs)
    {
        var s = sets.GetOrCreateValue(packs);
        lock (s)
        {
            if (s.Ready) return s;
            s.Ready = true;
            try
            {
                var gfx = System.IO.Path.Combine(packs.DataDir, "Resource", "Gfx.m2d");
                s.Config = UiCode.Stamps.Hash(gfx) + "," + UiCode.Stamps.Hash(System.IO.Path.ChangeExtension(gfx, ".m2h"));
                s.Store = new CacheStore(System.IO.Path.GetDirectoryName(UiCode.DllPath(packs.DataDir)));
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                GD.PushWarning("atlas cache: " + e.Message);
            }
            return s;
        }
    }

    /// <summary>The body of an archive entry's file: its pixels, or the file itself when the pixel code does not take it.</summary>
    static byte[] Encode(byte[] dds)
    {
        var rgba = DdsPixels.ToRgba(dds, out int w, out int h);
        return rgba != null ? ImageBlob.Write(ImageBlob.Kind.Rgba, w, h, rgba) : ImageBlob.Write(ImageBlob.Kind.File, 0, 0, dds);
    }

    /// <summary>The image of a decoded body (null when the file is one Godot can't read either).</summary>
    static Image ToImage((ImageBlob.Kind kind, int w, int h, byte[] bytes) b) =>
        b.kind == ImageBlob.Kind.Rgba ? Image.CreateFromData(b.w, b.h, false, Image.Format.Rgba8, b.bytes) : DdsLoader.Load(b.bytes);

    /// <summary>
    /// The atlas <paramref name="entry"/> of Resource/Gfx as an image: from its cache file, else from the archive. Null when
    /// the file is no image Godot reads.
    /// </summary>
    public static Image Load(PackSet packs, string entry)
    {
        var s = Of(packs);
        var mode = CacheStore.Mode;
        if (s.Store == null || mode == "off") return DdsLoader.Load(packs.Read("Resource/Gfx", entry));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var piece = Piece(entry);
        var key = CacheStore.Key(piece, s.Config, Build);
        byte[] body = null;
        (ImageBlob.Kind kind, int w, int h, byte[] bytes)? cached = null;
        string diff = null;
        double readMs = 0;
        try
        {
            body = s.Store.Read(piece, key);
            readMs = sw.Elapsed.TotalMilliseconds;
            if (body != null) cached = ImageBlob.Read(body);
        }
        catch (Exception e) { cached = null; diff = "unreadable: " + e.GetType().Name + ": " + e.Message; }
        if (cached == null && System.IO.File.Exists(s.Store.PathOf(piece, key)))
        {
            // the file of this key is there but does not read (a damaged body): removed, so the setup step builds it again
            diff ??= "damaged";
            try { System.IO.File.Delete(s.Store.PathOf(piece, key)); } catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { }
        }
        double decodeMs = sw.Elapsed.TotalMilliseconds - readMs;
        if (cached is { } c && mode != "check")
        {
            Image img = null;
            try { img = ToImage(c); }
            catch (Exception e) { diff = "unreadable: " + e.GetType().Name + ": " + e.Message; }
            if (img != null)
            {
                if (PerfLog.On) PerfLog.Print($"atlas {entry}: cache {body.Length / 1048576.0:F1} MB read {readMs:F1} decode {decodeMs:F1} image {sw.Elapsed.TotalMilliseconds - readMs - decodeMs:F1} ms");
                return img;
            }
            cached = null;
        }
        sw.Restart();
        var dds = packs.Read("Resource/Gfx", entry);
        if (cached is { } k)
        {
            try
            {
                var fresh = ImageBlob.Read(Encode(dds));
                diff = fresh.kind != k.kind || fresh.w != k.w || fresh.h != k.h ? $"{k.kind} {k.w}x{k.h} cached, {fresh.kind} {fresh.w}x{fresh.h} read"
                    : !fresh.bytes.AsSpan().SequenceEqual(k.bytes) ? "bytes differ" : null;
            }
            catch (Exception e) { diff = "the archive's file does not encode: " + e.GetType().Name + ": " + e.Message; }
            GD.Print($"atlas cache: {entry}: check" + (diff != null ? " DIFF " + diff : ""));
            return ToImage(k);
        }
        var image = DdsLoader.Load(dds);
        if (PerfLog.On || diff != null)
            GD.Print($"atlas cache: {entry}: " + (diff ?? "miss") + $", archive read and decode {sw.Elapsed.TotalMilliseconds:F1} ms");
        return image;
    }

    /// <summary>
    /// The setup step (a worker thread): writes the file of every DDS file of Resource/Gfx that has none whole (each file's
    /// checksum is checked, so a damaged body is built again here and never read at a dialog's open), on all cores (archive
    /// read, pixel conversion, LZ4 encode, write); the count built. An atlas that fails is logged and left to the archive
    /// read; the others are built. The atlases a name finds are the archive's first of that name
    /// (<see cref="PackSet.FindByFileName"/>), as a session asks.
    /// </summary>
    public static int BuildAll(PackSet packs, Action<double> progress)
    {
        var s = Of(packs);
        if (s.Store == null || CacheStore.Mode == "off") return 0;
        s.Store.RemoveLeftovers();
        var build = Build;
        var names = packs.List("Resource/Gfx").Where(n => n.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            .Select(n => packs.FindByFileName("Resource/Gfx", n)).Where(n => n != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var cores = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, System.Environment.ProcessorCount - 1) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var whole = new bool[names.Count];
        Parallel.For(0, names.Count, cores, i =>
        {
            try { whole[i] = s.Store.IsWhole(Piece(names[i]), CacheStore.Key(Piece(names[i]), s.Config, build)); }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { }
        });
        var missing = names.Where((n, i) => !whole[i]).ToList();
        if (PerfLog.On) GD.Print($"atlas cache: {names.Count - missing.Count} of {names.Count} files whole, checked in {sw.Elapsed.TotalMilliseconds:F0} ms");
        if (missing.Count == 0) return 0;
        System.IO.Directory.CreateDirectory(s.Store.Dir);
        sw.Restart();
        int done = 0, failed = 0;
        long bytes = 0;
        Parallel.ForEach(missing, cores, n =>
        {
            try
            {
                var body = Encode(packs.Read("Resource/Gfx", n));
                s.Store.Write(Piece(n), CacheStore.Key(Piece(n), s.Config, build), body, onlyConfig: true);
                Interlocked.Add(ref bytes, body.Length);
                Interlocked.Increment(ref done);
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref failed);
                GD.PushWarning($"atlas cache: {n}: not built ({e.GetType().Name}: {e.Message}); read from the archive");
            }
            progress?.Invoke((double)(Volatile.Read(ref done) + Volatile.Read(ref failed)) / missing.Count);
        });
        GD.Print($"atlas cache: {done} of {names.Count} atlases built" + (failed > 0 ? $", {failed} failed" : "") + $", {bytes / 1048576.0:F0} MB, in {sw.Elapsed.TotalSeconds:F1} s");
        return done;
    }
}

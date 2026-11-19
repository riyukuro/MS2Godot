using System;
using System.Collections.Generic;
using System.Linq;
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
        var cached = ImageCache.Read(s.Store, piece, key, out int size, out double readMs, out string diff);
        double decodeMs = sw.Elapsed.TotalMilliseconds - readMs;
        if (cached is { } c && mode != "check")
        {
            Image img = null;
            try { img = ToImage(c); }
            catch (Exception e) { diff = "unreadable: " + e.GetType().Name + ": " + e.Message; }
            if (img != null)
            {
                if (PerfLog.On) PerfLog.Print($"atlas {entry}: cache {size / 1048576.0:F1} MB read {readMs:F1} decode {decodeMs:F1} image {sw.Elapsed.TotalMilliseconds - readMs - decodeMs:F1} ms");
                return img;
            }
            cached = null;
        }
        sw.Restart();
        var dds = packs.Read("Resource/Gfx", entry);
        if (cached is { } k)
        {
            try { diff = ImageCache.Compare(k, dds); }
            catch (Exception e) { diff = "the archive's file does not convert: " + e.GetType().Name + ": " + e.Message; }
            GD.Print($"atlas cache: {entry}: check" + (diff != null ? " DIFF " + diff : ""));
            return ToImage(k);
        }
        var image = DdsLoader.Load(dds);
        if (PerfLog.On || diff != null)
            GD.Print($"atlas cache: {entry}: " + (diff ?? "miss") + $", archive read and decode {sw.Elapsed.TotalMilliseconds:F1} ms");
        return image;
    }

    /// <summary>
    /// The setup step and each launch (a worker thread): writes the file of every DDS file of Resource/Gfx that needs one
    /// (<see cref="ImageCache.BuildAll"/>: at the setup step each body's checksum is checked, so a damaged body is built
    /// again there and never read at a dialog's open; at a launch only the headers are read); the count built. An atlas
    /// that fails is logged and left to the archive read; the others are built. The atlases a name finds are the archive's
    /// first of that name (<see cref="PackSet.FindByFileName"/>), as a session asks.
    /// </summary>
    public static int BuildAll(PackSet packs, Action<double> progress)
    {
        var s = Of(packs);
        if (s.Store == null || CacheStore.Mode == "off") return 0;
        s.Store.RemoveLeftovers();
        var build = Build;
        var names = packs.List("Resource/Gfx").Where(n => n.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            .Select(n => packs.FindByFileName("Resource/Gfx", n)).Where(n => n != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var r = ImageCache.BuildAll(s.Store, names, Piece, n => CacheStore.Key(Piece(n), s.Config, build), n => packs.Read("Resource/Gfx", n), progress,
            (n, e) => GD.PushWarning($"atlas cache: {n}: not built ({e.GetType().Name}: {e.Message}); read from the archive"));
        if (PerfLog.On || r.Done + r.Failed > 0)
            GD.Print($"atlas cache: {r.Current} of {r.Listed} files current ({(CacheStore.CheckBodies ? "bodies" : "headers")} checked in {r.CheckMs:F0} ms)"
                + (r.Done + r.Failed > 0 ? $"; {r.Done} built" + (r.Failed > 0 ? $", {r.Failed} failed" : "") + $", {r.Bytes / 1048576.0:F0} MB, in {r.BuildMs / 1000:F1} s" : ""));
        return r.Done;
    }
}

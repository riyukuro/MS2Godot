using Ms2.Formats.Pixels;

namespace Ms2.Formats.Cache;

/// <summary>
/// The UI atlases' cache files (<see cref="ImageBlob"/> bodies in a <see cref="CacheStore"/>): what a file holds for an
/// archive DDS file, the check mode's comparison with the archive, and the setup step's build of the files that need one.
/// </summary>
public static class ImageCache
{
    /// <summary>The body of an archive DDS file: its pixels, or the file itself when the pixel code does not take it.</summary>
    public static byte[] Encode(byte[] dds, System.Buffers.ArrayPool<byte> pool = null)
    {
        var rgba = DdsPixels.ToRgba(dds, out int w, out int h);
        return rgba != null ? ImageBlob.Write(ImageBlob.Kind.Rgba, w, h, rgba, pool) : ImageBlob.Write(ImageBlob.Kind.File, 0, 0, dds, pool);
    }

    /// <summary>
    /// The decoded body of the piece's file of <paramref name="key"/>, or null: no file, or one that does not read (a
    /// damaged body, a reader's fault: <paramref name="diff"/> says which), which is removed, so the next build makes it
    /// again. <paramref name="size"/> is the body's length, <paramref name="readMs"/> the file read's time.
    /// </summary>
    public static (ImageBlob.Kind kind, int w, int h, byte[] bytes)? Read(CacheStore store, string piece, string key, out int size, out double readMs, out string diff)
    {
        size = 0;
        diff = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        byte[] body = null;
        try
        {
            body = store.Read(piece, key);
            readMs = sw.Elapsed.TotalMilliseconds;
            if (body != null) { size = body.Length; return ImageBlob.Read(body); }
        }
        catch (Exception e) { readMs = sw.Elapsed.TotalMilliseconds; diff = "unreadable: " + e.GetType().Name + ": " + e.Message; }
        if (File.Exists(store.PathOf(piece, key)))
        {
            diff ??= "damaged";
            store.Remove(piece, key);
        }
        return null;
    }

    /// <summary>
    /// The first difference of a decoded body and the archive file it was made from, or null: the pixels (or the file) are
    /// compared directly, with no encode and no second decode.
    /// </summary>
    public static string Compare((ImageBlob.Kind kind, int w, int h, byte[] bytes) cached, byte[] dds)
    {
        var rgba = DdsPixels.ToRgba(dds, out int w, out int h);
        var kind = rgba != null ? ImageBlob.Kind.Rgba : ImageBlob.Kind.File;
        if (rgba == null) { w = 0; h = 0; }
        if (kind != cached.kind || w != cached.w || h != cached.h) return $"{cached.kind} {cached.w}x{cached.h} cached, {kind} {w}x{h} read";
        return (rgba ?? dds).AsSpan().SequenceEqual(cached.bytes) ? null : "bytes differ";
    }

    /// <summary>What <see cref="BuildAll"/> did.</summary>
    public readonly record struct Built(int Listed, int Current, int Done, int Failed, long Bytes, double CheckMs, double BuildMs);

    /// <summary>
    /// The workers of a build: one per core but one, as many as a quarter of the memory the process may use holds at
    /// <see cref="WorkerBytes"/> each (an archive file, its pixels, up to 64 MB for a 4096 square atlas, and the encoder's
    /// buffer of the same size), so a small machine builds with fewer and a large one is not held to a fixed count.
    /// </summary>
    public static int Workers => WorkersFor(Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    /// <summary>The most one build worker holds at once (a 4096 square atlas: file, pixels, encoder buffer, written body).</summary>
    public const long WorkerBytes = 256L << 20;

    /// <summary>The largest encode buffer a build pools (LZ4's worst case for a 4096 square atlas); a larger one is not kept.</summary>
    const int EncodeBufferMax = 128 << 20;

    /// <summary><see cref="Workers"/> for a machine of <paramref name="cores"/> cores and <paramref name="memory"/> bytes.</summary>
    public static int WorkersFor(int cores, long memory) =>
        (int)Math.Clamp(Math.Min(cores - 1, memory / 4 / WorkerBytes), 1, 64);

    /// <summary>
    /// Writes the file of every name whose file is not <see cref="CacheStore.Current"/> (at the setup step every body is
    /// read and checked, so a damaged or partial file is built again; at a launch the header alone is read): the name's
    /// archive file (<paramref name="read"/>) encoded and written for the key <paramref name="key"/> gives. A name that
    /// fails is told to <paramref name="failed"/> and left to the archive read; the others are built.
    /// </summary>
    public static Built BuildAll(CacheStore store, IReadOnlyList<string> names, Func<string, string> piece, Func<string, string> key,
        Func<string, byte[]> read, Action<double> progress = null, Action<string, Exception> failed = null, int workers = 0)
    {
        var opts = new ParallelOptions { MaxDegreeOfParallelism = workers > 0 ? workers : Workers };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var current = new bool[names.Count];
        Parallel.For(0, names.Count, opts, i =>
        {
            try { current[i] = store.Current(piece(names[i]), key(names[i])); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        });
        var missing = names.Where((n, i) => !current[i]).ToList();
        double checkMs = sw.Elapsed.TotalMilliseconds;
        if (missing.Count == 0) return new Built(names.Count, names.Count, 0, 0, 0, checkMs, 0);
        Directory.CreateDirectory(store.Dir);
        sw.Restart();
        int done = 0, bad = 0;
        long bytes = 0;
        // the encode buffers: this build's own pool (one per worker, up to a 4096 square atlas's), dropped with the build
        var pool = System.Buffers.ArrayPool<byte>.Create(EncodeBufferMax, opts.MaxDegreeOfParallelism);
        Parallel.ForEach(missing, opts, n =>
        {
            try
            {
                var body = Encode(read(n), pool);
                store.Write(piece(n), key(n), body, onlyConfig: true);
                Interlocked.Add(ref bytes, body.Length);
                Interlocked.Increment(ref done);
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref bad);
                failed?.Invoke(n, e);
            }
            progress?.Invoke((double)(Volatile.Read(ref done) + Volatile.Read(ref bad)) / missing.Count);
        });
        return new Built(names.Count, names.Count - missing.Count, done, bad, bytes, checkMs, sw.Elapsed.TotalMilliseconds);
    }
}

using System.Security.Cryptography;

namespace Ms2.Formats.Cache;

/// <summary>
/// The SHA-1 of client files, remembered per resolved path (symbolic links followed) with the file's size and write
/// time, in one text file: a file whose size and time match its line is not read again. Each data folder's files keep
/// their own lines, so two client folders (or a folder that links some packs from another) each keep their cache.
/// </summary>
public sealed class Stamps
{
    readonly string file;
    static readonly object gate = new();

    public Stamps(string stampFile) => file = stampFile;

    /// <summary>The path with every link resolved: the file whose bytes are read.</summary>
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        try { return File.ResolveLinkTarget(full, true)?.FullName ?? full; }
        catch (IOException) { return full; }
    }

    /// <summary>The lower-case hex SHA-1 of the file at <paramref name="path"/>.</summary>
    public string Hash(string path)
    {
        var real = Resolve(path);
        var fi = new FileInfo(real);
        if (!fi.Exists) throw new FileNotFoundException("no such file", path);
        string stamp = $"{fi.Length}\t{fi.LastWriteTimeUtc.Ticks}";
        lock (gate)
        {
            var lines = Load();
            if (lines.TryGetValue(real, out var l) && l.Stamp == stamp) return l.Hash;
        }
        string hash;
        using (var s = File.OpenRead(real)) hash = Convert.ToHexString(SHA1.HashData(s)).ToLowerInvariant();
        lock (gate)
        {
            var lines = Load();
            lines[real] = (stamp, hash);
            Save(lines);
        }
        return hash;
    }

    /// <summary>Every hash on file whose file still exists (lines of files that are gone are dropped).</summary>
    public IReadOnlyCollection<string> Known()
    {
        lock (gate)
        {
            var lines = Load();
            var gone = lines.Keys.Where(p => !File.Exists(p)).ToList();
            foreach (var p in gone) lines.Remove(p);
            if (gone.Count > 0) Save(lines);
            return lines.Values.Select(v => v.Hash).ToHashSet();
        }
    }

    Dictionary<string, (string Stamp, string Hash)> Load()
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        if (!File.Exists(file)) return d;
        foreach (var line in File.ReadAllLines(file))
        {
            var f = line.Split('\t');
            if (f.Length == 4 && f[3].Length == 40) d[f[0]] = (f[1] + "\t" + f[2], f[3]);
        }
        return d;
    }

    void Save(Dictionary<string, (string Stamp, string Hash)> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
        var tmp = file + ".tmp" + Environment.ProcessId;
        File.WriteAllLines(tmp, lines.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}\t{k.Value.Stamp}\t{k.Value.Hash}"));
        File.Move(tmp, file, true);
    }
}

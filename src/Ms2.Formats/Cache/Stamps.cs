using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Ms2.Formats.Cache;

/// <summary>
/// The SHA-1 of client files, remembered per resolved path (symbolic links followed) with the file's stamp, in one text
/// file: a file whose stamp matches its line is not read again. The stamp is the size, the write time, and the file's
/// identity and change time where the system gives them (the inode and the status change time; on Windows the file index
/// and change time): a file replaced by another (a new inode) or written with its old write time put back (a new change
/// time, which no tool can set) reads as changed. The setup step hashes the files again before it builds from them
/// (<see cref="Hash(string, bool)"/>). Each data folder's files keep their own lines, so two client folders (or a folder
/// that links some packs from another) each keep their cache.
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

    /// <summary>The stamp of an existing file: size, write time, then its identity and change time where known.</summary>
    public static string StampOf(string real)
    {
        var fi = new FileInfo(real);
        if (!fi.Exists) throw new FileNotFoundException("no such file", real);
        return $"{fi.Length}\t{fi.LastWriteTimeUtc.Ticks}\t{Identity(real)}";
    }

    /// <summary>
    /// The lower-case hex SHA-1 of the file at <paramref name="path"/>: from its line when the stamp matches, unless
    /// <paramref name="reread"/> (the setup step, about to read the file anyway), which hashes the bytes in every case.
    /// </summary>
    public string Hash(string path, bool reread = false)
    {
        var real = Resolve(path);
        string stamp = StampOf(real);
        string known = null;
        lock (gate)
        {
            var lines = Load();
            if (lines.TryGetValue(real, out var l))
            {
                if (l.Stamp == stamp && !reread) return l.Hash;
                if (l.Stamp == stamp) known = l.Hash;
            }
        }
        string hash;
        using (var s = File.OpenRead(real)) hash = Convert.ToHexString(SHA1.HashData(s)).ToLowerInvariant();
        if (hash == known && StampOf(real) == stamp) return hash;
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
            // path, the stamp's fields, the hash; a line of an older stamp (fewer fields) does not match and is replaced
            var f = line.Split('\t');
            if (f.Length >= 4 && f[^1].Length == 40) d[f[0]] = (string.Join("\t", f[1..^1]), f[^1]);
        }
        return d;
    }

    void Save(Dictionary<string, (string Stamp, string Hash)> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
        var tmp = file + ".tmp" + Environment.ProcessId;
        try
        {
            File.WriteAllLines(tmp, lines.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}\t{k.Value.Stamp}\t{k.Value.Hash}"));
            File.Move(tmp, file, true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    // ---- the file's identity and change time ----------------------------------------------------------------------

    /// <summary>"dev:inode:ctime" (Linux, statx) or "volume:index:changetime" (Windows); empty where neither is read.</summary>
    static string Identity(string real)
    {
        try
        {
            if (OperatingSystem.IsLinux()) return LinuxIdentity(real);
            if (OperatingSystem.IsWindows()) return WindowsIdentity(real);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException) { }
        return "";
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    static extern int Statx(int dirfd, string path, int flags, uint mask, byte[] buf);

    // struct statx (the same on every Linux architecture): stx_ino at 32, stx_ctime {s64 sec; u32 nsec} at 96,
    // stx_dev_major/minor at 136/140; 256 bytes
    static string LinuxIdentity(string real)
    {
        const int AtFdCwd = -100;
        const uint BasicStats = 0x7ff;
        var b = new byte[256];
        if (Statx(AtFdCwd, real, 0, BasicStats, b) != 0) return "";
        ulong ino = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(32));
        long sec = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(96));
        uint nsec = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(104));
        uint major = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(136)), minor = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(140));
        return $"{major}.{minor}:{ino}:{sec}.{nsec:D9}";
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, byte[] info);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool GetFileInformationByHandleEx(Microsoft.Win32.SafeHandles.SafeFileHandle file, int infoClass, byte[] info, int size);

    // BY_HANDLE_FILE_INFORMATION: dwVolumeSerialNumber at 28, nFileIndexHigh/Low at 44/48 (52 bytes);
    // FILE_BASIC_INFO (class 0): ChangeTime at 24 (40 bytes)
    static string WindowsIdentity(string real)
    {
        using var h = File.OpenHandle(real, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var info = new byte[52];
        var basic = new byte[40];
        if (!GetFileInformationByHandle(h, info) || !GetFileInformationByHandleEx(h, 0, basic, basic.Length)) return "";
        uint volume = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(28));
        ulong index = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(44)) << 32 | BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(48));
        long change = BinaryPrimitives.ReadInt64LittleEndian(basic.AsSpan(24));
        return $"{volume}:{index}:{change}";
    }
}

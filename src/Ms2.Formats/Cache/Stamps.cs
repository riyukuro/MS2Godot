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
/// that links some packs from another) each keep their cache. Processes sharing the file take turns on it (a lock file
/// next to it), so one's new line is never lost to another's save.
/// </summary>
public sealed class Stamps
{
    readonly string file;
    static readonly object gate = new();

    public Stamps(string stampFile) => file = stampFile;

    /// <summary>How long a save waits for another process to finish its own before giving up (the line is then made again
    /// on the next run).</summary>
    public static TimeSpan LockWait = TimeSpan.FromSeconds(10);

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
            // a lock held past LockWait (a stuck process): the hash is right, the line is made again on the next run
            using var held = TryLock();
            if (held == null) return hash;
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
            // without the lock (held past LockWait) the lines are read as they are and none is dropped
            using var held = TryLock();
            var lines = Load();
            var gone = lines.Keys.Where(p => !File.Exists(p)).ToList();
            foreach (var p in gone) lines.Remove(p);
            if (held != null && gone.Count > 0) Save(lines);
            return lines.Values.Select(v => v.Hash).ToHashSet();
        }
    }

    Dictionary<string, (string Stamp, string Hash)> Load()
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        if (!File.Exists(file)) return d;
        // opened for sharing: a save in another process renames its file over this one (Windows refuses while it is open
        // without it)
        string[] all;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            all = r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (FileNotFoundException) { return d; }
        foreach (var raw in all)
        {
            var line = raw.TrimEnd('\r');
            // path, the stamp's fields, the hash; a line of an older stamp (fewer fields) does not match and is replaced
            var f = line.Split('\t');
            if (f.Length >= 4 && f[^1].Length == 40) d[f[0]] = (string.Join("\t", f[1..^1]), f[^1]);
        }
        return d;
    }

    void Save(Dictionary<string, (string Stamp, string Hash)> lines)
    {
        SweepTmp();
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

    /// <summary>Removes the temporary files of saves that did not finish (a process stopped between the write and the
    /// rename); those of a process still running stay.</summary>
    void SweepTmp()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        var name = Path.GetFileName(file) + ".tmp";
        try
        {
            foreach (var f in Directory.GetFiles(dir, name + "*"))
                if (int.TryParse(Path.GetFileName(f).AsSpan(name.Length), out int pid) && !CacheStore.Running(pid))
                    try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (IOException) { }
    }

    /// <summary>The lock file next to the stamp file, held open without sharing (a system lock on the file, released when the
    /// process ends); waits for the process that holds it, and gives up (null) after <see cref="LockWait"/>.</summary>
    IDisposable TryLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
        var path = file + ".lock";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException)
            {
                if (sw.Elapsed >= LockWait) return null;
                Thread.Sleep(10);
            }
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

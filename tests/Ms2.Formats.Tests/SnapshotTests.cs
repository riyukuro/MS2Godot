using Ms2.Formats.Cache;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class SnapshotTests
{
    public enum Kind : byte { None, Quest = 3, Epic = 200 }

    public struct Point { public int X; public float Y; public string Tag; }

    public interface IShape { int Area { get; } }
    public sealed class Square : IShape { public int Side; public int Area => Side * Side; }

    public sealed class Node
    {
        public readonly int Id;
        public Node Next;                      // may point back: a cycle
        public Node(int id) => Id = id;
    }

    public sealed class Root
    {
        public bool B; public sbyte Sb = -5; public short S = -300; public ushort Us = 60000; public uint U = 4000000000;
        public long L = long.MinValue; public ulong Ul = ulong.MaxValue; public char C = 'あ'; public double D = -0.0; public float NaN = float.NaN;
        public Kind K = Kind.Epic; public int? None; public int? Some = 7; public Kind? NullableEnum = Kind.Quest;
        public string Text = "text"; public string Same; public string Null;
        public int[] Ints = { 1, -2, int.MaxValue }; public byte[] Empty = Array.Empty<byte>(); public string[] Strings = { "a", null, "b" };
        public int[,] Grid = { { 1, 2, 3 }, { 4, 5, 6 } };
        public Point P = new() { X = 3, Y = 1.5f, Tag = "p" };
        public Point[] Points = { new() { X = 1 }, new() { Tag = "t" } };
        public List<Node> Nodes = new();
        public Dictionary<string, int> Ignore = new(StringComparer.OrdinalIgnoreCase) { ["Key"] = 1 };
        public Dictionary<int, List<string>> Lists = new() { [5] = new() { "x" }, [-1] = new() };
        public SortedDictionary<string, int> Sorted = new(StringComparer.Ordinal) { ["b"] = 2, ["a"] = 1 };
        public HashSet<int> Set = new() { 9, 1, 5 };
        public IShape Shape = new Square { Side = 4 };
        public object Boxed = 12L;
        public readonly List<int> ReadOnlyList = new() { 1 };
        public Func<int, int> Twice = x => x * 2;     // not stored: the constructor's default is kept
    }

    static Root Sample()
    {
        var r = new Root();
        r.Same = r.Text;
        var a = new Node(1); var b = new Node(2);
        a.Next = b; b.Next = a;
        r.Nodes.Add(a); r.Nodes.Add(b); r.Nodes.Add(a);
        return r;
    }

    [Test]
    public void RoundTripKeepsValuesSharingAndCycles()
    {
        var a = Sample();
        var bytes = Snapshot.Write(a);
        var b = Snapshot.Read<Root>(bytes);
        Assert.That(Snapshot.Diff(a, b), Is.Null);
        Assert.That(b.Nodes[0], Is.SameAs(b.Nodes[2]));
        Assert.That(b.Nodes[1].Next, Is.SameAs(b.Nodes[0]));
        Assert.That(b.Nodes[0].Id, Is.EqualTo(1));
        Assert.That(((IDictionary<string, int>)b.Ignore)["KEY"], Is.EqualTo(1), "the comparer is kept");
        Assert.That(b.Sorted.Keys, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(b.Grid[1, 2], Is.EqualTo(6));
        Assert.That(b.Shape.Area, Is.EqualTo(16));
        Assert.That(b.Boxed, Is.EqualTo(12L));
        Assert.That(float.IsNaN(b.NaN) && double.IsNegative(b.D), Is.True);
        Assert.That(b.Twice(4), Is.EqualTo(8));
        Assert.That(Snapshot.Write(b), Is.EqualTo(bytes), "a read graph writes the same bytes");
    }

    [Test]
    public void DiffNamesTheFirstDifference()
    {
        var a = Sample(); var b = Sample();
        Assert.That(Snapshot.Diff(a, b), Is.Null);
        b.Lists[5].Add("y");
        Assert.That(Snapshot.Diff(a, b), Does.Contain("Lists"));
        b = Sample();
        b.Nodes[2] = new Node(1) { Next = b.Nodes[1] };         // equal values, not the same object
        Assert.That(Snapshot.Diff(a, b), Does.Contain("shared differently"));
        b = Sample();
        b.P.Tag = "q";
        Assert.That(Snapshot.Diff(a, b), Does.Contain("P.Tag"));
    }

    [Test]
    public void DamagedOrTrailingBytesAreRefused()
    {
        var bytes = Snapshot.Write(Sample());
        Assert.That(() => Snapshot.Read<Root>(bytes.AsMemory(0, bytes.Length - 3)), Throws.InstanceOf<InvalidDataException>());
        Assert.That(() => Snapshot.Read<Root>(bytes.Append((byte)0).ToArray()), Throws.InstanceOf<InvalidDataException>());
    }

    public class Base { public int A; }
    public sealed class Derived : Base { public int B; }
    public sealed class HoldsBase { public Base Value = new Derived(); }

    [Test]
    public void ASubclassInADeclaredClassFieldIsRefused() =>
        Assert.That(() => Snapshot.Write(new HoldsBase()), Throws.InstanceOf<NotSupportedException>());

    [Test]
    public void CacheFileReadsOnlyAWholeFileOfItsKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-cachefile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "t.bin");
            var body = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7)).ToArray();
            CacheFile.Write(path, "k1", body);
            Assert.That(CacheFile.Read(path, "k1"), Is.EqualTo(body));
            Assert.That(CacheFile.KeyOf(path), Is.EqualTo("k1"));
            Assert.That(CacheFile.Read(path, "k2"), Is.Null);
            var all = File.ReadAllBytes(path);
            all[^10] ^= 1;
            File.WriteAllBytes(path, all);
            Assert.That(CacheFile.Read(path, "k1"), Is.Null, "a changed byte fails the checksum");
            File.WriteAllBytes(path, all[..^1]);
            Assert.That(CacheFile.Read(path, "k1"), Is.Null, "a short file");
            Assert.That(CacheFile.Read(Path.Combine(dir, "none.bin"), "k1"), Is.Null);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void StoreParsesOnAMissAndAnotherBuildRemovesTheOldFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-cachestore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CacheStore(dir);
            int parses = 0;
            Root parse() { parses++; return Sample(); }
            var a = store.Get("root", "cfg", "b1", parse, _ => { }, out var o1);
            Assert.That((o1.Source, parses), Is.EqualTo(("parse", 1)));
            var b = store.Get("root", "cfg", "b1", parse, _ => { }, out var o2);
            Assert.That((o2.Source, parses), Is.EqualTo(("cache", 1)));
            Assert.That(Snapshot.Diff(a, b), Is.Null);
            store.Get("root", "other", "b1", parse, _ => { }, out var o3);
            Assert.That(o3.Source, Is.EqualTo("parse"));
            Assert.That(Directory.GetFiles(dir, "root-*.bin"), Has.Length.EqualTo(2), "another configuration keeps its file");
            store.Get("root", "cfg", "b2", parse, _ => { }, out var o4);
            Assert.That(o4.Source, Is.EqualTo("parse"));
            Assert.That(Directory.GetFiles(dir, "root-*.bin"), Has.Length.EqualTo(1), "the other build's files are gone");
            foreach (var f in Directory.GetFiles(dir)) { var x = File.ReadAllBytes(f); x[^1] ^= 1; File.WriteAllBytes(f, x); }
            store.Get("root", "cfg", "b2", parse, _ => { }, out var o5);
            Assert.That(o5.Source, Is.EqualTo("parse"), "a damaged file is parsed again, not half-read");
            Assert.That(store.Ensure("root", "cfg", "b2", parse), Is.False);
            Assert.That(store.Ensure("root", "cfg2", "b2", parse), Is.True);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void StampsRehashAChangedFileAndForgetAGoneOne()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-stamps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "a.m2d");
            File.WriteAllText(f, "one");
            var stamps = new Stamps(Path.Combine(dir, "stamps.tsv"));
            var h1 = stamps.Hash(f);
            Assert.That(h1, Is.EqualTo(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData("one"u8)).ToLowerInvariant()));
            File.WriteAllText(f, "two!");
            var h2 = stamps.Hash(f);
            Assert.That(h2, Is.Not.EqualTo(h1));
            var link = Path.Combine(dir, "link.m2d");
            File.CreateSymbolicLink(link, f);
            Assert.That(stamps.Hash(link), Is.EqualTo(h2));
            Assert.That(File.ReadAllLines(Path.Combine(dir, "stamps.tsv")), Has.Length.EqualTo(1), "a link shares its target's line");
            Assert.That(stamps.Known(), Is.EquivalentTo(new[] { h2 }));
            File.Delete(f);
            Assert.That(stamps.Known(), Is.Empty);
        }
        finally { Directory.Delete(dir, true); }
    }
    static string Sha1(string s) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    [Test]
    public void StampsSeeAFileWrittenWithItsSizeAndWriteTimeKept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-stamps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "a.m2d");
            File.WriteAllText(f, "one");
            var stamps = new Stamps(Path.Combine(dir, "stamps.tsv"));
            Assert.That(stamps.Hash(f), Is.EqualTo(Sha1("one")));
            var when = File.GetLastWriteTimeUtc(f);
            System.Threading.Thread.Sleep(20);
            // written in place, the write time put back: the change time moved
            File.WriteAllText(f, "two");
            File.SetLastWriteTimeUtc(f, when);
            Assert.That(new FileInfo(f).Length, Is.EqualTo(3));
            Assert.That(stamps.Hash(f), Is.EqualTo(Sha1("two")));
            // replaced by another file of the same size and write time: a new inode
            var other = Path.Combine(dir, "b.tmp");
            File.WriteAllText(other, "six");
            File.SetLastWriteTimeUtc(other, when);
            File.Move(other, f, true);
            Assert.That(stamps.Hash(f), Is.EqualTo(Sha1("six")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void StampsRereadHashesTheBytesWhateverTheLineSays()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-stamps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "a.m2d");
            File.WriteAllText(f, "one");
            var tsv = Path.Combine(dir, "stamps.tsv");
            var stamps = new Stamps(tsv);
            stamps.Hash(f);
            // a line whose stamp matches but whose hash is not the file's (a change no stamp sees)
            var wrong = Sha1("other");
            File.WriteAllLines(tsv, File.ReadAllLines(tsv).Select(l => l[..l.LastIndexOf('\t')] + "\t" + wrong));
            Assert.That(stamps.Hash(f), Is.EqualTo(wrong), "a matching stamp is trusted");
            Assert.That(stamps.Hash(f, reread: true), Is.EqualTo(Sha1("one")));
            Assert.That(stamps.Hash(f), Is.EqualTo(Sha1("one")), "the reread hash is the line's from then on");
            // an unchanged file reread leaves the file alone
            var before = File.GetLastWriteTimeUtc(tsv);
            System.Threading.Thread.Sleep(20);
            stamps.Hash(f, reread: true);
            Assert.That(File.GetLastWriteTimeUtc(tsv), Is.EqualTo(before));
        }
        finally { Directory.Delete(dir, true); }
    }
}

using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// AttributePoint (KMS 0x45, GMS2 0x46): CSceneStateGame::OnAttributePoint (scenestategame.cpp:4270), handled by
/// <see cref="StatPointBook.Apply"/> (mode 0 reward points, mode 1 spent stat points).
/// </summary>
public class AttributePointRecordedTests {
    // gms2 recording: 0x46 mode 0 (Extra), packet 2310, 41 bytes (extracted with extract.py): total 5, (1,5),(2,0),(3,0),(4,0)
    static readonly byte[] Gms2Extra = Convert.FromHexString(
        "0005000000040000000100000005000000020000000000000003000000000000000400000000000000");
    // gms2 recording: 0x46 mode 1 (Stats), packet 2312, 14 bytes (extracted with extract.py): total 5, stat 0 = 5
    static readonly byte[] Gms2Stats = Convert.FromHexString("0105000000010000000005000000");

    // kms2 recording: 0x45 mode 0 (Extra), packet 592, 41 bytes (extracted with extract.py): total 69, (1,23),(2,8),(3,12),(4,26)
    static readonly byte[] KmsExtra = Convert.FromHexString(
        "00450000000400000001000000170000000200000008000000030000000C000000040000001A000000");
    // kms2 recording: 0x45 mode 1 (Stats), packet 1811, 19 bytes (extracted with extract.py): total 106, stat 0 = 46, stat 17 = 60
    static readonly byte[] KmsStats = Convert.FromHexString("016A00000002000000002E000000113C000000");

    [Test] public void Gms2ExtraAndStatsUpdateStatPointBook() {
        var book = new StatPointBook();
        int changes = 0;
        book.Changed += () => changes++;

        var re = new ByteReader(Gms2Extra);
        var extra = AttributePointMessage.Read(re) as AttributePointMessage.Extra;
        Assert.That(extra, Is.Not.Null);
        Assert.That(extra.Total, Is.EqualTo(5));
        Assert.That(extra.Values, Is.EqualTo(new (int, uint)[] { (1, 5), (2, 0), (3, 0), (4, 0) }));
        Assert.That(re.Available, Is.EqualTo(0));
        book.Apply(extra);
        Assert.That((book.Total, book.Used, book.Remaining, book.GetExtra(1), changes), Is.EqualTo((5, 0, 5, 5u, 1)));

        var rs = new ByteReader(Gms2Stats);
        var stats = AttributePointMessage.Read(rs) as AttributePointMessage.Stats;
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.Total, Is.EqualTo(5));
        Assert.That(stats.Points, Is.EqualTo(new (byte, int)[] { (0, 5) }));
        Assert.That(rs.Available, Is.EqualTo(0));
        book.Apply(stats);
        Assert.That((book.Total, book.Used, book.Remaining, book.GetStatPoints(0), changes), Is.EqualTo((5, 5, 0, 5, 2)));
    }

    [Test] public void KmsExtraAndStatsUpdateStatPointBook() {
        var book = new StatPointBook();
        int changes = 0;
        book.Changed += () => changes++;

        var re = new ByteReader(KmsExtra);
        var extra = AttributePointMessage.Read(re) as AttributePointMessage.Extra;
        Assert.That(extra, Is.Not.Null);
        Assert.That(extra.Total, Is.EqualTo(69));
        Assert.That(extra.Values, Is.EqualTo(new (int, uint)[] { (1, 23), (2, 8), (3, 12), (4, 26) }));
        Assert.That(re.Available, Is.EqualTo(0));
        book.Apply(extra);
        Assert.That((book.Total, book.Used, book.Remaining, book.GetExtra(1), book.GetExtra(4), changes),
            Is.EqualTo((69, 0, 69, 23u, 26u, 1)));

        var rs = new ByteReader(KmsStats);
        var stats = AttributePointMessage.Read(rs) as AttributePointMessage.Stats;
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.Total, Is.EqualTo(106));
        Assert.That(stats.Points, Is.EqualTo(new (byte, int)[] { (0, 46), (17, 60) }));
        Assert.That(rs.Available, Is.EqualTo(0));
        book.Apply(stats);
        Assert.That((book.Total, book.Used, book.Remaining, book.GetStatPoints(0), book.GetStatPoints(17), changes),
            Is.EqualTo((106, 106, 0, 46, 60, 2)));
    }
}

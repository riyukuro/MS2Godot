using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 SkillPoint (inbound 0x45): the KMS layout without the trailing i32 (recordings, upstream SkillPoint.WriteTo).</summary>
public class Gms2SkillPointTests {
    static byte[] Frame() {
        var w = new ByteWriter();
        w.WriteInt(8); w.WriteInt(2);
        w.WriteInt(1); w.WriteInt(2); w.WriteShort(0); w.WriteInt(8); w.WriteShort(1); w.WriteInt(0);
        w.WriteInt(2); w.WriteInt(1); w.WriteShort(0); w.WriteInt(0);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void ReadsTheWholePacketWithoutATrailingInt() {
        var r = new ByteReader(Frame());
        var p = SkillPointPacket.Read(r, ProtocolProfile.Gms2);
        Assert.That(p.First, Is.EqualTo(8));
        Assert.That(p.Sources, Has.Count.EqualTo(2));
        Assert.That(p.Sources[0].Entries[0], Is.EqualTo((0, 8)));
        Assert.That(p.Extra, Is.EqualTo(0));
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.Catch<Exception>(() => SkillPointPacket.Read(new ByteReader(Frame())));   // the KMS reader runs past the end
    }

    [Test]
    public void EveryRecordedGms2SkillPointPacketDecodes() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.SkillPoint));
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var p = SkillPointPacket.Read(r, ProtocolProfile.Gms2);
            Assert.That(r.Available, Is.EqualTo(0), file);
            Assert.That(p.First, Is.EqualTo(p.Sources.Sum(s => s.Entries.Sum(e => e.Points))), file);
        }
    }
}

/// <summary>GMS2 Job (inbound 0x4A): the frame is the KMS one, the skill book is the GMS2 per-type block.</summary>
public class Gms2JobTests {
    [Test]
    public void EveryRecordedGms2JobPacketDecodes() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.Job));
        Assert.That(all, Is.Not.Empty);
        int withBook = 0;
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var p = JobPacket.Read(r, ProtocolProfile.Gms2);
            Assert.That(r.Available, Is.EqualTo(0), file);
            if (p.Book != null) { withBook++; Assert.That(p.Book.Skills, Is.Not.Empty, file); }
        }
        Assert.That(withBook, Is.GreaterThan(0));
    }

    [Test]
    public void BookModeReadsTheGms2Block() {
        var w = new ByteWriter();
        w.WriteInt(7); w.WriteByte(1);                       // object id, mode 1 (basic)
        w.WriteInt(900); w.WriteByte(1); w.WriteInt(90);     // job 900, one group of job code 90
        w.WriteByte(1); w.WriteBool(false); w.WriteBool(true); w.WriteInt(11000001); w.WriteInt(4); w.WriteByte(0);
        w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var p = JobPacket.Read(r, ProtocolProfile.Gms2);
        Assert.That(p.Book.Rank, Is.EqualTo(900));
        Assert.That(p.Book.JobGroup, Is.EqualTo(90));
        Assert.That(p.Book.Skills.Single(), Is.EqualTo(new BookSkill(11000001, 4, false, true, false)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}

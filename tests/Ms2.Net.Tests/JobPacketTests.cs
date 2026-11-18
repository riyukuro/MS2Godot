using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class JobPacketTests {
    static readonly SkillBook Book = new() {
        Rank = 1, JobGroup = 10,
        Skills = new[] { new BookSkill(10200001, 3, false, true, false), new BookSkill(10200011, 1, true, false, true) },
    };

    [TestCase(0)] [TestCase(2)] [TestCase(7)] [TestCase(11)]
    public void BookModesReadTheSkillBook(int mode) {
        var r = new ByteReader(JobPacket.Write(5001, mode, Book));
        var p = JobPacket.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(p.ObjectId, Is.EqualTo(5001)); Assert.That(p.Mode, Is.EqualTo(mode));
        Assert.That(p.Book.Rank, Is.EqualTo(1)); Assert.That(p.Book.JobGroup, Is.EqualTo(10));
        Assert.That(p.Book.Skills, Is.EqualTo(Book.Skills));
        Assert.That(p.Book.TryGet(10200001, out var s) && s.Learned && s.Level == 3, Is.True);
        Assert.That(p.Book.TryGet(1, out _), Is.False);
    }

    [Test] public void ErrorModeReadsTheCodeAndTheValueOf18() {
        var w = new ByteWriter(); w.WriteInt(7); w.WriteByte(3); w.WriteByte(18); w.WriteInt(42);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var p = JobPacket.Read(r);
        Assert.That(r.Available, Is.EqualTo(0)); Assert.That(p.Book, Is.Null);
        Assert.That((p.Error, p.ErrorValue), Is.EqualTo((18, 42)));
    }
}

using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class QuestPacketTests {
    static QuestMessage Read(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); return QuestMessage.Read(r); }

    [Test] public void LoadStatesThenStartUpdateCompleteAbandon() {
        var book = new QuestBook(); var seen = new List<int>(); book.Changed += seen.Add;
        // Quest.WriteTo: id, state, completion count, start, end, track, count, counters (mode 0x16 from QuestPacket.LoadQuestStates)
        book.Apply(Read(w => { w.WriteByte(0x16); w.WriteInt(2);
            w.WriteInt(1001); w.WriteInt(2); w.WriteInt(1); w.WriteLong(100); w.WriteLong(200); w.WriteBool(false); w.WriteInt(0);
            w.WriteInt(1002); w.WriteInt(1); w.WriteInt(0); w.WriteLong(300); w.WriteLong(0); w.WriteBool(true); w.WriteInt(2); w.WriteInt(3); w.WriteInt(4); }));
        Assert.That(book.Get(1001).State, Is.EqualTo(2)); Assert.That(book.Get(1002).Tracked); Assert.That(book.Get(1002).Progress, Is.EqualTo(new long[] { 3, 4 }));
        book.Apply(Read(w => { w.WriteByte(2); w.WriteInt(1003); w.WriteLong(999); w.WriteBool(true); w.WriteInt(1); w.WriteInt(0); }));
        Assert.That(book.Get(1003).State, Is.EqualTo(1)); Assert.That(book.Get(1003).StartTime, Is.EqualTo(999));
        book.Apply(Read(w => { w.WriteByte(3); w.WriteInt(1003); w.WriteInt(1); w.WriteInt(5); }));
        Assert.That(book.Get(1003).Progress, Is.EqualTo(new long[] { 5 }));
        book.Apply(Read(w => { w.WriteByte(4); w.WriteInt(1003); w.WriteInt(1); w.WriteLong(1234); }));
        Assert.That(book.Get(1003).State, Is.EqualTo(2)); Assert.That(book.Get(1003).CompleteTime, Is.EqualTo(1234));
        book.Apply(Read(w => { w.WriteByte(9); w.WriteInt(1002); w.WriteBool(false); })); Assert.That(book.Get(1002).Tracked, Is.False);
        book.Apply(Read(w => { w.WriteByte(6); w.WriteInt(1002); })); Assert.That(book.Get(1002), Is.Null);
        book.Apply(Read(w => { w.WriteByte(7); w.WriteInt(1); w.WriteInt(1001); })); Assert.That(book.Get(1001), Is.Null);
        Assert.That(seen, Is.EqualTo(new[] { 0, 1003, 1003, 1003, 1002, 1002, 0 }));
    }

    [Test] public void UpdateOfUnknownQuestIsIgnoredAndResetClears() {
        var book = new QuestBook();
        book.Apply(Read(w => { w.WriteByte(3); w.WriteInt(77); w.WriteInt(0); })); Assert.That(book.All, Is.Empty);
        book.Apply(Read(w => { w.WriteByte(2); w.WriteInt(5); w.WriteLong(1); w.WriteBool(false); w.WriteInt(0); }));
        book.Apply(Read(w => { w.WriteByte(0x15); w.WriteInt(9); w.WriteInt(0); }));   // the server's exploration packet resets the lists in the client
        Assert.That(book.All, Is.Empty); Assert.That(book.Group, Is.EqualTo(9));
    }

    [Test] public void ResultZeroRemovesAndOtherModesAreSkipped() {
        var book = new QuestBook();
        book.Apply(Read(w => { w.WriteByte(2); w.WriteInt(5); w.WriteLong(1); w.WriteBool(false); w.WriteInt(0); }));
        book.Apply(Read(w => { w.WriteByte(5); w.WriteInt(5); w.WriteInt(1); })); Assert.That(book.Get(5), Is.Not.Null);
        book.Apply(Read(w => { w.WriteByte(5); w.WriteInt(5); w.WriteInt(0); })); Assert.That(book.Get(5), Is.Null);
        Assert.That(Read(w => w.WriteByte(0x26)), Is.EqualTo(new QuestMessage.Other(0x26)));
    }
}

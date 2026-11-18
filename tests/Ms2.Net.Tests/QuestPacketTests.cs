using System.Linq;
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

    [Test]
    public void DailyDropRemovesOnlyTypeNineDailyUngrouped() {
        var book = new QuestBook();
        var table = new System.Collections.Generic.Dictionary<int, QuestBook.QuestInfoRow> {
            [1] = new(9, 2, 0), [2] = new(9, 3, 0), [3] = new(9, 2, 5), [4] = new(4, 2, 0) };
        book.QuestInfo = id => table.TryGetValue(id, out var r) ? r : null;
        book.Apply(new QuestMessage.Load(new[] { 1, 2, 3, 4, 5 }.Select(i => new QuestRecord { Id = i, State = 1 }).ToList()));
        book.Apply(new QuestMessage.Counts(false, false, new[] { 7 }));      // no drop byte: nothing removed
        Assert.That(book.All.Count, Is.EqualTo(5));
        book.Apply(new QuestMessage.Counts(false, true, new[] { 7 }));
        Assert.That(book.All.Select(q => q.Id).OrderBy(i => i), Is.EqualTo(new[] { 2, 3, 4, 5 }));
        book.Apply(new QuestMessage.Counts(true, true, new int[0]));         // weekly: state 3
        Assert.That(book.All.Select(q => q.Id).OrderBy(i => i), Is.EqualTo(new[] { 3, 4, 5 }));
    }

    static void Record(ByteWriter w, int id, int state, params int[] progress) {
        w.WriteInt(id); w.WriteInt(state); w.WriteInt(0); w.WriteLong(1); w.WriteLong(0); w.WriteBool(false);
        w.WriteInt(progress.Length); foreach (int p in progress) w.WriteInt(p);
    }

    [Test] public void LoginQuestListMisreadsAfterAnIdWithoutQuestData() {
        var w = new ByteWriter(); w.WriteByte(0x16); w.WriteInt(3);
        Record(w, 100, 2, 1); Record(w, 200, 1, 3); Record(w, 300, 1, 0);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var load = (QuestMessage.Load) QuestMessage.Read(r, id => id == 100 || id == 300);
        // 200 has no data: its progress (count 1, value 3) is not read, so the third record starts inside it and is not quest 300
        Assert.That(load.Quests.Select(q => q.Id), Is.EqualTo(new[] { 100 }));
        Assert.That(load.Skipped.First(), Is.EqualTo(200));
        Assert.That(load.Skipped, Does.Not.Contain(300));
        // with every id known all three come through
        var all = (QuestMessage.Load) QuestMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()), _ => true);
        Assert.That(all.Quests.Select(q => q.Id), Is.EqualTo(new[] { 100, 200, 300 }));
        Assert.That(all.Skipped, Is.Empty); Assert.That(all.Overrun, Is.False);
    }
}

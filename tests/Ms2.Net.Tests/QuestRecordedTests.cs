using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Quest (KMS 0x52, GMS2 0x53): CQuestManager::OnQuest, handled by <see cref="QuestBook.Apply"/>.
/// Exercises recorded GMS2 and KMS2 frames for Start (mode 2), Update (mode 3), Complete (mode 4) and Pinned (mode 0x17).
/// </summary>
public class QuestRecordedTests {
    // gms2: 0x53 mode 2 (Start), 18 bytes: quest 50001550, tracked=true
    static readonly byte[] Gms2Start = Convert.FromHexString("028EF6FA02CC49A25E000000000100000000");
    // gms2: 0x53 mode 4 (Complete), 17 bytes: quest 50001550, count 1
    static readonly byte[] Gms2Complete = Convert.FromHexString("048EF6FA0201000000D849A25E00000000");
    // gms2: 0x53 mode 3 (Update), 13 bytes: quest 72000005, progress [1]
    static readonly byte[] Gms2Update = Convert.FromHexString("0305A24A040100000001000000");
    // gms2: 0x53 mode 0x17 (Pinned), 5 bytes: empty list
    static readonly byte[] Gms2PinnedEmpty = Convert.FromHexString("1700000000");

    // kms2: 0x52 mode 2 (Start), 22 bytes: quest 72000133, progress [0]
    static readonly byte[] KmsStart = Convert.FromHexString("0285A24A0499D6416100000000000100000000000000");
    // kms2: 0x52 mode 3 (Update), 21 bytes: quest 90000660, progress [1, 0, 0]
    static readonly byte[] KmsUpdate = Convert.FromHexString("03144D5D0503000000010000000000000000000000");
    // kms2: 0x52 mode 4 (Complete), 17 bytes: quest 20002305, count 1
    static readonly byte[] KmsComplete = Convert.FromHexString("04013631010100000018D5416100000000");
    // kms2: 0x52 mode 0x17 (Pinned), 13 bytes: [90000400, 90000060]
    static readonly byte[] KmsPinned = Convert.FromHexString("1702000000104C5D05BC4A5D05");

    [Test] public void Gms2StartCompleteUpdateAndPinnedUpdateQuestBook() {
        var book = new QuestBook();
        var started = new List<int>();
        var rewarded = new List<int>();
        var progressed = new List<int>();
        book.Started += started.Add;
        book.Rewarded += rewarded.Add;
        book.ProgressUpdated += progressed.Add;

        var rs = new ByteReader(Gms2Start);
        var start = QuestMessage.Read(rs) as QuestMessage.Start;
        Assert.That(start, Is.Not.Null);
        Assert.That((start.Id, start.StartTime, start.Tracked), Is.EqualTo((50001550, 1587694028L, true)));
        Assert.That(start.Progress, Is.Empty);
        Assert.That(rs.Available, Is.EqualTo(0));
        book.Apply(start);
        Assert.That(book.Get(50001550)?.State, Is.EqualTo(1));
        Assert.That(started, Is.EqualTo(new[] { 50001550 }));

        var rc = new ByteReader(Gms2Complete);
        var complete = QuestMessage.Read(rc) as QuestMessage.Complete;
        Assert.That(complete, Is.Not.Null);
        Assert.That((complete.Id, complete.CompletionCount, complete.CompleteTime), Is.EqualTo((50001550, 1, 1587694040L)));
        Assert.That(rc.Available, Is.EqualTo(0));
        book.Apply(complete);
        Assert.That((book.Get(50001550).State, book.Get(50001550).CompletionCount), Is.EqualTo((2, 1)));
        Assert.That(rewarded, Is.EqualTo(new[] { 50001550 }));

        // Update on an unknown quest is ignored; once loaded it updates progress and raises ProgressUpdated
        var ru = new ByteReader(Gms2Update);
        var update = QuestMessage.Read(ru) as QuestMessage.Update;
        Assert.That(update, Is.Not.Null);
        Assert.That((update.Id, update.Progress), Is.EqualTo((72000005, new long[] { 1 })));
        Assert.That(ru.Available, Is.EqualTo(0));
        book.Apply(update);
        Assert.That(progressed, Is.Empty);
        book.Apply(new QuestMessage.Start(72000005, 1, true, new long[] { 0 }));
        book.Apply(update);
        Assert.That(book.Get(72000005).Progress, Is.EqualTo(new long[] { 1 }));
        Assert.That(progressed, Is.EqualTo(new[] { 72000005 }));

        var rp = new ByteReader(Gms2PinnedEmpty);
        var pinned = QuestMessage.Read(rp) as QuestMessage.Pinned;
        Assert.That(pinned, Is.Not.Null);
        Assert.That(pinned.Ids, Is.Empty);
        Assert.That(rp.Available, Is.EqualTo(0));
        book.Apply(pinned);
        Assert.That(book.Pinned, Is.Empty);
    }

    [Test] public void KmsStartUpdateCompleteAndPinnedUpdateQuestBook() {
        var book = new QuestBook();
        var started = new List<int>();
        var progressed = new List<int>();
        var rewarded = new List<int>();
        book.Started += started.Add;
        book.ProgressUpdated += progressed.Add;
        book.Rewarded += rewarded.Add;

        var rs = new ByteReader(KmsStart);
        var start = QuestMessage.Read(rs) as QuestMessage.Start;
        Assert.That(start, Is.Not.Null);
        Assert.That((start.Id, start.StartTime, start.Tracked, start.Progress),
            Is.EqualTo((72000133, 1631704729L, false, new long[] { 0 })));
        Assert.That(rs.Available, Is.EqualTo(0));
        book.Apply(start);
        Assert.That(book.Get(72000133)?.State, Is.EqualTo(1));
        Assert.That(started, Is.EqualTo(new[] { 72000133 }));

        var ru = new ByteReader(KmsUpdate);
        var update = QuestMessage.Read(ru) as QuestMessage.Update;
        Assert.That(update, Is.Not.Null);
        Assert.That((update.Id, update.Progress), Is.EqualTo((90000660, new long[] { 1, 0, 0 })));
        Assert.That(ru.Available, Is.EqualTo(0));
        book.Apply(new QuestMessage.Start(90000660, 1, true, new long[] { 0, 0, 0 }));
        book.Apply(update);
        Assert.That(book.Get(90000660).Progress, Is.EqualTo(new long[] { 1, 0, 0 }));
        Assert.That(progressed, Is.EqualTo(new[] { 90000660 }));

        var rc = new ByteReader(KmsComplete);
        var complete = QuestMessage.Read(rc) as QuestMessage.Complete;
        Assert.That(complete, Is.Not.Null);
        Assert.That((complete.Id, complete.CompletionCount, complete.CompleteTime), Is.EqualTo((20002305, 1, 1631704344L)));
        Assert.That(rc.Available, Is.EqualTo(0));
        book.Apply(new QuestMessage.Start(20002305, 1, true, Array.Empty<long>()));
        book.Apply(complete);
        Assert.That(book.Get(20002305).State, Is.EqualTo(2));
        Assert.That(rewarded, Is.EqualTo(new[] { 20002305 }));

        var rp = new ByteReader(KmsPinned);
        var pinned = QuestMessage.Read(rp) as QuestMessage.Pinned;
        Assert.That(pinned, Is.Not.Null);
        Assert.That(pinned.Ids, Is.EqualTo(new[] { 90000400, 90000060 }));
        Assert.That(rp.Available, Is.EqualTo(0));
        book.Apply(pinned);
        Assert.That(book.Pinned, Is.EquivalentTo(new[] { 90000400, 90000060 }));
    }
    /// <summary>
    /// The quest manager's events the quest tracker listens to (questmanager_2.cpp:1047-1083 and CloseQuestDialogs): mode 5
    /// gives slot 7 its id and result, modes 6 and 7 slot 5 for each quest the book had, mode 9 slot 6, and the quest list
    /// (0x16) and a dropping count list (0x1f) a rebuild.
    /// </summary>
    [Test] public void RemovalTrackingResultAndReloadEventsReachListeners() {
        var book = new QuestBook();
        var removed = new List<int>();
        var tracked = new List<int>();
        var results = new List<(int, int)>();
        int reloads = 0;
        book.Removed += removed.Add;
        book.TrackingChanged += tracked.Add;
        book.ResultReceived += (id, r) => results.Add((id, r));
        book.Reloaded += () => reloads++;
        QuestMessage Frame(string hex) {
            var r = new ByteReader(Convert.FromHexString(hex));
            var m = QuestMessage.Read(r);
            Assert.That(r.Available, Is.EqualTo(0), hex);
            return m;
        }
        foreach (int id in new[] { 11, 12, 13, 14 }) book.Apply(new QuestMessage.Start(id, 1, true, new long[] { 0 }));

        book.Apply(Frame("090B00000000"));             // mode 9: quest 11 untracked
        Assert.That(tracked, Is.EqualTo(new[] { 11 }));
        Assert.That(book.Get(11).Tracked, Is.False);
        book.Apply(Frame("096300000001"));             // an unknown quest: nothing
        Assert.That(tracked, Is.EqualTo(new[] { 11 }));

        book.Apply(Frame("060C000000"));               // mode 6: quest 12 removed
        book.Apply(Frame("0663000000"));               // an unknown quest: nothing
        Assert.That(removed, Is.EqualTo(new[] { 12 }));
        book.Apply(Frame("07020000000D00000063000000"));   // mode 7: quests 13 and 99 expired, only 13 known
        Assert.That(removed, Is.EqualTo(new[] { 12, 13 }));
        Assert.That(book.Get(13), Is.Null);

        book.Apply(Frame("050E00000000000000"));       // mode 5: quest 14 given up, result 0 removes it
        book.Apply(Frame("056300000003000000"));       // another result, an unknown quest: still told
        Assert.That(results, Is.EqualTo(new[] { (14, 0), (99, 3) }));
        Assert.That(book.Get(14), Is.Null);

        Assert.That(reloads, Is.EqualTo(0));
        book.Apply(new QuestMessage.Load(new List<QuestRecord>()));
        Assert.That(reloads, Is.EqualTo(1));
        book.Apply(Frame("1F0000000000"));             // mode 0x1f without the drop byte: no rebuild
        Assert.That(reloads, Is.EqualTo(1));
        book.Apply(Frame("1F0100000000"));             // with it: CloseQuestDialogs
        Assert.That(reloads, Is.EqualTo(2));
    }
}

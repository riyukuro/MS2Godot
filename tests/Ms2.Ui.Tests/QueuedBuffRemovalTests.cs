using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The own PC's dot entry as a queued damage record sees it (<see cref="QueuedBuffRemovals"/>): a dot's last tick and the buff's
/// remove read in one go reach the own list first (the remove, at once) and the record later (the field queue's drain); the
/// record still finds the entry the client's packet order gives it.
/// </summary>
public class QueuedBuffRemovalTests
{
    static readonly AdditionalLevelData Dot = new() { Level = 1, DotSoundMaterial = 100 };
    static AdditionalLevelData Level(uint id, ushort level) => id == 60016061 ? Dot : null;

    static BuffPacket Add(int key) => new(BuffPacket.Add, 5, key, 5, 0, new BuffState(0, 6000, 60016061, 1, 1, 1), 0);
    static BuffPacket Remove(int key) => new(BuffPacket.Remove, 5, key, 5, 0, null, null);

    [Test]
    public void TheLastTickReadBeforeTheRemoveFindsItsEntry()
    {
        var list = new AdditionalBuffList(Level);
        var removals = new QueuedBuffRemovals();
        list.Apply(Add(2));
        // read: tick (queued), remove (taken by the list now, its queued event after the tick)
        removals.Removed(2, list.Find(2));
        list.Apply(Remove(2));
        Assert.That(list.Find(2), Is.Null, "the list took the remove");
        Assert.That(removals.Find(list, 2)?.LevelData?.DotSoundMaterial, Is.EqualTo(100), "the queued tick before the remove");
        removals.Drained(2);
        Assert.That(removals.Find(list, 2), Is.Null, "a record after the remove");
        Assert.That(removals.Count, Is.Zero);
    }

    [Test]
    public void TheListsEntryComesFirstAndARemoveOfNothingKeepsNothing()
    {
        var list = new AdditionalBuffList(Level);
        var removals = new QueuedBuffRemovals();
        list.Apply(Add(2));
        var old = list.Find(2);
        removals.Removed(2, old);
        list.Apply(Remove(2));
        list.Apply(Add(2));   // added again in the same read
        Assert.That(removals.Find(list, 2), Is.SameAs(list.Find(2)).And.Not.SameAs(old));
        removals.Removed(3, list.Find(3));
        Assert.That(removals.Find(list, 3), Is.Null);
        Assert.That(removals.Find(null, 2), Is.SameAs(old), "no list yet");
    }
}

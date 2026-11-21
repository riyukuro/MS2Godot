using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class ServerKeyTableTests {
    static readonly KeyBinding[] Keys = { new(16, 1, 500001, 0, 0), new(44, 2, 300003, 0, 0) };
    static IReadOnlyList<IReadOnlyList<(int, QuickSlotEntry)>> Pages => new[] {
        new[] { (4, new QuickSlotEntry(10000001, 0, 0)), (24, new QuickSlotEntry(0, 20000001, 0x1122334455667788)) },
        Array.Empty<(int, QuickSlotEntry)>(),
    };

    [Test] public void LoadReadsBindingsThenPages() {
        var t = new ServerKeyTable(); var r = new ByteReader(ServerKeyTable.Write(0, false, Keys, 1, Pages));
        Assert.That(t.Apply(r), Is.EqualTo(0)); Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(t.Loaded && !t.UsesDefaultKeys, Is.True);
        Assert.That(t.Bindings, Is.EqualTo(Keys));
        Assert.That(t.PageCount, Is.EqualTo(2)); Assert.That(t.CurrentPage, Is.EqualTo(1));
        Assert.That(t.Entry(0, 4), Is.EqualTo(new QuickSlotEntry(10000001, 0, 0)));
        Assert.That(t.Entry(0, 24).ItemUid, Is.EqualTo(0x1122334455667788));
        Assert.That(t.Entry(0, 5).IsEmpty && t.Entry(1, 4).IsEmpty && t.Entry(9, 0).IsEmpty, Is.True);
    }

    [Test] public void DefaultFlagReadsNothingMore() {
        var t = new ServerKeyTable(); t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Keys, 0, Pages)));
        var r = new ByteReader(ServerKeyTable.Write(0, true, Keys, 0, Pages));
        t.Apply(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(t.UsesDefaultKeys, Is.True); Assert.That(t.Bindings, Is.Empty);
        Assert.That(t.Entry(0, 4).SkillId, Is.EqualTo(10000001));      // the pages stay
    }

    [Test] public void ZeroBindingsMeanDefaultKeysButPagesFollow() {
        var t = new ServerKeyTable(); var r = new ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0, Pages));
        t.Apply(r);
        Assert.That(r.Available, Is.EqualTo(0)); Assert.That(t.UsesDefaultKeys, Is.True); Assert.That(t.PageCount, Is.EqualTo(2));
    }

    [Test] public void Mode7ReplacesPagesAndKeepsAnOutOfRangePage() {
        var t = new ServerKeyTable(); t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Keys, 1, Pages)));
        var one = new[] { new[] { (0, new QuickSlotEntry(10000002, 0, 0)) } };
        Assert.That(t.Apply(new ByteReader(ServerKeyTable.Write(7, false, null, 5, one))), Is.EqualTo(7));
        Assert.That(t.PageCount, Is.EqualTo(1)); Assert.That(t.CurrentPage, Is.EqualTo(1));
        Assert.That(t.Entry(0, 0).SkillId, Is.EqualTo(10000002)); Assert.That(t.Bindings, Is.EqualTo(Keys));
    }
    [Test] public void SetEntryPutsAndEmptiesASlotOfAPage() {
        // CKeyTable::SetQuickSlotEntryOnPage (keytable.cpp:1629), the SetAt the skill set's edits do
        var t = new ServerKeyTable(); t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Keys, 1, Pages)));
        t.SetEntry(1, 7, new QuickSlotEntry(10000003, 0, 0));
        Assert.That(t.Entry(1, 7).SkillId, Is.EqualTo(10000003));
        t.SetEntry(0, 4, default);
        Assert.That(t.Entry(0, 4).IsEmpty, Is.True);
        Assert.That(t.FindQuickSlotBySkillAndItemUid(10000003, 0), Is.EqualTo(7));
        t.SetEntry(5, 0, new QuickSlotEntry(1, 0, 0));                   // no such page: ignored
        Assert.That(t.PageCount, Is.EqualTo(2));
    }

    [Test] public void ADefaultKeysMode0ReadsNoPagesSoTheSlotsDoNotReload() {
        // CKeyTable::OnKeyTable (keytable.cpp:293-303): useDefault 1 loads DefaultKey.xml and returns before SetQuickSlotPage
        var t = new ServerKeyTable();
        t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Keys, 1, Pages)));
        Assert.That(t.PagesRead, Is.True);
        t.Apply(new ByteReader(ServerKeyTable.Write(0, true, Keys, 0, Pages)));
        Assert.That(t.PagesRead, Is.False);
        t.Apply(new ByteReader(ServerKeyTable.Write(7, false, null, 0, Pages)));
        Assert.That(t.PagesRead, Is.True);
        t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0, Pages)));
        Assert.That(t.PagesRead, Is.True, "a binding count of 0 still reads the pages");
    }

    [Test] public void FindBySkillAndItemDoesNotAllocate() {
        var t = new ServerKeyTable(); t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Keys, 1, Pages)));
        t.SetEntry(1, 9, new QuickSlotEntry(0, 20000001, 55)); t.SetEntry(1, 3, new QuickSlotEntry(0, 20000001, 55));
        Assert.That(t.FindQuickSlotBySkillAndItem(0, 20000001, 55), Is.EqualTo(3), "the lowest slot");
        Assert.That(t.FindQuickSlotBySkillAndItemUid(0, 55), Is.EqualTo(3));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { t.FindQuickSlotBySkillAndItem(0, 20000001, 55); t.FindQuickSlotBySkillAndItemUid(0, 55); }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(512));
    }

    [Test] public void FindBySkillAndItemMatchesAllThreeOfTheCurrentPage() {
        // CKeyTable::FindQuickSlotBySkillAndItem @0x14142e150 (keytable.cpp:1600): skill, item id and item uid of the current
        // page; -1 when none matches
        var t = new ServerKeyTable(); t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Keys, 1, Pages)));
        t.SetEntry(1, 6, new QuickSlotEntry(0, 20000001, 55));
        Assert.That(t.FindQuickSlotBySkillAndItem(0, 20000001, 55), Is.EqualTo(6));
        Assert.That(t.FindQuickSlotBySkillAndItem(0, 20000002, 55), Is.EqualTo(-1));
        Assert.That(t.FindQuickSlotBySkillAndItem(0, 20000001, 56), Is.EqualTo(-1));
        t.SetEntry(0, 2, new QuickSlotEntry(0, 20000009, 9));             // another page than the current one
        Assert.That(t.FindQuickSlotBySkillAndItem(0, 20000009, 9), Is.EqualTo(-1));
    }
}

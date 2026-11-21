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

    [Test] public void Mode7IgnoredWhileLocked() {
        var t = new ServerKeyTable(); t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Keys, 1, Pages)));
        t.Locked = true;
        var one = new[] { new[] { (0, new QuickSlotEntry(10000002, 0, 0)) } };
        Assert.That(t.Apply(new ByteReader(ServerKeyTable.Write(7, false, null, 0, one))), Is.EqualTo(7));
        Assert.That(t.PageCount, Is.EqualTo(2));
        Assert.That(t.CurrentPage, Is.EqualTo(1));
        Assert.That(t.Entry(0, 4).SkillId, Is.EqualTo(10000001));
    }

    [Test] public void SetCurrentPageAndSetEntryUpdateState() {
        var t = new ServerKeyTable(); t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Keys, 0, Pages)));
        Assert.That(t.SetCurrentPage(1), Is.True);
        Assert.That(t.CurrentPage, Is.EqualTo(1));
        Assert.That(t.SetCurrentPage(-1), Is.False);
        Assert.That(t.SetCurrentPage(5), Is.False);
        t.SetEntry(1, 2, new QuickSlotEntry(10500001, 0, 0));
        Assert.That(t.Entry(1, 2).SkillId, Is.EqualTo(10500001));
    }

    static byte[] WriteOut(Ms2.Net.Wire.IOutPacket p) {
        var w = new ByteWriter();
        p.Write(w);
        return w.ToArray();
    }

    [Test] public void OutboundKeyTableModesWriteClientLayouts() {
        Assert.That((ushort)Outbound.KeyTable, Is.EqualTo(0x003A));

        // Mode 1: CKeyTable::SendKeyTableMode1 (keytable.cpp:378-395): mode 1, count 1, then key/type/code/slot/priority
        var m1 = new KeyTableBindRequest(new KeyBinding(16, 1, 500001, 3, 2));
        Assert.That(m1.Op, Is.EqualTo(Outbound.KeyTable));
        var r1 = new ByteReader(WriteOut(m1));
        Assert.That(r1.ReadByte(), Is.EqualTo(1));
        Assert.That(r1.ReadInt(), Is.EqualTo(1));
        Assert.That(r1.ReadInt(), Is.EqualTo(16));
        Assert.That(r1.ReadInt(), Is.EqualTo(1));
        Assert.That(r1.ReadLong(), Is.EqualTo(500001L));
        Assert.That(r1.ReadInt(), Is.EqualTo(3));
        Assert.That(r1.ReadByte(), Is.EqualTo(2));
        Assert.That(r1.Available, Is.EqualTo(0));

        // Mode 2: CKeyTable::SendKeysToServer (keytable.cpp:399-425)
        var m2 = new KeyTableSaveAllRequest(Keys);
        Assert.That(m2.Op, Is.EqualTo(Outbound.KeyTable));
        var r2 = new ByteReader(WriteOut(m2));
        Assert.That(r2.ReadByte(), Is.EqualTo(2));
        Assert.That(r2.ReadInt(), Is.EqualTo(2));
        foreach (var k in Keys) {
            Assert.That(r2.ReadInt(), Is.EqualTo(k.Key));
            Assert.That(r2.ReadInt(), Is.EqualTo(k.Type));
            Assert.That(r2.ReadLong(), Is.EqualTo(k.Code));
            Assert.That(r2.ReadInt(), Is.EqualTo(k.Slot));
            Assert.That(r2.ReadByte(), Is.EqualTo(k.Priority));
        }
        Assert.That(r2.Available, Is.EqualTo(0));

        // Mode 3: FUN_14092f690 (unit_14092f420_accountsessionstate.cpp:405-412)
        var entry = new QuickSlotEntry(10000001, 20000001, 0x1122334455667788);
        var m3 = new KeyTableSetQuickSlotRequest(1, entry.SkillId, entry.ItemId, entry.ItemUid, 7);
        Assert.That(m3.Op, Is.EqualTo(Outbound.KeyTable));
        var r3 = new ByteReader(WriteOut(m3));
        Assert.That(r3.ReadByte(), Is.EqualTo(3));
        Assert.That(r3.ReadShort(), Is.EqualTo(1));
        Assert.That(r3.ReadInt(), Is.EqualTo(10000001));
        Assert.That(r3.ReadInt(), Is.EqualTo(20000001));
        Assert.That(r3.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(r3.ReadInt(), Is.EqualTo(7));
        Assert.That(r3.Available, Is.EqualTo(0));

        // Mode 4: FUN_14092fac0 (unit_14092f420_accountsessionstate.cpp:468-475)
        var m4 = new KeyTableAddQuickSlotRequest(2, entry);
        Assert.That(m4.Op, Is.EqualTo(Outbound.KeyTable));
        var r4 = new ByteReader(WriteOut(m4));
        Assert.That(r4.ReadByte(), Is.EqualTo(4));
        Assert.That(r4.ReadShort(), Is.EqualTo(2));
        Assert.That(r4.ReadInt(), Is.EqualTo(10000001));
        Assert.That(r4.ReadInt(), Is.EqualTo(20000001));
        Assert.That(r4.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(r4.ReadInt(), Is.EqualTo(-1));
        Assert.That(r4.Available, Is.EqualTo(0));

        // Mode 5: SendKeyTableMode5 (unit_14092f420_accountsessionstate.cpp:493-498)
        var m5 = new KeyTableRemoveQuickSlotRequest((short)1, 10000001, 0x1122334455667788);
        Assert.That(m5.Op, Is.EqualTo(Outbound.KeyTable));
        var r5 = new ByteReader(WriteOut(m5));
        Assert.That(r5.ReadByte(), Is.EqualTo(5));
        Assert.That(r5.ReadShort(), Is.EqualTo(1));
        Assert.That(r5.ReadInt(), Is.EqualTo(10000001));
        Assert.That(r5.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(r5.Available, Is.EqualTo(0));

        // Mode 6: SendKeyTableMode6 (unit_14092f420_accountsessionstate.cpp:505-511)
        var m6 = new KeyTableAddPageRequest();
        Assert.That(m6.Op, Is.EqualTo(Outbound.KeyTable));
        Assert.That(WriteOut(m6), Is.EqualTo(new byte[] { 6 }));

        // Mode 7: CKeyTable::SendKeyTableMode7 (keytable.cpp:2021-2034)
        var m7 = new KeyTableUnlockRequest();
        Assert.That(m7.Op, Is.EqualTo(Outbound.KeyTable));
        Assert.That(WriteOut(m7), Is.EqualTo(new byte[] { 7 }));

        // Mode 8: SendKeyTableMode8 (unit_14092f420_accountsessionstate.cpp:514-521)
        var m8 = new KeyTableSetPageRequest(2);
        Assert.That(m8.Op, Is.EqualTo(Outbound.KeyTable));
        var r8 = new ByteReader(WriteOut(m8));
        Assert.That(r8.ReadByte(), Is.EqualTo(8));
        Assert.That(r8.ReadShort(), Is.EqualTo(2));
        Assert.That(r8.Available, Is.EqualTo(0));
    }
}

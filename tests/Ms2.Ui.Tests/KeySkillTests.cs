using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The skill a key casts: CKeyTable::GetSkillIdByKey @0x14142dad0 (keytable.cpp:1452), GetKeySkillId @0x14142d9b0 (:1418),
/// IsKeyBoundToAction @0x14142cd50 (:1254), FindQuickSlotIndexByKey @0x14142dcb0 (:1488) and GetQuickSlotItemUidByKey
/// @0x14142ded0 (:1546), over a hand-made key table.
/// </summary>
public class KeySkillTests
{
    const int Key1 = 0x02, Key2 = 0x03, KeyQ = 0x10, KeyE = 0x12, KeyR = 0x13, KeyZ = 0x2c, LShift = 0x2a, RShift = 0x36, KeyX = 0x2d;
    const long Slot0 = KeyTable.FirstQuickSlotAction, Slot1 = KeyTable.FirstQuickSlotAction + 1, Slot2 = KeyTable.FirstQuickSlotAction + 2;

    static KeyTable Keys(params KeyBinding[] bindings)
    {
        var names = new Dictionary<string, string>();
        foreach (var k in new[] { Key1, Key2, KeyQ, KeyE, KeyR, KeyZ, LShift, RShift, KeyX }) names[k.ToString()] = "k" + k;
        foreach (var c in new[] { Slot0, Slot1, Slot2, 0x49401, KeyTable.HeldItemAction, 777L }) names[c.ToString()] = "a" + c;
        var t = new KeyTable(names);
        foreach (var b in bindings) Assert.That(t.SetBinding(b.Key, b.Type, b.Code, b.Slot, b.Priority), Is.True, $"bind {b.Key}");
        return t;
    }

    /// <summary>Two pages: page 0 slot 0 = skill 10200001, slot 1 = an item (skill 0, item uid 55); page 1 slot 0 = 10200011.</summary>
    static ServerKeyTable Slots(int page)
    {
        var t = new ServerKeyTable();
        t.Apply(new ByteReader(ServerKeyTable.Write(0, true, null, 0, null)));
        t.Apply(new ByteReader(ServerKeyTable.Write(7, false, null, page, new IReadOnlyList<(int, QuickSlotEntry)>[]
        {
            new[] { (0, new QuickSlotEntry(10200001, 0, 0)), (1, new QuickSlotEntry(0, 20000001, 55)) },
            new[] { (0, new QuickSlotEntry(10200011, 0, 0)) },
        })));
        return t;
    }

    static int Skill(KeyTable t, int key, ServerKeyTable slots, bool player = true, int? held = null)
        => t.GetSkillIdByKey(key, player, () => 100, () => held, slots);

    [Test] public void AQuickSlotKeyCastsTheSkillOfItsSlotOnTheCurrentPage()
    {
        var t = Keys(new KeyBinding(Key1, 1, Slot0, 0, 0), new KeyBinding(Key2, 1, Slot2, 0, 0));
        Assert.That(Skill(t, Key1, Slots(0)), Is.EqualTo(10200001));
        Assert.That(Skill(t, Key1, Slots(1)), Is.EqualTo(10200011), "m_nQuickSlotPage");
        Assert.That(Skill(t, Key2, Slots(0)), Is.EqualTo(0), "an empty slot");
        Assert.That(Skill(t, Key1, null), Is.EqualTo(0), "no quick slot pages");
        Assert.That(Skill(t, Key1, Slots(0), player: false), Is.EqualTo(0), "no local player");
    }

    [Test] public void TheKeysOfASkillNeedAValidEntryAndTheSkill()
    {
        // CKeyTable::FUN_14142c6c0 (keytable.cpp:1125-1138) over IsKeyEntryValid (:1292-1314)
        var t = Keys(new KeyBinding(Key1, 1, Slot0, 0, 0), new KeyBinding(Key2, 1, Slot2, 0, 0), new KeyBinding(KeyQ, 1, 0x49401, 0, 0),
                     new KeyBinding(KeyE, 3, 0x49401, 10200001, 0), new KeyBinding(KeyR, 0, 0x49401, 10200001, 0));
        var s = Slots(0);
        Assert.That(t.IsKeyOfSkill(Key1, 10200001, s), Is.True);
        Assert.That(t.IsKeyOfSkill(Key1, 10200011, s), Is.False, "another skill");
        Assert.That(t.IsKeyOfSkill(Key1, 10200011, Slots(1)), Is.True, "the current page");
        Assert.That(t.IsKeyOfSkill(Key2, 0, s), Is.False, "an empty quick slot is not a valid entry");
        Assert.That(t.IsKeyOfSkill(KeyQ, 0, s), Is.False, "a type 1 key with slot 0 is not a valid entry");
        Assert.That(t.IsKeyOfSkill(KeyE, 10200001, s), Is.True, "another type: the slot");
        Assert.That(t.IsKeyOfSkill(KeyR, 10200001, s), Is.False, "type 0");
        Assert.That(t.IsKeyOfSkill(KeyZ, 0, s), Is.False, "an unbound key");
        long Window()   // measured twice, the second asserted (the first can take one-time JIT allocations)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) t.IsKeyOfSkill(1 + i % 0xd3, 10200001, s);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Window();
        Assert.That(Window(), Is.LessThan(512));
    }

    [Test] public void TheAttackKeyNeedsABindingTypeAndComesFirst()
    {
        var t = Keys(new KeyBinding(KeyZ, 1, 0x49401, 0, 0), new KeyBinding(KeyX, 0, 0x49401, 9, 0));
        Assert.That(Skill(t, KeyZ, Slots(0)), Is.EqualTo(100), "GetNormalAttackSkillId");
        Assert.That(Skill(t, KeyX, Slots(0)), Is.EqualTo(9), "type 0 is not bound (IsKeyBoundToAction), GetKeySkillId gives the slot");
        Assert.That(t.IsKeyBoundToAction(KeyX, 0x49401), Is.False);
    }

    [Test] public void TheHeldItemKeyGivesTheHeldItemsSkillElseItsBinding()
    {
        var t = Keys(new KeyBinding(KeyQ, 1, KeyTable.HeldItemAction, 4, 0));
        Assert.That(Skill(t, KeyQ, Slots(0), held: 0), Is.EqualTo(0), "a held item without a skill");
        Assert.That(Skill(t, KeyQ, Slots(0), held: 30000001), Is.EqualTo(30000001));
        Assert.That(Skill(t, KeyQ, Slots(0)), Is.EqualTo(4), "no held item: GetKeySkillId, type 1 = the slot");
    }

    [Test] public void TypeTwoNeedsTheDynamicActionList()
    {
        var t = Keys(new KeyBinding(KeyE, 2, 777, 5, 0), new KeyBinding(KeyR, 3, 777, 6, 0));
        Assert.That(t.GetKeySkillId(KeyE, null), Is.EqualTo(0));
        Assert.That(t.GetKeySkillId(KeyE, null, c => c == 777), Is.EqualTo(777), "CDynamicActionData::IsListed");
        Assert.That(t.GetKeySkillId(KeyR, null), Is.EqualTo(6), "any other type: the slot");
        Assert.That(t.GetKeySkillId(0x99, null), Is.EqualTo(0), "no key data");
    }

    [Test] public void TheItemUidComesFromAnyPriorityOrThePairKey()
    {
        var t = Keys(new KeyBinding(Key1, 1, Slot0, 0, 0), new KeyBinding(KeyQ, 1, Slot1, 0, 2), new KeyBinding(LShift, 1, Slot1, 0, 1));
        // CKeyTable::GetQuickSlotItemUidByKey (keytable.cpp:1546): the uid of the item the shown SkillSlot finds (vfunc_25), which
        // for slot 1 is another bag item than the page entry's uid 55 (the slot finds the first usable item of the id)
        Func<int, long> shown = i => i == 1 ? 66L : 0L;
        Assert.That(t.FindQuickSlotIndexByKey(KeyQ), Is.EqualTo(1), "priority 2");
        Assert.That(t.GetQuickSlotItemUidByKey(KeyQ, shown), Is.EqualTo(66L));
        Assert.That(t.GetQuickSlotItemUidByKey(RShift, shown), Is.EqualTo(66L), "the pair key");
        Assert.That(t.GetQuickSlotItemUidByKey(Key1, shown), Is.EqualTo(0L), "a skill slot");
        Assert.That(t.GetQuickSlotItemUidByKey(KeyQ, null), Is.EqualTo(0L), "no main player info");
        Assert.That(t.FindQuickSlotIndexByKey(KeyZ), Is.EqualTo(-1));
    }

    [Test] public void TheDefaultKeysBindTheAttackAndTheQuickSlots()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var t = new KeyTable(new PackSet(d), "en");
        int attack = Enumerable.Range(1, 0xcf).FirstOrDefault(k => t.IsKeyBoundToAction(k, 0x49401));
        Assert.That(attack, Is.Not.EqualTo(0), "DefaultKey.xml's attack row has a type");
        int slotKey = t.FindKeyByQuickSlot(0, 0);
        Assert.That(slotKey, Is.Not.EqualTo(0));
        Assert.That(t.GetSkillIdByKey(slotKey, true, () => 100, () => null, Slots(1)), Is.EqualTo(10200011));
    }
}

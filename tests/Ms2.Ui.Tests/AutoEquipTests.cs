using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The global inventory's auto-equip on level up (Unk_Inventory::FUN_140a66990 and its helpers, inventory.cpp:1755-1882):
/// the candidates per slot kind, the pending item, the queue, the excluded items, the switch and their callers' entry
/// points, over items made here (no data set); every refused case asks nothing. Then the internal command and a sweep of
/// what it reads on the data set (MS2_DATA).
/// </summary>
public class AutoEquipTests
{
    /// <summary>An item: preset = its slot kind here (FirstGroupType is the identity), the gear score sets its item level
    /// (gear * 10 at grade 1, subtype 0).</summary>
    static Item Gear(long uid, int levelLimit, int slot, int gearScore = 10, int skin = 0, int type = 1)
    {
        var d = new ItemData { Id = 11000000 + (int)uid, Type = type, LevelLimit = levelLimit, ItemPreset = slot, GearScore = gearScore, SkinType = skin };
        var it = Item.Create(d, 1);
        it.Uid = uid;
        return it;
    }

    sealed class Field
    {
        public bool Feature = true;
        public int? Level = 20;
        public readonly List<Item> Bag = new();
        public readonly Dictionary<(int, int), Item> Worn = new();
        /// <summary>CEquipment's slots, when set: GetEquipBySlotKind reads them instead of <see cref="Worn"/>.</summary>
        public EquipmentSlots Slots;
        public Func<Item, int> UseError = _ => 0;
        public int Answer = 1;
        public readonly List<long> Asked = new();
        public AutoEquip Make()
        {
            var a = new AutoEquip
            {
                FeatureEnabled = () => Feature,
                EquipTabItems = () => Bag,
                FindItemByUid = uid => Bag.FirstOrDefault(i => i.Uid == uid),
                PlayerLevel = () => Level,
                WornAt = (slot, layer) => Slots != null ? Slots.At(slot, layer) : Worn.TryGetValue((slot, layer), out var w) ? w : null,
                ItemUseError = i => UseError(i),
                RequestEquipItem = i => { Asked.Add(i.Uid); return Answer; },
                FirstGroupType = preset => preset,
            };
            a.ResetAutoEquip();
            return a;
        }
    }

    [Test]
    public void LevelUpAsksTheBestItemOfEachEmptySlotOfThatLevel()
    {
        var f = new Field { Level = 20 };
        f.Bag.AddRange(new[]
        {
            Gear(1, 20, 3, gearScore: 10),
            Gear(2, 20, 3, gearScore: 20),   // slot 3: strictly higher, replaces 1
            Gear(3, 20, 3, gearScore: 20),   // slot 3: equal, keeps 2
            Gear(4, 20, 18, gearScore: 5),   // slot 18 (bin 1)
            Gear(5, 20, 1),                  // slot 1 (bin 1, after 18)
            Gear(6, 19, 8),                  // another level limit: not this level's
            Gear(7, 20, 9),                  // slot 9 is worn
            Gear(8, 20, 11, skin: 1),        // a skin item: layer 1
            Gear(9, 20, 12, type: 2),        // not equipment: never put on (FUN_140a67020)
            Gear(10, 20, 17),                // slot 17 (bin 0)
        });
        f.Worn[(9, 0)] = Gear(90, 1, 9);
        Assert.That((ItemLevels.Of(f.Bag[0])?.Total, ItemLevels.Of(f.Bag[1])?.Total), Is.EqualTo(((int?)100, (int?)200)));
        var a = f.Make();
        Assert.That(a.CollectAutoEquipCandidates(20).Select(i => i.Uid), Is.EqualTo(new long[] { 10, 5, 4, 2, 9 }),
            "the map's walk: bin 0 (17), bin 1 newest first (1, 18), then 3 and 12; the type 2 item is a candidate");
        Assert.That(a.CollectAutoEquipCandidates(0).Select(i => i.Uid), Does.Contain(6L), "level 0: any level limit");

        a.OnLevelUp(20);
        Assert.That(f.Asked, Is.EqualTo(new long[] { 10 }), "one asked at once, RequestEquipItem(item, 0, 0, 0)");
        Assert.That(a.PendingItem, Is.EqualTo(10L), "the asked one is pending");
        Assert.That(a.QueuedUids, Is.EqualTo(new long[] { 5, 4, 2 }), "the rest wait; the type 2 item fails FUN_140a67020");

        a.ProcessAutoEquipQueue();
        Assert.That(f.Asked.Count, Is.EqualTo(1), "nothing while an item is pending");
        Assert.That(a.OnOwnPutOn(4), Is.False, "another item put on: the pending stays");
        Assert.That(a.OnOwnPutOn(10), Is.True, "the pending one put on: cleared, the notice is shown");
        Assert.That(a.PendingItem, Is.EqualTo(0L));
        a.ProcessAutoEquipQueue();
        Assert.That((f.Asked.Last(), a.PendingItem, a.QueuedUids.Count()), Is.EqualTo((5L, 5L, 2)), "one per frame, the head");
        f.Worn[(1, 0)] = Gear(91, 1, 1);
        a.OnOwnPutOn(5);
        f.Worn[(18, 0)] = Gear(92, 1, 18);   // something else filled slot 18 meanwhile
        a.ProcessAutoEquipQueue();
        Assert.That((f.Asked.Count, a.PendingItem, a.QueuedUids), Is.EqualTo((2, 0L, new long[] { 2 })),
            "the head is checked again (FUN_140a67020): its slot is taken, it leaves the queue unasked");
        f.Answer = 0;
        a.ProcessAutoEquipQueue();
        Assert.That((f.Asked.Last(), a.PendingItem, a.QueuedUids.Count()), Is.EqualTo((2L, 0L, 0)),
            "asked and refused (RequestEquipItem 0): not pending, gone from the queue");
    }

    [Test]
    public void RefusedCasesAskNothing()
    {
        void Check(string why, Action<Field> set, Action<AutoEquip> after = null)
        {
            var f = new Field { Level = 20 };
            f.Bag.Add(Gear(1, 20, 3));
            set(f);
            var a = f.Make();
            after?.Invoke(a);
            var item = f.Bag.FirstOrDefault() ?? Gear(1, 20, 3);
            a.OnLevelUp(20);
            a.OnNewItem(item);
            a.TryAutoEquip(item);
            a.Enqueue(item);
            a.ProcessAutoEquipQueue();
            Assert.That((f.Asked.Count, a.PendingItem, a.QueuedUids.Count()), Is.EqualTo((0, 0L, 0)), why);
        }
        Check("feature 232 off", f => f.Feature = false);
        Check("the switch off (AutoInstallEquipMent 0)", _ => { }, a => a.SetAutoEquipEnabled(false));
        Check("no local player", f => f.Level = null);
        Check("below AutoInstallEquipmentMinLevel", f => f.Level = 4);
        Check("above AutoInstallEquipmentMaxLevel", f => f.Level = 20, a => a.MaxLevel = 19);
        Check("GetItemUseError not 0", f => f.UseError = _ => 3);
        Check("the slot is worn", f => f.Worn[(3, 0)] = Gear(9, 1, 3));
        Check("excluded (taken off before)", _ => { }, a => a.ExcludeFromAutoEquip(1));
        Check("the bag no longer has it", f => f.Bag.Clear());
        Check("a skin item (layer 1)", f => { f.Bag.Clear(); f.Bag.Add(Gear(1, 20, 3, skin: 1)); });
        Check("not an equipment item", f => { f.Bag.Clear(); f.Bag.Add(Gear(1, 20, 3, type: 2)); });

        // the level up reads the level limit; the new item and the queue do not
        var g = new Field { Level = 30 };
        g.Bag.Add(Gear(1, 20, 3));
        var b = g.Make();
        b.OnLevelUp(30);
        Assert.That(g.Asked, Is.Empty, "a level 20 item on a level 30 level up");
        b.OnNewItem(g.Bag[0]);
        Assert.That(g.Asked, Is.EqualTo(new long[] { 1 }), "a new item of any level limit is tried (mode 7)");
    }

    [Test]
    public void BindBoxAnswerExcludeAndReset()
    {
        var f = new Field();
        f.Bag.Add(Gear(1, 20, 3));
        var a = f.Make();
        a.OnLevelUp(20);
        Assert.That(a.PendingItem, Is.EqualTo(1L), "the bind box shown counts as asked (RequestEquipItem 1)");
        a.OnBindEquipAnswered(1, 1);
        Assert.That(a.PendingItem, Is.EqualTo(1L), "the yes that asked: still pending until the put-on");
        a.OnBindEquipAnswered(2, 0);
        Assert.That(a.PendingItem, Is.EqualTo(1L), "another uid");
        f.Feature = false;
        a.OnBindEquipAnswered(1, 0);
        Assert.That(a.PendingItem, Is.EqualTo(1L), "feature 0xe8 off");
        f.Feature = true;
        a.OnBindEquipAnswered(1, 0);
        Assert.That(a.PendingItem, Is.EqualTo(0L), "the no (or a refused yes) for the pending uid clears it");

        f.Feature = false;
        a.ExcludeFromAutoEquip(5);
        Assert.That(a.ExcludedUids, Is.Empty, "ExcludeFromAutoEquip needs feature 232");
        f.Feature = true;
        a.ExcludeFromAutoEquip(5); a.ExcludeFromAutoEquip(5); a.ExcludeFromAutoEquip(6);
        Assert.That(a.ExcludedUids, Is.EqualTo(new long[] { 5, 6 }), "each uid once");
        a.SetPendingItem(7);
        a.SetAutoEquipEnabled(false);
        a.ResetAutoEquip();
        Assert.That((a.PendingItem, a.ExcludedUids.Count, a.QueuedUids.Count(), a.IsAutoEquipEnabled), Is.EqualTo((0L, 0, 0, true)),
            "ResetAutoEquip: all cleared, the switch back to feature 232");
        f.Feature = false;
        a.ResetAutoEquip();
        Assert.That(a.IsAutoEquipEnabled, Is.False);
    }

    /// <summary>
    /// GetEquipBySlotKind is CEquipment::GetItemAt (pc.cpp:1370), whose slots ApplyEquipItem fills for every group of a worn
    /// item's model (equipment.cpp:2174): pants (PA) are not asked while a top-and-bottom outfit (CL+PA) is worn, an
    /// off-hand (LH) not while a two-hander (RH+LH) is, at the level up and as a new item alike; with a one-slot top or a
    /// one-handed weapon in their place both are asked.
    /// </summary>
    [Test]
    public void AnItemOfSeveralSlotsKeepsEachOfThemFromTheAutoEquip()
    {
        foreach (var (wornSlot, groups, candidateSlot, takes) in new[]
        {
            (8, new[] { 8, 9 }, 9, true),     // a top-and-bottom outfit; pants
            (8, new[] { 8 }, 9, false),       // a top only
            (5, new[] { 5, 4 }, 4, true),     // a two-hander; an off-hand
            (5, new[] { 5 }, 4, false),       // a one-handed weapon
        })
        {
            string why = $"worn {string.Join("+", groups)} at {wornSlot}, candidate slot {candidateSlot}";
            var f = new Field { Level = 20, Slots = new EquipmentSlots() };
            f.Slots.Apply(Gear(90, 10, wornSlot), wornSlot, 0, groups);
            var a = f.Make();
            var candidate = Gear(1, 20, candidateSlot);
            f.Bag.Add(candidate);
            Assert.That(a.CanAutoEquip(candidate), Is.EqualTo(!takes), why);
            a.OnLevelUp(20);
            a.OnNewItem(candidate);
            Assert.That(f.Asked, takes ? Is.Empty : Is.EqualTo(new long[] { 1 }), why);
        }
    }

    [Test]
    public void SlotMapWalkOrder()
    {
        Assert.That(AutoEquip.SlotMapOrder(new[] { 5, 18, 1, 17, 3 }), Is.EqualTo(new[] { 17, 1, 18, 3, 5 }),
            "17 bins: 17 in bin 0; 18 and 1 in bin 1, newest first");
        Assert.That(AutoEquip.SlotMapOrder(new[] { 12, 2, 8 }), Is.EqualTo(new[] { 2, 8, 12 }));
    }

    [Test]
    public void InternalCommandSetsTheSwitch()
    {
        var seen = new List<bool>();
        var unported = new List<(string, string)>();
        var hooks = new UserCommands.Hooks { SetAutoEquipEnabled = seen.Add, Unported = (n, a) => { unported.Add((n, a)); return 1; } };
        UserCommands.ExecuteInternal("AutoInstallEquipMent 1", hooks);
        UserCommands.ExecuteInternal("autoinstallequipment 0", hooks);
        UserCommands.ExecuteInternal("AutoInstallEquipMent", hooks);
        UserCommands.ExecuteInternal("AutoInstallEquipMent -1", hooks);
        UserCommands.ExecuteInternal("AutoInstallEquipMent  7x", hooks);
        UserCommands.ExecuteInternal("AutoInstallEquipMent abc", hooks);
        Assert.That(seen, Is.EqualTo(new[] { true, false, false, false, true, false }), "_wtoi of the first argument above 0");
        UserCommands.ExecuteInternal("NoSuchCommand 1", hooks);
        UserCommands.ExecuteInternal("title 5", hooks);
        Assert.That((seen.Count, unported.Single()), Is.EqualTo((6, ("title", "5"))), "unknown: nothing; another internal one: unported");
        Assert.That(UserCommands.RegisteredInternal, Has.Length.EqualTo(11));
    }

    // ---- the data set ----

    static PackSet packs;
    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
        }
        return packs;
    }

    /// <summary>
    /// What the auto-equip reads on the data set: feature 232 by name, the constants AutoInstallEquipmentMinLevel / MaxLevel
    /// (their defaults 5 / 99 when the data has none) and the notice string with its {0}. Prints them.
    /// </summary>
    [Test]
    public void SweepAutoEquipData()
    {
        var p = Packs();
        var t = new GameTables(p, "en");
        string set = Environment.GetEnvironmentVariable("MS2_DATA");
        string name = FeatureTable.Names[AutoEquip.FeatureId - 1];
        bool on = LobbyFeatures.IsEnabledByName(p, name);
        var c = FeatureElements.Constants(p);
        string min = c.String("AutoInstallEquipmentMinLevel"), max = c.String("AutoInstallEquipmentMaxLevel");
        int lo = c.Int("AutoInstallEquipmentMinLevel", AutoEquip.DefaultMinLevel), hi = c.Int("AutoInstallEquipmentMaxLevel", AutoEquip.DefaultMaxLevel);
        Assert.That(lo, Is.InRange(1, 99));
        Assert.That(hi, Is.InRange(lo, 99));
        string notice = t.Common(AutoEquip.PutOnNoticeKey);
        Assert.That(notice, Is.Not.Empty, "s_msg_item_auto_install_equipment has a text");
        Assert.That(notice, Does.Contain("{0}"), "formatted with the item's name");
        Assert.That(AutoEquip.PutOnNotice(t.Common, "X"), Does.Contain("X"));
        TestContext.WriteLine($"{set}: feature 232 {name} {(on ? "on" : "off")}; AutoInstallEquipmentMinLevel {min ?? "(default)"} -> {lo}, MaxLevel {max ?? "(default)"} -> {hi}; notice {notice.Length} chars");
    }
}

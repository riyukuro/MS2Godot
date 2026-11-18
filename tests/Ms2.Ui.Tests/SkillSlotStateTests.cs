using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The quick slot state (SkillSlot) and the cast check (CSkillSystem::FUN_14176a8c0 / FUN_14176a530) as pure functions
/// over hand-made skill and item rows.</summary>
public class SkillSlotStateTests
{
    static List<XElement> Levels(XElement e, string name, string attr) => e.Elements(name).ToList();

    static SkillTables Skills(params string[] skills)
    {
        var doc = XDocument.Parse("<ms2>" + string.Concat(skills) + "</ms2>");
        return new SkillTables(JobTable.Empty, new SkillDataTable(() => new[] { doc }, Levels));
    }

    // main type 1 is not a book skill kind, so JobSystemCli gives it level 1 without a book entry
    const string Attack = "<skill id=\"100\"><basic mainType=\"1\"><ui icon=\"./icon/100.png\"/>"
        + "<condition useCoolDownShowUI=\"1\" sp=\"5\"><weapon lh=\"0\" rh=\"52\"/></condition></basic>"
        + "<level value=\"1\" sp=\"3\" cooldown=\"8000\"><motion sequenceName=\"Bow_Attack\"/></level></skill>";
    // main type 3: an item kind counted by its items (FUN_1407e9d70 / FUN_1407e9ea0)
    const string Potion = "<skill id=\"200\"><basic mainType=\"3\"><ui icon=\"./icon/200.png\"/></basic><level value=\"1\"/></skill>";

    static readonly Dictionary<int, ItemData> items = new()
    {
        [20000001] = ItemData.Load(20000001, XElement.Parse("<e><property type=\"2\" slotMax=\"100\"/><skill skillID=\"200\"/></e>")),
        [30000001] = ItemData.Load(30000001, XElement.Parse("<e><basic stringTag=\"ugcmapox\"/><property type=\"4\" slotMax=\"1\"/></e>")),
        [30000002] = ItemData.Load(30000002, XElement.Parse("<e><property type=\"4\" slotMax=\"1\"/><limit levelLimit=\"30\"/></e>")),
    };

    static SlotWorld World(SkillTables t, params EquipItem[] bag)
    {
        var w = new SlotWorld
        {
            Tables = t, ItemData = id => items.GetValueOrDefault(id), Jobs = new JobSystem(t), Level = 10,
            Hp = 100, Sp = 100, Ep = 100, Field = new SlotField(), Weapon = (52, 0),
            Inventory = bag, TabOf = id => items.ContainsKey(id) ? 2 : -1,
        };
        w.CanUseItem = i => w.CanUseItemDefault(i, DateTimeOffset.FromUnixTimeSeconds(1_000_000));
        return w;
    }

    [Test]
    public void TheCastCheckGivesTheClientReasonCodes()
    {
        var t = Skills(Attack);
        var w = World(t);
        var lv = CastGate.LevelData(w, 100);
        Assert.That(CastGate.Why(w, lv, true), Is.EqualTo(0));
        w.Weapon = (51, 0);
        Assert.That(CastGate.Why(w, lv, true), Is.EqualTo(0x1c), "no matching weapon");
        w.Weapon = (52, 0);
        w.ItemSlotDisabled = slot => slot == 5;
        Assert.That(CastGate.Why(w, lv, true), Is.EqualTo(0x1c), "the right hand slot is blocked");
        w.ItemSlotDisabled = _ => false;
        w.Sp = 4;
        Assert.That(CastGate.Why(w, lv, true), Is.EqualTo(0xb), "condition sp 5");
        w.Sp = 100; w.Hp = 0;
        Assert.That(CastGate.Why(w, lv, true), Is.EqualTo(5));
        w.Hp = 100; w.Field = null;
        Assert.That(CastGate.Why(w, lv, true), Is.EqualTo(0xd), "no field");
        w.Field = new SlotField { SkillUseDisableMask = 1u << 1 };
        Assert.That(CastGate.Why(w, lv, true), Is.EqualTo(0x1e), "the field blocks main type 1");
        w.Field = new SlotField();
        w.CooldownRemain = (_, _) => 2f;
        Assert.That(CastGate.Why(w, lv, true), Is.EqualTo(2), "cooling down, shown in the UI");
    }

    [Test]
    public void ASkillSlotGraysAsTheClientDoes()
    {
        var t = Skills(Attack, Potion);
        var w = World(t);
        var s = new SkillSlotState(w, 100, 0, 0);
        Assert.That(s.IconName(), Is.EqualTo("./icon/100.png"));
        Assert.That(s.IsDisabled(hotkey: 30), Is.False);
        // a motion skill with no key and no skill set
        Assert.That(s.IsDisabled(hotkey: 0), Is.True);
        w.SkillSetActive = true;
        Assert.That(s.IsDisabled(hotkey: 0), Is.False);
        // cooling down with no charge left
        w.CooldownRemain = (_, _) => 3f;
        Assert.That(s.IsDisabled(30), Is.True);
        Assert.That((s.CooldownDuration(), s.CooldownRemain()), Is.EqualTo((8f, 3f)));
        w.CooldownRemain = (_, _) => 0f;
        // the buffs' main type mask
        w.BuffSkillMask = 1u << 1;
        Assert.That(s.IsDisabled(30), Is.True);
        w.BuffSkillMask = 0;
        Assert.That(new SkillSlotState(w, 999, 0, 0).IsDisabled(30), Is.True, "an unknown skill");
    }

    [Test]
    public void AnItemKindSkillCountsItsItems()
    {
        var t = Skills(Attack, Potion);
        var potions = new[]
        {
            new EquipItem { Id = 20000001, Uid = 1, Amount = 7, InventorySlot = 3 },
            new EquipItem { Id = 20000001, Uid = 2, Amount = 5, InventorySlot = 1 },
        };
        var w = World(t, potions);
        // a potion on a quick slot keeps its skill and item (GetItemCount: an item count kind counts the item id's stacks)
        var slot = new SkillSlotState(w, 200, 20000001, 1);
        Assert.That((slot.ItemCount(false), slot.Item()?.Uid, slot.IsDisabled(0)), Is.EqualTo((12, 1L, false)));
        Assert.That(slot.IconName(), Does.EndWith("20000001.png"));
        // with the skill alone the slot finds the first usable item of the skill in slot order, but counts nothing: the
        // client's count kind branch asks the item data of item id 0
        var skillOnly = new SkillSlotState(w, 200, 0, 0);
        Assert.That((skillOnly.Item()?.Uid, skillOnly.ItemCount(false), skillOnly.IsDisabled(30)), Is.EqualTo((2L, 0, false)));
        // none left: the skill-only slot grays (no item, count 0)
        Assert.That(new SkillSlotState(World(t), 200, 0, 0).IsDisabled(30), Is.True);
    }

    [Test]
    public void AnItemSlotChecksItsItem()
    {
        var t = Skills();
        var w = World(t, new EquipItem { Id = 30000002, Uid = 9, Amount = 1 });
        // a missing item grays, unless it is a UGCMapOX item (string tag 0xd9)
        Assert.That(new SkillSlotState(w, 0, 20000001, 0).IsDisabled(0), Is.True);
        Assert.That(new SkillSlotState(w, 0, 30000001, 0).IsDisabled(0), Is.False);
        // type 4, level limit 30 at level 10
        var s = new SkillSlotState(w, 0, 30000002, 9);
        Assert.That(s.IsDisabled(0), Is.True);
        w.Level = 30;
        Assert.That(s.IsDisabled(0), Is.False);
        Assert.That(s.ItemCount(false), Is.EqualTo(0), "not stackable");
        // an expired item cannot be used
        var expired = World(t, new EquipItem { Id = 30000002, Uid = 9, Amount = 1, ExpiryTime = 10 });
        expired.Level = 30;
        Assert.That(new SkillSlotState(expired, 0, 30000002, 9).IsDisabled(0), Is.True);
    }
}

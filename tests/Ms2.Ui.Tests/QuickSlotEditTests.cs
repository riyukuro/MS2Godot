using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Items;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The quick slot edits FUN_14092f690 and SendKeyTableMode5 (unnamed/unit_14092f420_accountsessionstate.cpp:352, :481)
/// over hand-made skill and item rows.</summary>
public class QuickSlotEditTests
{
    static List<XElement> Levels(XElement e, string name, string attr) => e.Elements(name).ToList();

    const string Active = "<skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\"/></skill>";
    const string Passive = "<skill id=\"300\"><basic mainType=\"6\"/><level value=\"1\"/></skill>";
    const string Ride = "<skill id=\"80000001\"><basic mainType=\"1\"/><level value=\"1\"/></skill>";

    static readonly Dictionary<int, ItemData> items = new()
    {
        [20000001] = ItemData.Load(20000001, XElement.Parse("<e><property type=\"2\" slotMax=\"100\"/></e>")),   // consumable
        [11000001] = ItemData.Load(11000001, XElement.Parse("<e><property type=\"1\" slotMax=\"1\"/></e>")),     // gear
        [29000001] = ItemData.Load(29000001, XElement.Parse("<e><property type=\"21\" slotMax=\"1\"/></e>")),    // lapenshard
        [20000002] = ItemData.Load(20000002, XElement.Parse("<e><property type=\"2\" slotMax=\"100\"/><skill skillID=\"100\"/></e>")),   // potion with a skill
        [30000010] = ItemData.Load(30000010, XElement.Parse("<e><property type=\"9\" slotMax=\"1\"/></e>")),     // ride
        [30000011] = ItemData.Load(30000011, XElement.Parse("<e><property type=\"9\" slotMax=\"1\"/><ucc mesh=\"m\"/></e>")),   // ugc ride
        [29000002] = ItemData.Load(29000002, XElement.Parse("<e><property type=\"21\" slotMax=\"1\"/><function name=\"Lapenshard\"/><skill skillID=\"100\"/></e>")),
    };

    static SlotWorld World(params EquipItem[] bag)
    {
        var doc = XDocument.Parse("<ms2>" + Active + Passive + Ride + "</ms2>");
        return new SlotWorld
        {
            Tables = new SkillTables(JobTable.Empty, new SkillDataTable(() => new[] { doc }, Levels)),
            ItemData = id => items.GetValueOrDefault(id), Inventory = bag,
        };
    }

    static byte[] Bytes(IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void AnActiveSkillIsSentAsMode3WithThePageAndSlot()
    {
        var sent = new List<IOutPacket>();
        Assert.That(QuickSlotEdit.Add(World(), _ => false, sent.Add, _ => Assert.Fail("no notice"), 100, 0, 0, 1, 4), Is.True);
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].Op, Is.EqualTo(Outbound.KeyTable));
        Assert.That((ushort)sent[0].Op, Is.EqualTo(0x3A));           // SendOp::KeyTable (commonfiles/opcodes.h:363)
        var w = new ByteWriter();
        w.WriteByte(3); w.WriteShort(1); w.WriteInt(100); w.WriteInt(0); w.WriteLong(0); w.WriteInt(4);
        Assert.That(Bytes(sent[0]), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }

    [Test]
    public void APassiveOrUnknownSkillIsRefused()
    {
        var sent = new List<IOutPacket>();
        var notices = new List<int>();
        Assert.That(QuickSlotEdit.Add(World(), _ => false, sent.Add, notices.Add, 300, 0, 0, 0, 0), Is.False);
        Assert.That(notices, Is.EqualTo(new[] { QuickSlotEdit.PassiveNotice }));
        Assert.That(QuickSlotEdit.Add(World(), _ => false, sent.Add, notices.Add, 999, 0, 0, 0, 0), Is.False);   // no row: no notice
        Assert.That(notices, Has.Count.EqualTo(1));
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void FeatureD3RefusesASkillWhoseItemIsALapenshard()
    {
        var sent = new List<IOutPacket>();
        var w = World(new EquipItem { Id = 29000001, Uid = 7 });
        Assert.That(QuickSlotEdit.Add(w, id => id == QuickSlotEdit.LapenshardFeature, sent.Add, null, 100, 29000001, 0, 0, 0), Is.False);
        Assert.That(QuickSlotEdit.Add(w, _ => false, sent.Add, null, 100, 29000001, 0, 0, 0), Is.True);   // feature off: taken
        Assert.That(sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnItemMustBeQuickSlotUsable()
    {
        var sent = new List<IOutPacket>();
        var w = World(new EquipItem { Id = 20000001, Uid = 55 }, new EquipItem { Id = 11000001, Uid = 66 });
        Assert.That(QuickSlotEdit.Add(w, _ => false, sent.Add, null, 0, 20000001, 55, 0, 2), Is.True);
        Assert.That(QuickSlotEdit.Add(w, _ => false, sent.Add, null, 0, 11000001, 66, 0, 3), Is.False);
        Assert.That(QuickSlotEdit.Add(w, _ => false, sent.Add, null, 0, 12345, 0, 0, 3), Is.False);      // no data at all
        // no bag item of the uid: the item id's own data (FUN_14053f870)
        Assert.That(QuickSlotEdit.Add(w, _ => false, sent.Add, null, 0, 20000001, 99, 0, 5), Is.True);
        Assert.That(sent, Has.Count.EqualTo(2));
    }

    [Test]
    public void ASkillSetTakesTheEditWithoutASend()
    {
        var sent = new List<IOutPacket>();
        var w = World();
        w.SkillSetActive = true;
        Assert.That(QuickSlotEdit.Add(w, _ => false, sent.Add, null, 100, 0, 0, 0, 1), Is.True);
        Assert.That(QuickSlotEdit.Remove(w, sent.Add, 100, 0, 0, 0, 1), Is.True);
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public void RemoveSendsMode5UnlessTheSlotIsEmpty()
    {
        var sent = new List<IOutPacket>();
        Assert.That(QuickSlotEdit.Remove(World(), sent.Add, 0, 0, 0, 2, 6), Is.False);
        Assert.That(QuickSlotEdit.Remove(World(), sent.Add, 100, 20000001, 55, 2, 6), Is.True);
        Assert.That(sent, Has.Count.EqualTo(1));
        var w = new ByteWriter();
        w.WriteByte(5); w.WriteShort(2); w.WriteInt(100); w.WriteLong(55);         // no item id, no slot index
        Assert.That(Bytes(sent[0]), Is.EqualTo(w.Buffer.AsSpan(0, w.Length).ToArray()));
    }
    [Test]
    public void ABagItemDroppedOnASlotIsSentWithTheItemsSkill()
    {
        // SkillSlot::vfunc_18 (skillslot.cpp:561-604) from an InvenSlot: FindItemByUid, FUN_14053e460, FUN_141769320's skill
        var sent = new List<IOutPacket>();
        var boxes = new List<int>();
        var w = World(new EquipItem { Id = 20000001, Uid = 55 }, new EquipItem { Id = 20000002, Uid = 56 }, new EquipItem { Id = 11000001, Uid = 66 },
            new EquipItem { Id = 30000010, Uid = 70 }, new EquipItem { Id = 30000011, Uid = 71 }, new EquipItem { Id = 30000011, Uid = 72, Ugc = new UgcLook { Uid = 9 } });
        QuickSlotEntry? Drop(long uid, int slot) => QuickSlotEdit.AddFromInventory(w, _ => false, sent.Add, null, boxes.Add, uid, 1, slot);

        Assert.That(Drop(55, 2), Is.EqualTo(new QuickSlotEntry(0, 20000001, 55)), "a potion without a skill: the item");
        Assert.That(Drop(56, 3), Is.EqualTo(new QuickSlotEntry(100, 20000002, 56)), "a potion with a skill: both");
        Assert.That(Drop(66, 4), Is.Null, "gear is not quick slot usable");
        Assert.That(Drop(99, 4), Is.Null, "no bag item of the uid");
        Assert.That(Drop(70, 5), Is.EqualTo(new QuickSlotEntry(w.SummonRideSkill, 30000010, 70)), "a ride item: SummonRideSkillID");
        Assert.That(Drop(71, 6), Is.Null, "a UGC ride without a UGC id");
        Assert.That(boxes, Is.EqualTo(new[] { QuickSlotEdit.RideUgcNotice }));
        Assert.That(Drop(72, 6), Is.EqualTo(new QuickSlotEntry(w.SummonRideSkill, 30000011, 72)));
        Assert.That(sent, Is.EqualTo(new IOutPacket[]
        {
            new KeyTableSetQuickSlotRequest(1, 0, 20000001, 55, 2), new KeyTableSetQuickSlotRequest(1, 100, 20000002, 56, 3),
            new KeyTableSetQuickSlotRequest(1, 80000001, 30000010, 70, 5), new KeyTableSetQuickSlotRequest(1, 80000001, 30000011, 72, 6),
        }));
    }

    [Test]
    public void ABagLapenshardGoesOnAsItsSkillOnceEquipped()
    {
        var sent = new List<IOutPacket>();
        var w = World(new EquipItem { Id = 29000002, Uid = 80 });
        w.HasPc = true;
        Assert.That(w.ItemData(29000002).IsQuickSlotUsable, Is.True);
        QuickSlotEntry? Drop() => QuickSlotEdit.AddFromInventory(w, _ => false, sent.Add, null, null, 80, 0, 1);
        w.LapenshardEquipMask = _ => 1;
        Assert.That(Drop(), Is.Null, "not equipped (MyPC::FUN_14152f860)");
        w.LapenshardEquipped = id => id == 29000002;
        w.LapenshardEquipMask = _ => 2;
        Assert.That(Drop(), Is.Null, "equip mask not 1");
        w.LapenshardEquipMask = _ => 1;
        Assert.That(Drop(), Is.EqualTo(new QuickSlotEntry(100, 0, 0)));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new KeyTableSetQuickSlotRequest(0, 100, 0, 0, 1) }));
    }
}

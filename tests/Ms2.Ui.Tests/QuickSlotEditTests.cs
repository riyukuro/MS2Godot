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

    static readonly Dictionary<int, ItemData> items = new()
    {
        [20000001] = ItemData.Load(20000001, XElement.Parse("<e><property type=\"2\" slotMax=\"100\"/></e>")),   // consumable
        [11000001] = ItemData.Load(11000001, XElement.Parse("<e><property type=\"1\" slotMax=\"1\"/></e>")),     // gear
        [29000001] = ItemData.Load(29000001, XElement.Parse("<e><property type=\"21\" slotMax=\"1\"/></e>")),    // lapenshard
    };

    static SlotWorld World(params EquipItem[] bag)
    {
        var doc = XDocument.Parse("<ms2>" + Active + Passive + "</ms2>");
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
}

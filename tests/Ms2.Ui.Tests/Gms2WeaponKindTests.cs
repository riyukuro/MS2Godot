using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>GMS2 item files write subtype="0" on every equip, so the held weapon's kind was 0 and the attack key gave the bare-hand
/// action's skill 1 (not learned). Needs MS2_GMS2_DATA.</summary>
public class Gms2WeaponKindTests
{
    [Test]
    public void ThrowingStarGivesTheStarAttackNotTheBareHandSkill()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var packs = new PackSet(d);
        var items = new ItemDataTable(packs);
        var star = new Ms2.Net.Packets.EquipItem { Id = 13400218, Uid = 1, Amount = 1 };
        var table = WeaponActionTable.Load(packs);
        var held = table.HeldAction(star, star, items.Get, _ => true);
        Assert.That(items.Get(13400218).EquipSubtype, Is.EqualTo(34));
        Assert.That(held, Is.Not.Null);
        Assert.That(held.RightHand, Is.EqualTo(34));
        Assert.That(SkillCast.NormalAttackSkillId(held, table, 0), Is.EqualTo(900001).Or.EqualTo(900021), "a row of the star kind (34), not the bare-hand skill 1");
        Assert.That(SkillCast.NormalAttackSkillId(held, table, 0), Is.Not.EqualTo(1));
    }

    [Test]
    public void TheFilesWithSixteenReadByGroup()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var items = new ItemDataTable(new PackSet(d));
        Assert.That(items.Get(15000041).EquipSubtype, Is.EqualTo(50));
        Assert.That(items.Get(11400413).EquipSubtype, Is.EqualTo(14));
        Assert.That(items.Get(12000061).EquipSubtype, Is.EqualTo(20));
        Assert.That(items.Get(11150032).EquipSubtype, Is.EqualTo(0));
    }

    [Test]
    public void TheKindIsTheItemGroupOnlyForAnEquipWithNone()
    {
        var a = new ItemData { Id = 13400629, Type = 1 };
        a.FillEquipSubtype();
        Assert.That(a.EquipSubtype, Is.EqualTo(34));
        var obj = new ItemData { Id = 18100001, Type = 1 };
        obj.FillEquipSubtype();
        Assert.That(obj.EquipSubtype, Is.EqualTo(81));
        var top = new ItemData { Id = 11400629, Type = 1 };
        top.FillEquipSubtype();
        Assert.That(top.EquipSubtype, Is.EqualTo(14));
        var hair = new ItemData { Id = 10200003, Type = 1 };
        hair.FillEquipSubtype();
        Assert.That(hair.EquipSubtype, Is.EqualTo(0));
        var wrong = new ItemData { Id = 13400629, Type = 1, Subtype = 22 };
        wrong.FillEquipSubtype();
        Assert.That(wrong.EquipSubtype, Is.EqualTo(34), "the id group wins over a file value in a weapon group");
        var notEquip = new ItemData { Id = 20000001, Type = 2 };
        notEquip.FillEquipSubtype();
        Assert.That(notEquip.Subtype, Is.EqualTo(0));
    }

    [Test]
    public void FileValue16IsReadByTheIdGroup()
    {
        int Kind(int id, int fileSubtype, int type = 1)
        {
            var d = new ItemData { Id = id, Type = type, Subtype = fileSubtype };
            d.FillEquipSubtype();
            return d.EquipSubtype;
        }
        Assert.That(Kind(15000041, 16), Is.EqualTo(50), "a 16 weapon (150) is the 2H sword kind");
        Assert.That(Kind(13100127, 16), Is.EqualTo(31), "a 16 off-hand (131)");
        Assert.That(Kind(11200001, 16), Is.EqualTo(12), "an earring (112)");
        Assert.That(Kind(12200001, 0), Is.EqualTo(22), "a suit (122)");
        Assert.That(Kind(12000061, 16), Is.EqualTo(20), "a ring (120)");
        Assert.That(Kind(11300038, 13), Is.EqualTo(13));
        Assert.That(Kind(11150032, 16), Is.EqualTo(0), "a 111 skin: KMS has 0, the file's 16 is dropped");
        Assert.That(Kind(10200003, 16), Is.EqualTo(0));
        Assert.That(Kind(19000001, 0), Is.EqualTo(0), "a fishing rod (190) keeps its file value");
        Assert.That(Kind(11400413, 16, 2), Is.EqualTo(0), "a non-equip is not touched");
    }
}

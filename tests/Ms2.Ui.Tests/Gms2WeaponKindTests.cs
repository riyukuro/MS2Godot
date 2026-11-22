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
    public void TheKindIsTheItemGroupOnlyForAnEquipWithNone()
    {
        var a = new ItemData { Id = 13400629, Type = 1 };
        a.FillEquipSubtype();
        Assert.That(a.EquipSubtype, Is.EqualTo(34));
        var obj = new ItemData { Id = 18100001, Type = 1 };
        obj.FillEquipSubtype();
        Assert.That(obj.EquipSubtype, Is.EqualTo(81));
        var suit = new ItemData { Id = 11400629, Type = 1 };
        suit.FillEquipSubtype();
        Assert.That(suit.EquipSubtype, Is.EqualTo(0));
        var hair = new ItemData { Id = 10200003, Type = 1 };
        hair.FillEquipSubtype();
        Assert.That(hair.EquipSubtype, Is.EqualTo(0));
        var kept = new ItemData { Id = 13400629, Type = 1, Subtype = 22 };
        kept.FillEquipSubtype();
        Assert.That(kept.EquipSubtype, Is.EqualTo(22));
        var notEquip = new ItemData { Id = 20000001, Type = 2 };
        notEquip.FillEquipSubtype();
        Assert.That(notEquip.Subtype, Is.EqualTo(0));
    }
}

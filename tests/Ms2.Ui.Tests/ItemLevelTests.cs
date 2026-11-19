using System.Xml.Linq;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CItem::UpdateItemLevel (item.cpp:1544) and PC::vfunc_126's worn sum (pc.cpp:4172), on made item data.</summary>
public class ItemLevelTests
{
    static ItemData Data(int type, int subtype, int gearScore, int rank = 0) =>
        ItemData.Load(1, XElement.Parse($"<env><property type=\"{type}\" subtype=\"{subtype}\" gearScore=\"{gearScore}\" rank=\"{rank}\"/></env>"));

    static Item Make(ItemData d, int rarity, int enchant = 0, int plus = 0)
    {
        var it = Item.Create(d, rarity);
        it.EnchantLevel = enchant;
        it.UnlimitedEnchantLevel = plus;
        return it;
    }

    [Test]
    public void BaseFollowsTheGearScoreBands()
    {
        var weapon = Data(1, 0x1f, 20);
        Assert.That(weapon.GearScore, Is.EqualTo(20));
        Assert.That(ItemLevels.Base(weapon, 1), Is.EqualTo(200), "20 * 10 + 0 * 5, factor 1");
        Assert.That(ItemLevels.Base(weapon, 3), Is.EqualTo(210));
        Assert.That(ItemLevels.Base(Data(1, 0xd, 20), 3), Is.EqualTo(44), "210 * 0.21 = 44.1");
        // grade 4 from gear 50: (gear * 10 + 15) doubled per grade over 3, plus (gear - 50) * 100
        Assert.That(ItemLevels.Base(Data(1, 0x1f, 55), 4), Is.EqualTo((550 + 15) * 2 + 500));
        Assert.That(ItemLevels.Base(Data(1, 0x1f, 45), 4), Is.EqualTo(465), "under gear 50: no doubling");
        Assert.That(ItemLevels.Base(Data(1, 0x1f, 0x50), 5), Is.EqualTo((int)((2.0 + 19.537) * 1030.0 * 1.2)));
        Assert.That(ItemLevels.Base(Data(1, 0x1f, 0x50), 3), Is.Zero, "gear 70 up: no rate under grade 4");
        Assert.That(ItemLevels.Base(Data(1, 0x1f, 0), 6), Is.Zero);
        Assert.That(ItemLevels.Base(Data(1, 0x1f, 20, rank: 3), 1), Is.EqualTo(210), "the data's rank wins over the grade");
    }

    [Test]
    public void ItemLevelAddsTheEnchant()
    {
        Assert.That(ItemLevels.Of(Make(Data(3, 0, 20), 3)), Is.EqualTo(((int, int)?)(0, 0)), "not an equipment item");
        Assert.That(ItemLevels.Of(Make(Data(1, 0x1f, 20), 3, enchant: 5)), Is.EqualTo(((int, int)?)(210, 0)),
            "gear under 70 with no fixed rank 4: no enchant level");
        Assert.That(ItemLevels.Of(Make(Data(1, 0x1f, 20, rank: 4), 1, enchant: 5)), Is.EqualTo(((int, int)?)(215 + 21, 21)), "215 * 0.1");
        int b = ItemLevels.Base(Data(1, 0x1f, 0x50), 4);
        Assert.That(ItemLevels.Of(Make(Data(1, 0x1f, 0x50), 4, enchant: 15)), Is.EqualTo(((int, int)?)(b + b * 2, b * 2)));
        Assert.That(ItemLevels.Of(Make(Data(1, 0x1f, 0x50), 4, enchant: 16)), Is.EqualTo(((int, int)?)(b, 0)), "past 15: no rate");
        int plus2 = ItemLevels.UnlimitedEnchantLevel(2, 0, 0x1f);
        Assert.That(ItemLevels.Of(Make(Data(1, 0x1f, 0x50), 4, plus: 2)), Is.EqualTo(((int, int)?)(b + plus2, plus2)), "the Lua chunk's plus level");
        Assert.That(ItemLevels.Of(Make(Data(1, 0x1f, 0x50), 4, plus: 2), (plus, max, sub) => plus * 10 + sub),
            Is.EqualTo(((int, int)?)(b + 20 + 0x1f, 20 + 0x1f)));
    }

    [Test]
    public void WornSumTakesTheMainHandAndDividesItForOneOneHandedWeapon()
    {
        var armor = Make(Data(1, 0x3, 20), 1);                       // 200
        var twoHanded = Make(Data(1, 0x32, 20), 2);                  // 205, not subtype 0x1f / 0x22
        var oneHanded = Make(Data(1, 0x1f, 20), 3);                  // 210
        var offHand = Make(Data(1, 0x22, 20), 1);                    // 200
        bool All(Item _) => true;
        Assert.That(ItemLevels.Worn(new Dictionary<int, Item> { [8] = armor, [5] = twoHanded }, 4f, All), Is.EqualTo(((uint, uint)?)(405u, 0u)));
        Assert.That(ItemLevels.Worn(new Dictionary<int, Item> { [8] = armor, [4] = twoHanded }, 4f, All), Is.EqualTo(((uint, uint)?)(200u, 0u)),
            "slot 4 alone is not added");
        Assert.That(ItemLevels.Worn(new Dictionary<int, Item> { [8] = armor, [5] = oneHanded }, 4f, All), Is.EqualTo(((uint, uint)?)(252u, 0u)),
            "200 + 210 / 4 in float, truncated");
        Assert.That(ItemLevels.Worn(new Dictionary<int, Item> { [4] = offHand, [5] = oneHanded }, 4f, All), Is.EqualTo(((uint, uint)?)(205u, 0u)),
            "two: the Lua part, a gap of 10 / 210: the means");
        Assert.That(ItemLevels.Worn(new Dictionary<int, Item> { [4] = offHand, [5] = oneHanded }, 4f, All,
            oneHanded: (lo, hi, loMax, hiMax) => ((int)(lo + hi), (int)(loMax + hiMax))), Is.EqualTo(((uint, uint)?)(410u, 0u)));
        Assert.That(ItemLevels.Worn(new Dictionary<int, Item> { [8] = armor, [5] = twoHanded }, 4f, it => it != armor),
            Is.EqualTo(((uint, uint)?)(205u, 0u)), "an item the player cannot use adds nothing");
        Assert.That(ItemLevels.Worn(new Dictionary<int, Item>(), 4f, All), Is.EqualTo(((uint, uint)?)(0u, 0u)));
    }

    [Test]
    public void OneHandedPairFollowsTheScriptBranches()
    {
        Assert.That(ItemLevels.OneHandedAdditionalLevel(0, 300, 0, 41), Is.EqualTo((150, 20)), "no low level: half the high");
        Assert.That(ItemLevels.OneHandedAdditionalLevel(0, 0, 5, 7), Is.EqualTo((0, 7)), "nothing: the high values");
        Assert.That(ItemLevels.OneHandedAdditionalLevel(100, 120, 10, 30), Is.EqualTo((110, 20)), "gap 1/6: the means");
        // gap 0.5: 100 + 100 * 0.5 * 0.5 and 100 * 0.5 * 0.5, the additional from the totals
        Assert.That(ItemLevels.OneHandedAdditionalLevel(100, 200, 10, 30), Is.EqualTo((125, 25)));
        Assert.That(ItemLevels.OneHandedAdditionalLevel(200, 200, 9, 4), Is.EqualTo((200, 4)), "equal: the high values");
    }

    [Test]
    public void ExpiredItemsCountOnlyAsPetsOrMaids()
    {
        var it = Make(Data(1, 0x3, 20), 1);
        Assert.That(ItemLevels.Usable(it, 100), Is.True, "no expiry");
        it.ExpiryTime = 100;
        Assert.That(ItemLevels.Usable(it, 99), Is.True);
        Assert.That(ItemLevels.Usable(it, 100), Is.False);
        var pet = Make(Data(0xb, 0, 0), 1);
        pet.ExpiryTime = 1;
        Assert.That(ItemLevels.Usable(pet, 100), Is.True);
    }
}

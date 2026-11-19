using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The mastery rules that live outside the dialog (MasteryEvents) and the bag count the precheck uses (InventoryCount):
/// OnMastery's modes, the mode drops of a SkillDamage record on the own player, and FUN_140a5f180.
/// </summary>
public class MasteryEventsTests
{
    static SkillDamage Damage(byte mode, int owner, int region, int target, params long[] hits) => new()
    {
        Mode = mode,
        OwnerId = owner,
        RegionId = region,
        Targets = { new HitTarget(target, hits.Select(h => new Hit(1, h)).ToArray(), default, default) },
    };

    [Test]
    public void ModeThreeNeedsItsPcAndModeFiveItsRegion()
    {
        var splash = Damage(6, 77, 0, 5, -2);
        Assert.That(MasteryEvents.SelfRecords(splash, 5, _ => true, _ => false, _ => true), Is.Empty, "mode 6: the owner object is not in the field");
        Assert.That(MasteryEvents.SelfRecords(splash, 5, _ => true, _ => true, _ => true), Is.EqualTo(new long[] { -2 }));

        var d3 = Damage(3, 77, 0, 5, -10);
        Assert.That(MasteryEvents.SelfRecords(d3, 5, _ => false, _ => true, _ => true), Is.Empty, "mode 3: the owner PC is not in the field");
        Assert.That(MasteryEvents.SelfRecords(d3, 5, id => id == 77, _ => true, _ => false), Is.EqualTo(new long[] { -10 }));

        var d5 = Damage(5, 77, 900, 5, -20);
        Assert.That(MasteryEvents.SelfRecords(d5, 5, _ => true, _ => true, _ => false), Is.Empty, "mode 5: the region skill object is gone");
        Assert.That(MasteryEvents.SelfRecords(d5, 5, _ => false, _ => true, id => id == 900), Is.EqualTo(new long[] { -20 }));

        var d1 = Damage(1, 0, 0, 5, -3, -4);
        Assert.That(MasteryEvents.SelfRecords(d1, 5, _ => false, _ => false, _ => false), Is.EqualTo(new long[] { -7 }), "mode 1 has no owner test");
        Assert.That(MasteryEvents.SelfRecords(d1, 6, _ => true, _ => true, _ => true), Is.Empty, "another target");
        Assert.That(MasteryEvents.SelfRecords(Damage(0, 0, 0, 5, -1), 5, _ => true, _ => true, _ => true), Is.Empty, "mode 0 is a cast");
    }

    [Test]
    public void AHealDoesNotCancelWhereAHitDoes()
    {
        Assert.That(MasteryEvents.CancelsCraft(-1), Is.True);
        Assert.That(MasteryEvents.CancelsCraft(0), Is.False);
        Assert.That(MasteryEvents.CancelsCraft(30), Is.False);
    }

    [Test]
    public void ModeZeroSkipsTheOtherSystemsAndRedrawsTabsOnlyOnAGradeUp()
    {
        foreach (int type in new[] { 1, 2, 0xb })
            Assert.That(MasteryEvents.OnExpChanged(type, true), Is.EqualTo(default(MasteryEvents.Update)), $"type {type}");

        var same = MasteryEvents.OnExpChanged(7, false);
        Assert.That((same.Dialog, same.Tabs, same.GameMenu), Is.EqualTo((true, false, false)));

        var up = MasteryEvents.OnExpChanged(7, true);
        Assert.That((up.Dialog, up.Tabs, up.GameMenu), Is.EqualTo((true, true, true)), "a grade up redoes the tabs and the menu count");
    }

    [Test]
    public void ModeOneAddsTheLevelGuideAndRedrawsAlways()
    {
        var u = MasteryEvents.OnRewardReceived(7003);
        Assert.That(u.LevelGuide, Is.True);
        Assert.That((u.Type, u.Grade), Is.EqualTo((7, 3)));
        Assert.That((u.Dialog, u.Tabs, u.GameMenu), Is.EqualTo((true, true, true)));
        Assert.That(MasteryEvents.OnCrafted(7).Tabs, Is.False, "mode 2 only sets the pending sub");
    }

    // ---- FUN_140a5f180 -------------------------------------------------------------------------------------------

    static EquipItem Item(int id, int rarity = 1, int amount = 2, long expires = 0) => new()
    {
        Id = id, Rarity = rarity, Amount = amount, ExpiryTime = expires,
    };

    static ItemDataTable Table()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new GameTables(new Ms2.Formats.Pack.PackSet(d), "en").Items;
    }

    const int Potion = 20000004, PetItem = 60000001;      // a consumable and a pet (itemdata/002 and /600)

    [Test]
    public void TheCountSkipsItemsTheCharacterMayNotUse()
    {
        var table = Table();
        int id = Potion;
        Assert.That(table.Get(id), Is.Not.Null, "the consumable's data");

        var items = new[]
        {
            Item(id),
            Item(id, amount: 3),
            Item(id, rarity: 2, amount: 5),
            new EquipItem { Id = id, Rarity = 1, Amount = 9, Transfer = { HasBinding = true, BindingId = 999 } },
            new EquipItem { Id = id, Rarity = 1, Amount = 9, ExpiryTime = 50 },
        };
        Assert.That(InventoryCount.ById(items, table, id, 0, 100, 1000), Is.EqualTo(10), "every rarity; the other character's binding and the expired one do not count");
        Assert.That(InventoryCount.ById(items, table, id, 2, 100, 1000), Is.EqualTo(5), "rarity 2 only");
        Assert.That(InventoryCount.ById(items, table, id, 3, 100, 1000), Is.Zero);
    }

    [Test]
    public void APetIsUsableAfterItsExpiryWhereAnItemIsNot()
    {
        var table = Table();
        Assert.That(table.Get(PetItem)?.IsPetItem, Is.True, "the pet item's data");
        var items = new[] { new EquipItem { Id = PetItem, Rarity = 1, Amount = 1, ExpiryTime = 50 } };
        Assert.That(InventoryCount.ById(items, table, PetItem, 0, 1, 1000), Is.EqualTo(1), "a pet is exempt from IsExpired");
    }
}

using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CItemLapenshardTable's upgrade rows (itemlapensharddata.cpp:188 Load, GetIngredients @0x1405b41b0).</summary>
public class LapenshardUpgradeTableTests
{
    static LapenshardTable Table()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new GameTables(new PackSet(d), "en").Lapenshards;
    }

    [Test]
    public void RowsLinkByGroupAndLevel()
    {
        var t = Table();
        int checkedRows = 0;
        foreach (var id in t.ItemIds())
        {
            var r = t.Get(id);
            Assert.That(r, Is.Not.Null);
            Assert.That(r.Group, Is.EqualTo(t.Group(id)));
            Assert.That(t.Level(id), Is.EqualTo(r.Level));
            Assert.That(t.HasUpgrade(id), Is.EqualTo(r.Next != 0));
            Assert.That(r.Ingredients.Count, Is.LessThanOrEqualTo(4));
            if (r.Level == 1) Assert.That(t.FirstItem(r.Group), Is.GreaterThan(0));
            Assert.That(t.Item(r.Group, r.Level), Is.GreaterThan(0));
            checkedRows++;
        }
        Assert.That(checkedRows, Is.GreaterThan(0));
        Assert.That(t.Get(-1), Is.Null);
        Assert.That((t.Level(-1), t.HasUpgrade(-1), t.FirstItem(-1), t.Item(-1, 1)), Is.EqualTo((0, false, 0, 0)));
    }

    [Test]
    public void ScaleChangesOnlyMoneyTaggedIngredients()
    {
        var row = new LapenshardTable.Row(1, 1, 2, 7, 3, 0, new[]
        {
            new MasteryRecipeTable.Ingredient(10, 100, 0x5d),
            new MasteryRecipeTable.Ingredient(11, 5, 0),
            new MasteryRecipeTable.Ingredient(12, 1, 0x5f),
        });
        Assert.That(LapenshardTable.Scaled(row, 0), Is.SameAs(row.Ingredients));
        var s = LapenshardTable.Scaled(row, 2500);
        Assert.That(s.Select(i => i.Count), Is.EqualTo(new[] { 75, 5, 1 }));
        Assert.That(s.Select(i => (i.ItemId, i.Tag)), Is.EqualTo(row.Ingredients.Select(i => (i.ItemId, i.Tag))));
        Assert.That(LapenshardTable.Scaled(row, 10000)[0].Count, Is.EqualTo(1), "never below 1");
    }
}

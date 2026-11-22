using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item tooltip's enchant text and dye swatch (ItemTooltip.Extras.cs) on real tables (needs MS2_DATA).</summary>
[NonParallelizable]   // the tooltip draw sets process-wide hooks
public class ItemTooltipExtrasTests
{
    static PackSet packs;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
        }
        return packs;
    }

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };

    static ItemTooltip Tip(UiModel m, EquipItem r)
    {
        var tip = new ItemTooltip(m, 0x200);
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items));
        return tip;
    }

    const int IntroMagicianCoat = 12200436;

    [Test]
    public void EnchantableCoatShowsCanBeEnchanted()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        var r = new EquipItem { Id = IntroMagicianCoat, Uid = 1, Rarity = 1, Amount = 1 };
        var tip = Tip(m, r);
        Assert.That(tip.Data["itemDamage"], Is.EqualTo(m.Tables.Common(0x959)));
        Assert.That(m.Tables.Common(0x959), Does.Contain("Can Be Enchanted"));
        var shown = TooltipTests.Draw(tip.Data);
        TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Can Be Enchanted"));

        // a fail stack adds s_itemenchant_fail_stack_count below it
        r.Enchant.FailStack = 3;
        var text = (string)Tip(m, r).Data["itemDamage"];
        Assert.That(text, Does.StartWith(m.Tables.Common(0x959) + "\n"));
        Assert.That(text, Does.Contain("3"));
    }

    [Test]
    public void ExpiringItemCannotBeEnchanted()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        var item = Ms2.Ui.Hud.InventoryController.ToItem(new EquipItem { Id = IntroMagicianCoat, Uid = 2, Rarity = 1, Amount = 1 }, m.Tables.Items);
        item.ExpiryTime = 1;   // CItem::FUN_140525c60
        var tip = new ItemTooltip(m, 0x200);
        tip.Fill(item);
        Assert.That(tip.Data["itemDamage"], Is.EqualTo(m.Tables.Common(0x94e)));
        Assert.That(m.Tables.Common(0x94e), Is.Not.Empty);
    }

    [Test]
    public void DyedCoatHasNoSwatchWithoutTheColoursStringKey()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        var gm = m.Tables.ItemModels.Get(m.Tables.Items.Get(IntroMagicianCoat).ItemPreset);
        Assert.That(gm.Palette, Is.EqualTo(10));
        var c = m.Tables.ColorPalettes.BySN(10, 7);
        Assert.That(c, Is.Not.Null);
        Assert.That((c.Ch0, c.Ch2, c.Visible, c.ColorSN, c.PaletteId), Is.EqualTo((0xffef7091u, 0xff994252u, 0xffe4768bu, 7, 10)));
        // the KMS colorpalette.xml has no stringKey: FUN_1409d3010 returns before the member
        Assert.That(c.StringKey, Is.EqualTo(0));
        m.FeatureEnabledByName = Enable0xb7(m);
        var r = new EquipItem { Id = IntroMagicianCoat, Uid = 4, Rarity = 1, Amount = 1 };
        r.Color = new EquipColor(default, default, default, 7, 10);
        Assert.That(Tip(m, r).Data.ContainsKey("customizeColorData"), Is.False);
    }

    static Func<string, bool> Enable0xb7(UiModel m)
    {
        var baseline = m.FeatureEnabledByName;
        string name = FeatureTable.Names[0xb7 - 1];
        return f => f == name || baseline(f);
    }

    /// <summary>GMS2's table/na/colorpalette_achieve.xml row for palette 10 colour 7.</summary>
    const string NaPalette = "<ms2><colorPalette id=\"10\"><color colorSN=\"7\" ch0=\"0xffef7091\" ch1=\"0xff994252\" ch2=\"0xff994252\" palette=\"0xffe4768b\" stringKey=\"10000008\" /></colorPalette></ms2>";

    [Test]
    public void DyedCoatShowsItsColourWhereThePaletteNamesIt()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        m.Tables.ColorPalettes = ColorPaletteTable.Parse(System.Xml.Linq.XElement.Parse(NaPalette));
        var r = new EquipItem { Id = IntroMagicianCoat, Uid = 5, Rarity = 1, Amount = 1 };
        r.Color = new EquipColor(default, default, default, 7, 10);
        try
        {
            // feature 0xb7 off: no member
            if (!m.IsFeatureEnabled(0xb7)) Assert.That(Tip(m, r).Data.ContainsKey("customizeColorData"), Is.False);
            m.FeatureEnabledByName = Enable0xb7(m);
            var tip = Tip(m, r);
            var obj = (Dictionary<string, object>)tip.Data["customizeColorData"];
            Assert.That(obj["desc"], Is.EqualTo("Pink"));
            var color = (Dictionary<string, object>)obj["color"];
            Assert.That(color["realColor"], Is.EqualTo(0xffef7091u));
            Assert.That(color["visibleColor"], Is.EqualTo(0xffe4768bu));
            Assert.That(color["ch2Color"], Is.EqualTo(0xff994252u));
            Assert.That(color.ContainsKey("realColor2"), Is.False);
            Assert.That((color["colorSN"], color["paletteID"]), Is.EqualTo((7, 10u)));
            var shown = TooltipTests.Draw(tip.Data);
            TestContext.WriteLine("drawn: " + string.Join(" | ", shown));
            Assert.That(shown, Has.Some.Contains("Pink"));

            // no dye on the item (palette 0), or a colour SN the palette lacks: no member
            r.Color = new EquipColor(default, default, default, 7, 0);
            Assert.That(Tip(m, r).Data.ContainsKey("customizeColorData"), Is.False);
            r.Color = new EquipColor(default, default, default, 8, 10);
            Assert.That(Tip(m, r).Data.ContainsKey("customizeColorData"), Is.False);
        }
        finally { m.Tables.ColorPalettes = null; }
    }
}

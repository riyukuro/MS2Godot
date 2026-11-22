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
    public void ItemMovieIsTheDataMovieEvenWhenEmpty()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        // itemdata/113.xml: 11300002 shows the 11300048 movie; the coat has none
        Assert.That(Tip(m, new EquipItem { Id = 11300002, Uid = 3, Rarity = 1, Amount = 1 }).Data["itemMovie"], Is.EqualTo("Item/11300048.usm"));
        Assert.That(Tip(m, new EquipItem { Id = IntroMagicianCoat, Uid = 4, Rarity = 1, Amount = 1 }).Data["itemMovie"], Is.EqualTo(""));
        // flag 0x40 leaves it out
        var tip = new ItemTooltip(m, 0x40);
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(new EquipItem { Id = 11300002, Uid = 5, Rarity = 1, Amount = 1 }, m.Tables.Items));
        Assert.That(tip.Data.ContainsKey("itemMovie"), Is.False);
    }

    [Test]
    public void ItemStatusSetListsTheSetPiecesAndParts()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        // setiteminfo.xml: set 15123101 lists 11300451 and four more pieces; parts at 2 (str), 4 (pap) and 5 (pen)
        var arr = (List<object>)Tip(m, new EquipItem { Id = 11300451, Uid = 6, Rarity = 1, Amount = 1 }).Data["itemStatusSet"];
        Assert.That(arr, Has.Count.EqualTo(1));
        var text = (string)arr[0];
        Assert.That(text, Does.Contain(m.Tables.SetItemName(15123101)));
        foreach (int id in new[] { 11300451, 11400397, 11500317, 11600392, 11700419 })
            Assert.That(text, Does.Contain(m.Tables.ItemName(id)), id.ToString());
        Assert.That(text, Does.Not.Contain("ffffff"), "nothing worn");
        // an item no set lists still sets the member, empty
        var none = (List<object>)Tip(m, new EquipItem { Id = IntroMagicianCoat, Uid = 7, Rarity = 1, Amount = 1 }).Data.GetValueOrDefault("itemStatusSet");
        Assert.That(none ?? new List<object>(), Is.Empty);
    }

    [Test]
    public void ItemSocketListsGemsEmptyAndLockedSlots()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        // three slots, two open: a gem (itemgemstoneupgrade.xml IconId export_socket_b07) and an empty one
        var r = new EquipItem { Id = IntroMagicianCoat, Uid = 8, Rarity = 1, Amount = 1 };
        r.Socket.MaxSlot = 3;
        r.Socket.Gems.Add(new SocketGem { GemId = 40200001 });
        r.Socket.Gems.Add(null);
        string text = (string)Tip(m, r).Data["itemSocket"];
        string lockRow = m.Tables.Common("s_tooltip_itemoption_socket_row_lock");
        string emptyRow = m.Tables.Common("s_tooltip_itemoption_socket_row_empty");
        TestContext.Out.WriteLine(text);
        Assert.That(text, Does.Contain("export_socket_b07"));
        Assert.That(text, Does.Contain(emptyRow + lockRow), "the empty open slot, then the locked one");
        // no max slot: no member
        Assert.That(Tip(m, new EquipItem { Id = IntroMagicianCoat, Uid = 9, Rarity = 1, Amount = 1 }).Data.ContainsKey("itemSocket"), Is.False);
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

    [Test]
    public void TransformTableKeepsEachItemsLowestEnchantLevel()
    {
        var doc = System.Xml.Linq.XDocument.Parse("""
            <ms2>
              <transform inputRank="4" inputEnchantLevel="12" inputSlot="1,2" inputItemId="100,200" output1Id="9" output1Rank="1" output1Count="1"/>
              <transform inputRank="4" inputEnchantLevel="10" inputSlot="1" inputItemId="200" output1Id="9" output1Rank="1" output1Count="1"/>
              <transform inputRank="4" inputEnchantLevel="5" inputSlot="1" inputItemId="300" output1Id="0" output1Rank="1" output1Count="1"/>
              <transform inputRank="4" inputEnchantLevel="7" inputSlot="1" inputItemId="400" output1Id="9" output1Rank="1" output1Count="1"/>
            </ms2>
            """);
        var t = new ItemEnchantTransformTable(doc, _ => true);
        Assert.That(t.LowestLimitLevel(100), Is.EqualTo(12));
        Assert.That(t.LowestLimitLevel(200), Is.EqualTo(10));
        Assert.That(t.LowestLimitLevel(300), Is.EqualTo(5));   // set before the bad output stops the load
        Assert.That(t.LowestLimitLevel(400), Is.EqualTo(0));   // after it
        Assert.That(t.LowestLimitLevel(1), Is.EqualTo(0));
    }

    [Test]
    public void TransformLineFollowsCanBeEnchanted()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        TestContext.WriteLine($"feature 0xa6: {m.IsFeatureEnabled(0xa6)}");
        if (!m.IsFeatureEnabled(0xa6)) Assert.Ignore("feature 0xa6 is off in this data");
        int id = 0; uint level = 0;
        foreach (int i in m.Tables.EnchantTransform.ItemIds.OrderBy(i => i))
            if (m.Tables.Items.Get(i) is { IsEquipItem: true, HasSkinType: false } && (level = m.Tables.EnchantTransform.LowestLimitLevel(i)) != 0) { id = i; break; }
        if (id == 0) Assert.Ignore("no item has transform rows");
        var text = (string)Tip(m, new EquipItem { Id = id, Uid = 3, Rarity = 4, Amount = 1 }).Data["itemDamage"];
        TestContext.WriteLine($"{id} level {level}: {text}");
        Assert.That(text, Does.Contain(level.ToString()));
        Assert.That(text, Does.Contain("\n"));
    }

    [Test]
    public void ExtractionLineCountsTheTriesLeft()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        int id = m.Tables.Extraction.ItemIds.OrderBy(i => i).FirstOrDefault(i => m.Tables.Items.Get(i) != null);
        if (id == 0) Assert.Ignore("no itemExtraction row with item data");
        uint scrolls = m.Tables.Extraction.ScrollCount(id).Value;
        // a made item's count is 1 (the table has its row)
        var made = new ItemTooltip(m, 0x200);
        made.Fill(Item.Create(m.Tables.Items, id, 1));
        var text = (string)made.Data["itemDamage"];
        TestContext.WriteLine($"{id} scrolls {scrolls}: {text}");
        Assert.That(text, Does.EndWith(Ms2.Quest.ClientFormat.Format(m.Tables.Common("s_itemextraction_trycount"), 1u, scrolls)));
        var used = (string)Tip(m, new EquipItem { Id = id, Uid = 4, Rarity = 1, Amount = 1, GlamorForges = 0 }).Data["itemDamage"];
        TestContext.WriteLine("used up: " + used);
        Assert.That(used, Does.EndWith(m.Tables.Common("s_itemextraction_invalid")));
        Assert.That(m.Tables.Common("s_itemextraction_invalid"), Is.Not.Empty);
    }

    /// <summary>The first equipment id of the item data whose environment sets a repack limit, with transfer type 3 or 5.</summary>
    static int FirstRepackItem(UiModel m)
    {
        var rx = new System.Text.RegularExpressions.Regex("<item id=\"(\\d+)\"");
        for (int f = 0; f < 1000; f++)
        {
            string entry = $"itemdata/{f:000}.xml";
            if (!Packs().Exists("Xml", entry)) continue;
            string text = System.Text.Encoding.UTF8.GetString(Packs().Read("Xml", entry));
            if (!text.Contains("rePackingLimitCount=\"")) continue;
            foreach (System.Text.RegularExpressions.Match x in rx.Matches(text))
                if (m.Tables.Items.Get(int.Parse(x.Groups[1].Value)) is { RePackingLimitCount: > 0, TransferType: 3 or 5, IsPetItem: false } d)
                    return d.Id;
        }
        return 0;
    }

    /// <summary>The first item id of the item data (file order) the predicate passes.</summary>
    static int FirstItem(UiModel m, Func<ItemData, bool> pass)
    {
        var rx = new System.Text.RegularExpressions.Regex("<item id=\"(\\d+)\"");
        for (int f = 0; f < 1000; f++)
        {
            string entry = $"itemdata/{f:000}.xml";
            if (!Packs().Exists("Xml", entry)) continue;
            string text = System.Text.Encoding.UTF8.GetString(Packs().Read("Xml", entry));
            foreach (System.Text.RegularExpressions.Match x in rx.Matches(text))
                if (m.Tables.Items.Get(int.Parse(x.Groups[1].Value)) is { } d && pass(d))
                    return d.Id;
        }
        return 0;
    }

    [Test]
    public void RemakeLineNeedsOptionsInTheSlotGroup()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        int id = FirstItem(m, d => !d.IsPetItem && !d.RemakeDisable && d.LevelLimit >= 0x32 && d.Rank is 0 or >= 4
            && d.EquipSubtype is 0xc or (>= 0xd and <= 0x16));
        if (id == 0) Assert.Ignore("no remakeable equipment");
        var r = new EquipItem { Id = id, Uid = 6, Rarity = 4, Amount = 1, RemakeCount = 3 };
        string before = Tip(m, r).Data.TryGetValue("itemDamage", out var t0) ? (string)t0 : "";
        Assert.That(before, Does.Not.Contain(Ms2.Quest.ClientFormat.Format(m.Tables.Common(0xbf9), 3)));
        r.Options.Stat[2][1] = 10;
        var text = (string)Tip(m, r).Data["itemDamage"];
        TestContext.WriteLine($"{id}: {text}");
        Assert.That(text, Does.Contain(Ms2.Quest.ClientFormat.Format(m.Tables.Common(0xbf9), 3)));
        var flagged = new ItemTooltip(m, 0x400);
        flagged.Fill(Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items));
        Assert.That((string)flagged.Data["itemDamage"], Does.Contain(m.Tables.Common(0xbfa)));
        Assert.That(m.Tables.Common(0xbf9), Is.Not.Empty);
    }

    [Test]
    public void DyeLineWithFlag4AddsTheUnwornLine()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        int id = FirstItem(m, d => d.ItemPreset != 0 && m.Tables.ItemModels.Get(d.ItemPreset) is { ColorEnabled: true, Palette: not 0, MultiColor: false } gm
            && gm.DefaultColor(0) < 0);
        if (id == 0) Assert.Ignore("no customizable item without a default colour");
        var r = new EquipItem { Id = id, Uid = 11, Rarity = 1, Amount = 1 };
        var item = Ms2.Ui.Hud.InventoryController.ToItem(r, m.Tables.Items);
        string dye = m.Tables.Common(0x9b7);
        // without the equipment lookup the member is left out
        var none = new ItemTooltip(m, 4);
        Assert.That(none.SetCustomizeColor(item), Is.False);
        var unworn = new ItemTooltip(m, 4) { OwnerWearsUid = _ => false };
        Assert.That(unworn.SetCustomizeColor(item), Is.True);
        TestContext.WriteLine($"{id}: {unworn.Data["customizeColor"]}");
        Assert.That(unworn.Data["customizeColor"], Is.EqualTo(m.Tables.Common(0x9b8) + "\n" + dye));
        var worn = new ItemTooltip(m, 4) { OwnerWearsUid = uid => uid == 11 };
        Assert.That(worn.SetCustomizeColor(item), Is.True);
        Assert.That(worn.Data["customizeColor"], Is.EqualTo(dye));
        Assert.That(m.Tables.Common(0x9b8), Is.Not.Empty);
    }

    [Test]
    public void RepackLineCountsWhatIsLeft()
    {
        var m = NewModel();
        m.HasLocalPlayer = true;
        TestContext.WriteLine($"feature 0x38: {m.IsFeatureEnabled(0x38)}");
        if (!m.IsFeatureEnabled(0x38)) Assert.Ignore("feature 0x38 is off in this data");
        int id = FirstRepackItem(m);
        if (id == 0) Assert.Ignore("no item with a repack limit");
        var d = m.Tables.Items.Get(id);
        var r = new EquipItem { Id = id, Uid = 5, Rarity = 4, Amount = 1 };
        var text = (string)Tip(m, r).Data["itemDamage"];
        TestContext.WriteLine($"{id} limit {d.RePackingLimitCount} consume {d.RePackingItemConsumeCount}: {text}");
        Assert.That(text, Does.StartWith(Ms2.Quest.ClientFormat.Format(m.Tables.Common("s_item_repacking_limit_count"),
            d.RePackingLimitCount, d.RePackingItemConsumeCount)));
        r.Transfer.Max = d.RePackingLimitCount;
        text = (string)Tip(m, r).Data["itemDamage"];
        TestContext.WriteLine("used up: " + text);
        Assert.That(text, Does.StartWith(m.Tables.Common("s_item_repacking_disable")));
        r.Transfer.HasBinding = true;
        text = Tip(m, r).Data.TryGetValue("itemDamage", out var v) ? (string)v : "";
        Assert.That(text, Does.Not.Contain(m.Tables.Common("s_item_repacking_disable")));
    }
}

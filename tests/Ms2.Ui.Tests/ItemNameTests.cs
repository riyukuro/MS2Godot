using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item name CItem::FUN_140527b10 makes (<see cref="ItemTooltip.Name"/>), over every item of the item data
/// (needs MS2_DATA).</summary>
[NonParallelizable]
public class ItemNameTests
{
    static PackSet packs;
    static UiModel model;
    static List<int> ids;

    static UiModel Model()
    {
        if (model == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            model = new UiModel(new GameTables(packs, "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f) };
        }
        return model;
    }

    /// <summary>Every item id of the itemdata/NNN.xml files that has data (an available environment).</summary>
    static List<int> Ids()
    {
        if (ids != null) return ids;
        var m = Model();
        ids = new();
        var file = new Regex(@"^itemdata/\d+\.xml$", RegexOptions.IgnoreCase);
        foreach (var entry in packs.List("Xml").Where(e => file.IsMatch(e.Replace('\\', '/'))))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", entry)).Root.Elements("item"))
                if (int.TryParse((string)e.Attribute("id"), out int id) && m.Tables.Items.Get(id) != null) ids.Add(id);
        if (ids.Count == 0)     // the GMS2 data: one file per item (item/<a>/<bb>/<id>.xml), as the item table indexes them
            ids.AddRange(ItemDataTable.PerItemIndex(packs.List("Xml")).Keys.Where(id => m.Tables.Items.Get(id) != null));
        ids = ids.Distinct().OrderBy(i => i).ToList();
        return ids;
    }

    static string Name(Item item) => new ItemTooltip(Model(), 0).Name(item);

    static Item Made(int id) => Item.Create(Model().Tables.Items, id, 1);

    /// <summary>
    /// Every item, as a made item (no record): the name is never null, and an empty one only comes from an empty or
    /// missing itemname.xml entry, which the client prints empty too (FUN_141811980(1, id) gives "" and nothing is added
    /// to it); those ids are listed.
    /// </summary>
    [Test]
    public void EveryItemHasTheClientsName()
    {
        var t = Model().Tables;
        var all = Ids();
        Assert.That(all, Is.Not.Empty);
        var empty = new List<int>();
        foreach (int id in all)
        {
            string name = Name(Made(id));
            Assert.That(name, Is.Not.Null, $"item {id}");
            if (name.Length == 0) empty.Add(id);
        }
        var noEntry = empty.Where(id => t.ItemName(id).Length == 0).ToList();
        TestContext.Out.WriteLine($"{all.Count} items, {empty.Count} empty names, all with an empty or missing itemname entry: {noEntry.Count == empty.Count}");
        TestContext.Out.WriteLine("empty: " + string.Join(" ", empty));
        Assert.That(empty, Is.EquivalentTo(noEntry), "an empty name with an itemname entry");
        // the item-gain notice's badge (a gem): its own table name
        if (Made(70900001) is { } badge) Assert.That(Name(badge), Is.EqualTo(t.ItemName(70900001)).And.Not.Empty);
    }

    /// <summary>A pet item: the pet block's name (else the table name) in 0xcd6 for a type 1 pet, 0xcd5 for another one,
    /// as it is without a CPetDataTable row.</summary>
    [Test]
    public void PetNameByPetType()
    {
        var t = Model().Tables;
        var pets = Ids().Select(id => t.Items.Get(id)).Where(d => d.IsPetItem).ToList();
        Assume.That(pets, Is.Not.Empty);
        string Fmt(int common, string n) => t.Common(common).Replace("{0}", n);
        foreach (var d in pets)
        {
            var item = Made(d.Id);
            string table = t.ItemName(d.Id);
            string expect = t.Pets.TypeOf((uint)d.PetId) switch { 1 => Fmt(0xcd6, table), int => Fmt(0xcd5, table), null => table };
            Assert.That(Name(item), Is.EqualTo(expect), $"pet item {d.Id}");
        }
        var battle = pets.FirstOrDefault(d => t.Pets.TypeOf((uint)d.PetId) == 1);
        Assume.That(battle, Is.Not.Null);
        var named = Made(battle.Id);
        named.Record = new EquipItem { Id = battle.Id, PetName = "Rex" };
        Assert.That(Name(named), Is.EqualTo(Fmt(0xcd6, "Rex")));
    }

    /// <summary>A gem's name: the table name, followed by the pet name of a pet skin gem's pet (string table 0x3b).</summary>
    [Test]
    public void GemPetSkinAddsThePetName()
    {
        var t = Model().Tables;
        int gemId = Ids().FirstOrDefault(id => t.Items.Get(id).IsGemItem && t.ItemName(id).Length > 0);
        Assume.That(gemId, Is.Not.Zero);
        uint pet = t.Pets.Ids.FirstOrDefault(p => t.StringTableText(0x3b, p).Length > 0);
        Assume.That(pet, Is.Not.Zero);
        var gem = Made(gemId);
        gem.Record = new EquipItem { Id = gemId, GemType = 1 };
        Assert.That(Name(gem), Is.EqualTo(t.ItemName(gemId)));
        gem.Record = new EquipItem { Id = gemId, GemType = 11, GemPetId = (int)pet };
        Assert.That(Name(gem), Is.EqualTo(t.ItemName(gemId) + t.StringTableText(0x3b, pet)));
        gem.EnchantLevel = 3;                               // a gem with a gem name returns before the enchant forms
        Assert.That(Name(gem), Is.EqualTo(t.ItemName(gemId) + t.StringTableText(0x3b, pet)));
    }

    /// <summary>A score: a custom score's block title when its m_str20 is set, else the mode 7 name, else the table name.</summary>
    [Test]
    public void ScoreUsesTheMusicTitle()
    {
        var t = Model().Tables;
        int scoreId = Ids().FirstOrDefault(id => t.Items.Get(id).IsScoreItem);
        Assume.That(scoreId, Is.Not.Zero);
        var score = Made(scoreId);
        Assert.That(Name(score), Is.EqualTo(t.ItemName(scoreId)));
        score.Record = new EquipItem { Id = scoreId, HasMusic = true, MusicTitle = "Song", MusicWriter = "Writer", NotifiedName = "Given" };
        Assert.That(Name(score), Is.EqualTo("Song"));
        score.Record.MusicWriter = "";
        Assert.That(Name(score), Is.EqualTo("Given"));
        score.Record.NotifiedName = "";
        Assert.That(Name(score), Is.EqualTo(t.ItemName(scoreId)));
        score.Record = new EquipItem { Id = scoreId, MusicTitle = "Song", MusicWriter = "Writer" };   // no custom block
        Assert.That(Name(score), Is.EqualTo(t.ItemName(scoreId)));
    }

    /// <summary>A UGC look: blocked gives 0xf4; a local path (m_n30 and an item/ or blueprint/ url) the look's name; else the
    /// table name.</summary>
    [Test]
    public void UgcLookName()
    {
        var t = Model().Tables;
        int id = Ids().FirstOrDefault(i => t.Items.Get(i).IsUgcType && t.ItemName(i).Length > 0);
        Assume.That(id, Is.Not.Zero);
        var item = Made(id);
        var look = new UgcLook { ItemName = "Made", Flag30 = 1, Url = "item/ms2/01/0/1.m2u" };
        item.Record = new EquipItem { Id = id, HasUgc = true, Ugc = look };
        Assert.That(Name(item), Is.EqualTo("Made"));
        look.Url = "blueprint/ms2/01/0/1.m2u";
        Assert.That(Name(item), Is.EqualTo("Made"));
        look.Flag30 = 0;
        Assert.That(Name(item), Is.EqualTo(t.ItemName(id)));
        look.Flag30 = 1; look.Url = "";
        Assert.That(Name(item), Is.EqualTo(t.ItemName(id)));
        look.State = 2;
        Assert.That(Name(item), Is.EqualTo(t.Common(0xf4)));
    }
}

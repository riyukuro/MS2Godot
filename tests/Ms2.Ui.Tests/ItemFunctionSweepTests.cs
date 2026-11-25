using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The item functions of the data set (needs MS2_DATA): every item's &lt;function name&gt;, counted, against the client's
/// RegisterItemUseFuncs table (unit_140d1ae30_itemfunctiondispatch.cpp:373). A name the table has goes to its own handler in
/// UseItem; any other name is the plain use request (FUN_140a63880).
/// </summary>
public class ItemFunctionSweepTests
{
    /// <summary>RegisterItemUseFuncs' names, in its order.</summary>
    static readonly string[] Registered =
    {
        "OpenItemBox", "OpenItemBoxWithKey", "OpenDungeonItemBox", "CallAirTaxi", "CallMedic", "CallBlackMarket", "OpenBank",
        "LevelPotion", "OpenInstrument", "ExpandInven", "ChangeCharName", "ChangeGuildName", "ChangeGender", "ResetAbilityCoolTime",
        "RecallGuild", "RecallWedding", "RecallParty", "EnchantScroll", "FishingRod", "UGCMapPackage", "SelectItemBox",
        "OpenItemBoxLullu", "OpenItemBoxLulluSimple", "OpenPetBox", "ItemRemakeScroll", "ItemRePackingScroll", "PetTraining",
        "OpenWebPage", "ItemSocketScroll", "ItemExchangeScroll", "ItemExtraction", "PetExtraction", "QuestScroll", "ItemMergeRevert",
        "OpenMassive", "ItemActiveCodiTab", "ItemChangeBeauty", "InstallBillBoard", "OpenCoupleEffectBox", "SuperWorldChat",
        "WeddingChat", "TreasureMap", "CreateReactor", "FestivalEvent", "BlueprintImport", "ChatEmoticonAdd",
        "OpenItemBoxLimitedQuantity", "OpenGachaBox", "QuestStart",
    };

    /// <summary>Every item id of the data set: the rows of the itemdata/&lt;n&gt;.xml files (GMS2) and the item/&lt;a&gt;/&lt;b&gt;/&lt;id&gt;.xml
    /// files (KMS).</summary>
    internal static SortedSet<int> ItemIds(PackSet packs)
    {
        var ids = new SortedSet<int>();
        var perFile = new Regex(@"^item/\d+/\d+/(\d+)\.xml$");
        foreach (var entry in packs.List("Xml"))
        {
            if (entry.StartsWith("itemdata/") && entry.EndsWith(".xml") && int.TryParse(entry[9..^4], out _))
                foreach (var e in Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", entry)).Root.Elements("item"))
                    { if (int.TryParse((string)e.Attribute("id"), out int i)) ids.Add(i); }
            else if (perFile.Match(entry) is { Success: true } m) ids.Add(int.Parse(m.Groups[1].Value));
        }
        return ids;
    }

    [Test]
    public void TheOwnHandlerListIsTheClientsTable()
    {
        Assert.That(InventoryController.OwnUseFunctions, Is.EquivalentTo(Registered));
    }

    [Test]
    public void EveryItemFunctionOfTheDataIsCounted()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var items = new ItemDataTable(packs);
        var ids = ItemIds(packs);
        var byName = new SortedDictionary<string, (int Count, int First)>(StringComparer.Ordinal);
        int read = 0;
        foreach (int id in ids)
        {
            if (items.Get(id) is not { } data) continue;
            read++;
            if (!data.HasFunction) continue;
            var c = byName.TryGetValue(data.FunctionName, out var had) ? had : (Count: 0, First: id);
            byName[data.FunctionName] = (c.Count + 1, c.First);
        }
        TestContext.Out.WriteLine($"items {ids.Count}, read {read}, with a function {byName.Values.Sum(v => v.Count)}, names {byName.Count}");
        foreach (var (name, (count, first)) in byName)
            TestContext.Out.WriteLine($"  {(Registered.Contains(name) ? "own  " : "plain")} {name}: {count} (first {first} \"{items.Get(first)?.FunctionParameter}\")");
        foreach (var name in Registered.Where(n => !byName.ContainsKey(n)))
            TestContext.Out.WriteLine($"  none  {name}");
        Assert.That(read, Is.GreaterThan(0));
    }
}

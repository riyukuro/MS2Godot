using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIMasteryDialog's slot and type mapping (GetTypeForSlot), the craft box's required items and the craft gates
/// (CMasteryManager::FUN_140b6d470's precheck, the count input and the hit that cancels the cast).</summary>
public class MasteryControllerTests
{
    [TestCase(0, 3), TestCase(1, 5), TestCase(2, 4), TestCase(3, 6), TestCase(4, 8), TestCase(5, 7), TestCase(6, 9),
     TestCase(7, 10), TestCase(8, 0), TestCase(9, 0xb), TestCase(0xe, 0)]
    public void TypeForSlotFollowsTheClient(int slot, int type) => Assert.That(MasteryController.TypeForSlot(slot), Is.EqualTo(type));

    [Test]
    public void CraftBoxKeepsItsRequiredItems()
    {
        var box = new MessageBoxData(0x23);
        box.SetRequiredItems(new[] { (1, 0, 2), (3, 1, 4) });
        Assert.That(box.RequiredItems, Is.EqualTo(new[] { (1, 0, 2), (3, 1, 4) }));
        box.SetRequiredItems(new[] { (5, 0, 1) });
        Assert.That(box.RequiredItems, Has.Count.EqualTo(1));
    }

    // ---- the craft gates -----------------------------------------------------------------------------------------

    const int Pass = 70000005, NeedMastery = 70000001, NeedQuest = 70000002, NeedMeso = 70000003, NeedItem = 70000004;

    /// <summary>The five precheck cases and a recipe that passes it. Type 7 (blacksmithing) is at main tab 1.</summary>
    static MasteryRecipeTable Table()
    {
        string Row(int id, string attrs) =>
            $"<receipe id='{id}' masteryType='7' rewardItem1='20000004,1,1' {attrs}/>";
        var doc = XDocument.Parse("<ms2receipe>"
            + Row(NeedMastery, "requireMastery='100'")
            + Row(NeedQuest, "requireQuest='1234'")
            + Row(NeedMeso, "requireMeso='5000'")
            + Row(NeedItem, "requireItem1='20000004:0,0,3'")
            + Row(Pass, "")
            + "</ms2receipe>");
        return new MasteryRecipeTable(doc);
    }

    /// <summary>A controller with the tables, the recipe rows and a host that carries the model (no movie).</summary>
    static MasteryController Make(out UiModel model, out List<MessageBoxData> boxes, out List<string> notices)
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var tables = new GameTables(new PackSet(d), "en");
        model = new UiModel(tables) { HasLocalPlayer = true };
        model.Player.Mesos = 100;
        var c = new MasteryController { Table = Table(), Data = new MasteryData() };
        var layout = new JsonObject
        {
            ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 },
            ["symbols"] = new JsonObject(), ["root"] = new JsonObject(),
        };
        new UiHost(new Player(new Runtime(), layout), c, model);
        boxes = new();
        notices = new();
        c.ShowBox = boxes.Add;
        c.Notice = notices.Add;
        return c;
    }

    [TestCase(0, MasteryController.CraftBlock.NoPlayer)]
    [TestCase(NeedMastery, MasteryController.CraftBlock.Mastery)]
    [TestCase(NeedQuest, MasteryController.CraftBlock.Quest)]
    [TestCase(NeedMeso, MasteryController.CraftBlock.Meso)]
    [TestCase(NeedItem, MasteryController.CraftBlock.Item)]
    [TestCase(Pass, MasteryController.CraftBlock.None)]
    public void PrecheckFollowsCMasteryManager(int id, MasteryController.CraftBlock expected)
    {
        var c = Make(out var model, out _, out _);
        if (id == 0) model.HasLocalPlayer = false;
        Assert.That(c.CraftBlocked(id), Is.EqualTo(expected));
    }

    /// <summary>The HUD's ItemCount (InventoryCount) makes the precheck refuse an ingredient bound to another character,
    /// as FUN_140a5f180's CanUseItem test does.</summary>
    [Test]
    public void AnUnusableIngredientBlocksTheCraft()
    {
        var c = Make(out var model, out var boxes, out var notices);
        var bag = new List<Ms2.Net.Packets.EquipItem>();
        c.ItemCount = id => InventoryCount.ById(bag, model.Tables.Items, id, 0, 100, 1000);
        c.Table = new MasteryRecipeTable(XDocument.Parse(
            "<ms2receipe><receipe id='70000010' masteryType='7' requireItem1='20000004:0,0,1' rewardItem1='20000004,1,1'/></ms2receipe>"));
        bag.Add(new Ms2.Net.Packets.EquipItem { Id = 20000004, Rarity = 1, Amount = 1,
            Transfer = { HasBinding = true, BindingId = 999 } });
        Assert.That(c.CraftBlocked(70000010), Is.EqualTo(MasteryController.CraftBlock.Item));
        c.OpenCraftBox(70000010);
        Assert.That(boxes, Is.Empty, "no box for the unusable ingredient");
        Assert.That(notices, Has.Count.EqualTo(1));
        bag[0].Transfer.BindingId = 100;                       // now the character's own
        Assert.That(c.CraftBlocked(70000010), Is.EqualTo(MasteryController.CraftBlock.None));
    }

    [Test]
    public void ThePrecheckBlocksTheCraftBoxAndShowsItsNotice()
    {
        var c = Make(out _, out var boxes, out var notices);
        foreach (int id in new[] { NeedMastery, NeedQuest, NeedMeso, NeedItem })
        {
            c.OpenCraftBox(id);
            Assert.That(boxes, Is.Empty, $"recipe {id}: no box");
            Assert.That(notices, Has.Count.EqualTo(1), $"recipe {id}: one notice");
            Assert.That(notices[0], Is.Not.Null, $"recipe {id}: a notice text");
            notices.Clear();
        }
        c.OpenCraftBox(Pass);
        Assert.That(boxes, Has.Count.EqualTo(1), "the recipe that passes the precheck opens its box");
        Assert.That(notices, Is.Empty);
    }

    [Test]
    public void AnEmptyCountQueuesNoCast()
    {
        var c = Make(out _, out var boxes, out _);
        c.OpenCraftBox(Pass);
        Assert.That(boxes, Has.Count.EqualTo(1));
        c.CountInput = _ => "";                   // the box's input left empty
        boxes[0].Callback(0, boxes[0]);
        Assert.That(c.CraftState.Total, Is.Zero, "no count, no total, no cast");
        c.Process(1_000);
        Assert.That(c.CraftState.Total, Is.Zero, "the frame loop starts no cast without a total");
    }

    /// <summary>GetMaxCraftCount (@0x140eb6d40): 0 without the recipe and 0 when the mesos do not cover one craft.</summary>
    [Test]
    public void TheCraftableCountIsZeroWhenNothingCanBeCrafted()
    {
        var c = Make(out var model, out _, out _);
        Assert.That(c.MaxCraftCount(Pass), Is.EqualTo(1000), "no meso or ingredient limit in the row");
        Assert.That(c.MaxCraftCount(999), Is.Zero, "no recipe, no count");
        c.Table = new MasteryRecipeTable(XDocument.Parse("<ms2receipe><receipe id='70000009' masteryType='7' requireMeso='5000' rewardItem1='20000004,1,1'/></ms2receipe>"));
        model.Player.Mesos = 100;
        Assert.That(c.MaxCraftCount(70000009), Is.Zero, "100 mesos do not cover a 5000 meso craft");
    }

    /// <summary>A craft of two, queued from the count box (the count hook stands in for the box's text input).</summary>
    static void StartCraft(MasteryController c, List<MessageBoxData> boxes, string count = "2")
    {
        c.CountInput = _ => count;
        c.OpenCraftBox(Pass);
        Assert.That(boxes, Has.Count.EqualTo(1), "the box of the passing recipe");
        boxes[0].Callback(0, boxes[0]);
    }

    [Test]
    public void AHitCancelsTheCast()
    {
        var c = Make(out _, out var boxes, out _);
        var sent = new List<int>();
        c.SendCraft += sent.Add;
        StartCraft(c, boxes);
        c.OnDamaged(-1);                          // OnDamagedMyPC: a negative total damage is a hit
        Assert.That(c.CraftState.Total, Is.Zero, "the hit reset the craft");
        c.OnCastingFinished();
        Assert.That(sent, Is.Empty, "the hit cancelled the craft before it finished");
    }

    [Test]
    public void HealingAndNoHitLeaveTheCastRunning()
    {
        var c = Make(out _, out var boxes, out _);
        var sent = new List<int>();
        c.SendCraft += sent.Add;
        StartCraft(c, boxes);
        c.OnDamaged(0);                           // no damage (a heal record)
        Assert.That(c.CraftState.Total, Is.EqualTo(2), "the craft still has its total");
        c.OnCastingFinished();
        Assert.That(sent, Is.EqualTo(new[] { Pass }), "the first craft of two goes out");
    }

    /// <summary>The movie's main tab gate (onCraftOk is main tab 1's): tab 0 never opens a box.</summary>
    [Test]
    public void TheCraftBoxIsMainTabOnesAlone()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
        model.Player.Mesos = 100;
        var c = new MasteryController { Table = Table(), Data = new MasteryData() };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        var boxes = new List<MessageBoxData>();
        c.ShowBox = boxes.Add;
        var tab = h.Child("m_mainTab");
        Assert.That(tab, Is.Not.Null);
        h.Invoke(tab, "setSelectedButtonByIndex", 0);
        Assert.That(Rt.ToInt32(h.Get(tab, "selectedIndex")), Is.EqualTo(0));
        c.OnCallback("onCraftOk", new object[] { Pass });
        Assert.That(boxes, Is.Empty, "main tab 0 has no craft box");
        h.Invoke(tab, "setSelectedButtonByIndex", 1);
        Assert.That(Rt.ToInt32(h.Get(tab, "selectedIndex")), Is.EqualTo(1));
        c.OnCallback("onCraftOk", new object[] { Pass });
        Assert.That(boxes, Has.Count.EqualTo(1), "main tab 1 opens it");
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
    }
    /// <summary>
    /// CMasteryManager::GetTotalClaimableRewardLevels (masterymanager.cpp:78) reads the local player from the world, not the dialog:
    /// the game menu asks for it (FUN_141938ae0(0x4f1e)) before the mastery movie was ever loaded, as on a window relayout. Without
    /// a host it counts on the HUD's model, as the loaded dialog does; without a local player it is 0 (masterymanager.cpp:90).
    /// </summary>
    [Test]
    public void ClaimableCountWorksBeforeTheMovieLoads()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var tables = new GameTables(new PackSet(d), "en");
        var model = new UiModel(tables) { HasLocalPlayer = true };
        var data = new MasteryData();
        Array.Fill(data.Exp, uint.MaxValue - 1);       // every grade reached, none received
        var world = model;
        var closed = new MasteryController { Table = tables.MasteryRecipes, Data = data, World = () => world };
        var hosted = new MasteryController { Table = tables.MasteryRecipes, Data = data };
        var layout = new JsonObject
        {
            ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 },
            ["symbols"] = new JsonObject(), ["root"] = new JsonObject(),
        };
        new UiHost(new Player(new Runtime(), layout), hosted, model);
        int n = hosted.TotalClaimableRewardLevels();
        Assert.That(n, Is.GreaterThan(0), "the data has grade rewards for the default job");
        Assert.That(closed.TotalClaimableRewardLevels(), Is.EqualTo(n), "no host: the HUD's model");
        world = new UiModel(tables) { HasLocalPlayer = false };
        Assert.That(closed.TotalClaimableRewardLevels(), Is.EqualTo(0), "a relayout's new model, not the first one");
        world = model;
        model.HasLocalPlayer = false;
        Assert.That(closed.TotalClaimableRewardLevels(), Is.EqualTo(0), "no local player");
        Assert.That(new MasteryController { Table = tables.MasteryRecipes, Data = data }.TotalClaimableRewardLevels(), Is.EqualTo(0),
                    "no model at all");
    }
}

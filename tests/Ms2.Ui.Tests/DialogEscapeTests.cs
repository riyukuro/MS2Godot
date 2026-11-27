using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The Esc key over the dialogs: UISwfEntityManager::FUN_1422d1c20 asks each shown dialog, last-added first, through
/// UIDialog::vfunc_13 (uidialog.cpp:840-853), which closes one that is not always on, not minimized and closable (no flag 1);
/// the field Esc (FUN_1418fcb70) then asks FUN_141274ff0 and CloseAllDialogs over the same registry. A dialog no hand
/// list names, the Maple Guide among them, closes like any other (needs MS2_DATA).
/// </summary>
public class DialogEscapeTests
{
    static MovieSource src;
    static GameTables tables;
    static PackSet packs;

    static UiModel Model()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        if (packs == null)
        {
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        return new UiModel(tables);
    }

    static UiHost Load(UiController c)
    {
        var m = Model();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        return h;
    }

    static void Settle(params UiController[] cs)
    {
        for (int i = 0; i < 60; i++)
            foreach (var c in cs) c.Host?.Player.Tick();
    }

    [Test]
    public void EscClosesTheTopShownClosableDialogAndPassesTheOthers()
    {
        var guide = new LearningQuestController();
        var menu = new SystemMenuController { MapType = 0, ChannelWord = "Ch.", CurrentChannel = 1, Channels = new[] { new ChannelRow(1) } };
        var book = new Ms2.Net.Packets.ItemBook { TabOf = _ => 0, BaseSlots = _ => 24 };
        var inv = new InventoryController { View = new Ms2.Net.Packets.InventoryView(book) };
        Load(inv); Load(guide); Load(menu);
        Assert.That(guide.EntityShown && inv.EntityShown && menu.EntityShown, Is.True, "all three shown");
        Assert.That(menu.NotClosable, Is.True, "the system menu sets flag 1 (uisystemmenudialog.cpp:137)");
        Assert.That(guide.NotClosable || guide.AlwaysOn, Is.False, "the guide sets flag 3 only");

        UiController[] registry = { inv, guide, menu };
        Assert.That(DialogGates.AnyClosable(registry), Is.True);
        // the system menu is the last added but not closable: the guide, next down, takes the key
        Assert.That(DialogGates.EscapeKey(registry), Is.True, "a dialog takes Esc");
        Settle(registry);
        Assert.That(guide.IsClosed, Is.True, "the guide closes on Esc");
        Assert.That(inv.IsClosed || menu.IsClosed, Is.False, "one dialog per key");

        Assert.That(DialogGates.EscapeKey(registry), Is.True);
        Settle(registry);
        Assert.That(inv.IsClosed, Is.True, "then the inventory");
        Assert.That(DialogGates.EscapeKey(registry), Is.False, "the system menu alone passes the key on");
        Assert.That(DialogGates.AnyClosable(registry), Is.False, "so the field Esc toggles the menu");
        Assert.That(menu.IsClosed, Is.False);
    }

    /// <summary>The fame point board takes Esc through the same walk, in its turn: a dialog opened over it closes first.</summary>
    [Test]
    public void EscClosesTheFameBoardInItsTurn()
    {
        Model();
        var fame = new FamePointBoardController { TableString = tables.StringTableText };
        var guide = new LearningQuestController();
        Load(fame); Load(guide);
        Assert.That(fame.EntityShown && guide.EntityShown, Is.True, "both shown");

        UiController[] registry = { fame, guide };
        Assert.That(DialogGates.EscapeKey(registry), Is.True);
        Settle(registry);
        Assert.That(guide.IsClosed, Is.True, "the dialog opened last closes first");
        Assert.That(fame.IsClosed, Is.False, "one dialog per key");

        Assert.That(DialogGates.EscapeKey(registry), Is.True);
        Settle(registry);
        Assert.That(fame.IsClosed, Is.True, "then the fame point board");
        Assert.That(DialogGates.AnyClosable(registry), Is.False);
    }

    /// <summary>
    /// The HUD's key path (HudScene.DialogKeys) leaves these dialogs to the walk: no hand Esc line closes the fame point board,
    /// the level-up guide or the learning guide before it, and none closes the item enchant, treasure map or merge dialogs,
    /// whose client vfunc_13 does not close (uiitemenchantdialog.cpp:550, uiitemtreasuremapdialog.cpp:167,
    /// uiitemmergeoptiondialog.cpp:430). The walk is the key path's last Esc line.
    /// </summary>
    [Test]
    public void TheKeyPathHasNoHandEscLineForTheWalkDialogs()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MS2Godot.sln"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "the repo root");
        string hud = File.ReadAllText(Path.Combine(dir.FullName, "game", "ui", "HudScene.cs"));
        int start = hud.IndexOf("bool DialogKeys(InputEvent e)", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0), "DialogKeys");
        const string walk = "DialogGates.EscapeKey(MovieDialog.Live)";
        int end = hud.IndexOf(walk, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), "the walk line");
        string keys = hud[start..end];
        foreach (var hand in new[] { "famePointBoard.Close", "FamePointBoardOpen", "levelUpGuide.Close", "learningQuest.Close",
                                     "ItemEnchantKeys", "itemEnchant.Close", "TreasureMapKeys", "treasureMap.Close",
                                     "ItemMergeKeys", "itemMerge.Close" })
            Assert.That(keys, Does.Not.Contain(hand), hand + " runs before the walk");
        foreach (var f in new[] { "HudScene.ItemEnchant.cs", "HudScene.TreasureMap.cs", "HudScene.ItemMerge.cs" })
            Assert.That(File.ReadAllText(Path.Combine(dir.FullName, "game", "ui", f)), Does.Not.Contain("Key.Escape"), f + " closes on Esc by hand");
    }

    /// <summary>The item enchant and treasure map dialogs' vfunc_13 return 0: the walk passes them (the system menu toggles);
    /// the item merge dialog's takes Esc only in phase 4/5, so a shown merge in phase 0 passes it too.</summary>
    [Test]
    public void EnchantTreasureMapAndMergePassEsc()
    {
        Assert.That(new ItemEnchantDialogController().EscapeKey(), Is.False, "the enchant");
        var map = new ItemTreasureMapDialogController();
        var merge = new ItemMergeOptionDialogController();
        Load(map); Load(merge);
        Assert.That(map.EntityShown && merge.EntityShown, Is.True, "both shown");
        Assert.That(merge.Phase, Is.EqualTo(0));
        UiController[] registry = { map, merge };
        Assert.That(DialogGates.EscapeKey(registry), Is.False, "neither takes the key");
        Settle(registry);
        Assert.That(map.IsClosed || merge.IsClosed, Is.False, "neither closes");
        Assert.That(DialogGates.AnyClosable(registry), Is.True, "the field Esc's CloseAllDialogs still closes them");
    }

    [Test]
    public void CloseAllClosesEveryClosableDialogButTheSystemMenu()
    {
        var guide = new LearningQuestController();
        var menu = new SystemMenuController { MapType = 0, ChannelWord = "Ch.", CurrentChannel = 1, Channels = new[] { new ChannelRow(1) } };
        var book = new Ms2.Net.Packets.ItemBook { TabOf = _ => 0, BaseSlots = _ => 24 };
        var inv = new InventoryController { View = new Ms2.Net.Packets.InventoryView(book) };
        Load(guide); Load(inv); Load(menu);
        UiController[] registry = { guide, inv, menu };
        DialogGates.CloseAll(registry);
        Settle(registry);
        Assert.That(guide.IsClosed && inv.IsClosed, Is.True, "CloseAllDialogs closes both");
        Assert.That(menu.IsClosed, Is.False, "flag 1 is not closable (IsDialogClosable, uiservice.cpp:1001)");
    }
}

using System.Runtime.CompilerServices;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// A click on a dialog's window brings it forward (needs MS2_DATA). Window.configUI listens for MOUSE_DOWN
/// (onSortDepthControl), and setTopMostLayer sends OnTopMostLayer when CommonDepth.topmostLayer(this) is true
/// (CommonDepth.as:419-450). That is true only for an object whose loader (loaderInfo.loader) is a child of a depth
/// layer: msLoader.loadSwf adds the dialog's loader to its layer (CommonDepth.addLayer, the entity's m_2c, 0x19 for a
/// UIDialog, uidialog.cpp:202). The client moves the loader to its layer's end, and UIDialog::vfunc_78
/// (uidialog.cpp:809) moves the dialog to its entity list's tail, so Esc closes it first.
/// </summary>
public class DialogDepthLayerTests
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

    const string WindowClass = "scaleform.ms2.controls.dialog.Window";
    const string CommonDepthClass = "scaleform.ms2.common.CommonDepth";

    static List<AsDisplayObject> Windows(Player p)
    {
        var found = new List<AsDisplayObject>();
        if (p.RT.GetClass(WindowClass) is not { } w) return found;
        void Walk(AsDisplayObject o)
        {
            if (p.RT.IsType(o, w)) found.Add(o);
            if (o is AsContainer c) foreach (var ch in c.Children.ToArray()) Walk(ch);
        }
        Walk(p.Stage);
        return found;
    }

    static void MouseDown(Player p, AsDisplayObject target) =>
        p.Call(target, "dispatchEvent", new object[] { p.RT.GetClass("flash.events.MouseEvent").Construct("mouseDown", true, false) });

    static object LayerSprite(Player p, int layer)
    {
        var layers = p.RT.GetProperty(p.RT.GetClass(CommonDepthClass), Name.Public("m_depthLayers"));
        return p.RT.GetProperty(layers, Name.Public(layer.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    static IEnumerable<Type> Controllers() =>
        typeof(UiController).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(UiController).IsAssignableFrom(t)).OrderBy(t => t.Name);

    /// <summary>The dialogs whose ctor (or a base class's) sets m_2c; every other dialog is on 0x19 (uidialog.cpp:202).</summary>
    static readonly Dictionary<Type, int> ClientLayers = new()
    {
        [typeof(BeautyShopDialogController)] = 0xc,         // uibeautyshopdialog.cpp:508
        [typeof(BeautyStyleDialogController)] = 0xc,        // uibeautyshopstyledialog.cpp:782
        [typeof(ItemColoringDialogController)] = 0xc,       // uiitemcoloringdialog.cpp:253
        [typeof(ItemTreasureMapDialogController)] = 0xc,    // uiitemtreasuremapdialog.cpp:89
        [typeof(ItemMergeOptionDialogController)] = 0x1b,   // uiitemmergeoptiondialog.cpp:317
        [typeof(ItemEnchantDialogController)] = 0x1b,       // UIItemModifyDialogBase, uiitemenchantdialog.cpp:332
        [typeof(ItemRemakeOptionDialogController)] = 0x1b,  // UIItemModifyDialogBase, uiitemenchantdialog.cpp:332
        [typeof(PartyController)] = 0x11,                   // uipartydialog.cpp:400
        [typeof(PartyDpsController)] = 0x11,                // uipartydialog.cpp:1200
        [typeof(QuestGuideController)] = 0xd,               // uiquesteventlistener.cpp:337
        [typeof(RevivalDialogController)] = 0x1d,           // uirevivaldialog.cpp:156
        [typeof(TimerDialogController)] = 19,               // uitimerdialog.cpp:33
        [typeof(TriggerTimerDialogController)] = 0x13,      // uitriggertimerdialog.cpp:45
        [typeof(GameMenuController)] = 14,                  // UIBuiltInDialog, uibuiltindialog.cpp:21
        [typeof(MainPlayerInfoController)] = 14,            // UIBuiltInDialog, uibuiltindialog.cpp:21
        [typeof(RecallScrollNotifyController)] = 14,        // UIBuiltInDialog (its ctor @0x14129ba60 calls it), uibuiltindialog.cpp:21
        [typeof(DungeonLobbyController)] = 0x16,            // uidungeonlobby.cpp:48
        [typeof(ChangeJobDialogController)] = 0x23,         // UIChangeJobDialog ctor @0x140bd7e10
    };

    /// <summary>The movie's ItemGetOptionDialog constructor calls setTopMostEnable(false): a click on it sends nothing.</summary>
    static readonly HashSet<Type> TopMostOff = new() { typeof(ItemGetOptionDialogController) };

    /// <summary>Each dialog's load layer is the client's m_2c for it.</summary>
    [Test]
    public void EveryDialogLoadsIntoItsClientLayer()
    {
        int n = 0;
        foreach (var t in Controllers())
        {
            var c = (UiController)RuntimeHelpers.GetUninitializedObject(t);
            if (!c.IsDialogEntity) continue;
            n++;
            int want = ClientLayers.TryGetValue(t, out var l) ? l : CommonDepth.Dialog;
            Assert.That(c.LoadLayer, Is.EqualTo(want), t.Name);
        }
        Assert.That(n, Is.GreaterThanOrEqualTo(85), "dialog controllers checked");
    }

    /// <summary>
    /// The whole list: every dialog controller whose movie has a Window is loaded into its layer, and a mouse down on the
    /// window sends OnTopMostLayer (none for the one whose movie turns it off, until it is turned back on).
    /// STILL-OPEN: the non-dialog entities' movies with a Window (the character list and creation, the login dialog, the
    /// upper HUD and HP bar) are not put into a layer and send nothing on a click.
    /// </summary>
    [Test]
    public void EveryDialogWindowSendsOnTopMostLayerOnAMouseDown()
    {
        Model();
        int checkedCount = 0;
        var failures = new List<string>();
        foreach (var t in Controllers())
        {
            UiController c;
            string movie;
            try { c = (UiController)RuntimeHelpers.GetUninitializedObject(t); movie = c.Movie; }
            catch (Exception) { continue; }
            if (string.IsNullOrEmpty(movie) || !c.IsDialogEntity) continue;
            var (_, p) = src.Open(movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.HostLayer = c.LoadLayer;
            var calls = new List<string>();
            p.External = (ev, a) => { if (a.Length > 0 && Rt.ToString(a[0]) == "OnTopMostLayer") calls.Add(ev); return null; };
            if (!p.Start()) { failures.Add($"{t.Name}: {movie} did not start"); continue; }
            for (int i = 0; i < 10; i++) p.Tick();
            var ws = Windows(p);
            if (ws.Count == 0) continue;
            checkedCount++;
            if (p.HostLoader.Parent != LayerSprite(p, c.LoadLayer)) failures.Add($"{t.Name}: loader not in layer {c.LoadLayer}");
            calls.Clear();
            MouseDown(p, ws[0]);
            if (TopMostOff.Contains(t))
            {
                if (calls.Count != 0) failures.Add($"{t.Name}: sent OnTopMostLayer with it turned off");
                p.Call(ws[0], "setTopMostEnable", new object[] { true });
                MouseDown(p, ws[0]);
            }
            if (calls.Count == 0) failures.Add($"{t.Name} ({movie}): no OnTopMostLayer");
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        Assert.That(checkedCount, Is.GreaterThanOrEqualTo(85), "dialogs with a window checked");
    }

    /// <summary>
    /// Two dialogs of one layer on one stage: the inventory opened first, the fame point board after it. A mouse down on the
    /// inventory's window moves it to its layer's end (CommonDepth.topmostLayer's setChildIndex), raises it on screen and
    /// makes it the entity list's tail (UIDialog::vfunc_78), so Esc closes the inventory first.
    /// </summary>
    [Test]
    public void AClickOnTheOlderWindowBringsItForwardAndEscClosesItFirst()
    {
        Model();
        var book = new Ms2.Net.Packets.ItemBook { TabOf = _ => 0, BaseSlots = _ => 24 };
        var inv = new InventoryController { View = new Ms2.Net.Packets.InventoryView(book) };
        var fame = new FamePointBoardController { TableString = tables.StringTableText };
        var hInv = Load(inv);
        var hFame = Load(fame);
        var stage = new UiStage();
        int raisedInv = 0, raisedFame = 0;
        stage.Add(hInv, () => (0, 0), () => 0, null, () => raisedInv++);
        stage.Add(hFame, () => (0, 0), () => 1, null, () => raisedFame++);
        Assert.That(stage.InLayer(CommonDepth.Dialog), Is.EqualTo(new[] { hInv.Player, hFame.Player }), "add order");
        Assert.That(fame.LayerPlace, Is.GreaterThan(inv.LayerPlace), "the board opened later");

        var ws = Windows(hInv.Player);
        Assert.That(ws, Is.Not.Empty, "the inventory has a window");
        MouseDown(hInv.Player, ws[0]);

        Assert.That(hInv.Calls, Does.Contain("OnTopMostLayer"));
        Assert.That(stage.InLayer(CommonDepth.Dialog), Is.EqualTo(new[] { hFame.Player, hInv.Player }), "moved to the layer's end");
        Assert.That((raisedInv, raisedFame), Is.EqualTo((1, 0)), "only the clicked dialog is raised");
        Assert.That(inv.LayerPlace, Is.GreaterThan(fame.LayerPlace), "the entity list's tail");

        UiController[] registry = { inv, fame };
        Assert.That(DialogGates.EscapeKey(registry), Is.True);
        Settle(registry);
        Assert.That(inv.IsClosed, Is.True, "the clicked inventory closes first");
        Assert.That(fame.IsClosed, Is.False);
    }

    /// <summary>A movie alone in its layer (numChildren &lt;= 1) is not moved: a party dialog (layer 0x11) and the inventory
    /// (0x19) on one stage keep their places and nothing is raised.</summary>
    [Test]
    public void AMovieAloneInItsLayerIsNotMoved()
    {
        Model();
        var book = new Ms2.Net.Packets.ItemBook { TabOf = _ => 0, BaseSlots = _ => 24 };
        var inv = new InventoryController { View = new Ms2.Net.Packets.InventoryView(book) };
        var hInv = Load(inv);
        var (_, party) = src.Open("uipartydialog", r => r.Budget = 20_000_000);
        party.StageW = 1280; party.StageH = 720; party.HostLayer = CommonDepth.Party;
        Assert.That(party.Start(), Is.True);
        var stage = new UiStage();
        int raised = 0;
        stage.Add(hInv, () => (0, 0), () => 0, null, () => raised++);
        stage.Add(party, () => (0, 0), () => 1, null, CommonDepth.Party, () => raised++);
        stage.TopmostLayer(hInv.Player);
        stage.TopmostLayer(party);
        Assert.That(raised, Is.Zero);
        Assert.That(stage.InLayer(CommonDepth.Dialog), Is.EqualTo(new[] { hInv.Player }));
        Assert.That(stage.InLayer(CommonDepth.Party), Is.EqualTo(new[] { party }));
    }
}

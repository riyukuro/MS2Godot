using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIHomeMenuDialog (uihomemenudialog.cpp): the opener OnHomePriority, the gates, the state frame, the button states, the sends.</summary>
public class HomeMenuDialogTests
{
    sealed class Host : IHomeMenuHost
    {
        public bool HasPlayer { get; set; } = true;
        public bool InCubeMode { get; set; }
        public int PlotMode { get; set; }
        public bool CanChange { get; set; }
        public bool CanEditHome { get; set; }
        public bool HasService { get; set; } = true;
        public bool OwnsHome { get; set; } = true;
        public bool InOwnersHouse { get; set; }
        public bool Blueprints = true, Shop = true;
        public bool DesignHasChanges { get; set; }
        public int CubeCount { get; set; }
        public bool FreeCameraActive { get; set; }
        public HashSet<int> Features = new();
        public List<string> Log = new();
        public bool BlueprintAvailable(bool message) { Log.Add("bp " + message); return Blueprints; }
        public bool BlueprintShopAvailable(bool message) => Shop;
        public void ToggleDialog(int uiId) => Log.Add("toggle " + uiId.ToString("x"));
        public bool SendCraftModeOff() { Log.Add("craftOff"); return true; }
        public void EnterCraftMode() => Log.Add("craftOn");
        public void SendRequestMoveFieldMode(int mode) => Log.Add("move " + mode);
        public bool FeatureEnabled(int id) => Features.Contains(id);
        public void BeforeDemolish() => Log.Add("prelude");
        public void SendDemolish() => Log.Add("demolish");
        public void SetFreeCamera(bool on) => Log.Add("camera " + on);
        public void SetSaveSlotMode(bool save) => Log.Add("slots " + (save ? "save" : "load"));
    }

    static (HomeMenuDialogController c, Host h, List<string> notices, List<MessageBoxData> boxes, List<(string, object[])> calls,
        Dictionary<string, Dictionary<string, object>> buttons) Make()
    {
        var h = new Host(); var notices = new List<string>(); var boxes = new List<MessageBoxData>(); var calls = new List<(string, object[])>();
        var buttons = new Dictionary<string, Dictionary<string, object>>();
        var c = new HomeMenuDialogController(h)
        {
            SystemNotice = notices.Add, ShowBox = boxes.Add, CommonText = id => "common" + id, Str = k => k,
            CallMovie = (n, a) => calls.Add((n, a)), SetMember = (_, _, _) => { },
            ChildButton = n => { if (n == "m_craftMode" || n == "m_designMode" || n == "m_blueprintMode" || n == "m_freeCamera" || n == "m_blueprintSave"
                || n == "m_blueprintImport" || n == "m_blueprintExport" || n == "m_blueprintShop") { buttons[n] = new(); return new object(); } return null; },
        };
        return (c, h, notices, boxes, calls, buttons);
    }

    [Test] public void HomePriorityTogglesTheHomeOptionDialogForTheOwner()
    {
        var (c, h, notices, _, _, _) = Make();
        h.CanChange = true;
        Assert.That(c.OnCallback("onhomepriority", null), Is.True, "compared without case");
        Assert.That(h.Log, Is.EqualTo(new[] { "toggle 4f26" }));
        Assert.That(notices, Is.Empty);
    }

    [TestCase(1)] [TestCase(3)]
    public void HomePriorityAlsoOpensInDesignAndBlueprintMode(int mode)
    {
        var (c, h, _, _, _, _) = Make();
        h.PlotMode = mode;
        c.OnCallback("OnHomePriority", null);
        Assert.That(h.Log, Is.EqualTo(new[] { "toggle 4f26" }));
    }

    [Test] public void HomePriorityAndAttributeShowTheNoticeForAVisitor()
    {
        var (c, h, notices, _, _, _) = Make();
        c.OnCallback("OnHomePriority", null);
        c.OnCallback("OnHomeAttribute", null);
        Assert.That(h.Log, Is.Empty);
        Assert.That(notices, Is.EqualTo(new[] { "common1761", "common1761" }), "common 0x6e1 twice");
    }

    [Test] public void HomeAttributeTogglesDialog4f7a()
    {
        var (c, h, _, _, _, _) = Make();
        h.CanChange = true;
        c.OnCallback("OnHomeAttribute", null);
        Assert.That(h.Log, Is.EqualTo(new[] { "toggle 4f7a" }));
    }

    [Test] public void StateFrameNameByOwnerPlotModeAndCraft()
    {
        var (c, h, _, _, _, _) = Make();
        var expect = new[] { (0, false, "default"), (0, true, "default_craft"), (1, false, "design"), (1, true, "design_craft"),
            (3, false, "blueprint"), (3, true, "blueprint_craft"), (2, false, "other"), (2, true, "other"), (7, false, "default") };
        foreach (var (mode, craft, name) in expect)
        {
            h.PlotMode = mode; h.InCubeMode = craft;
            Assert.That(c.GetStateFrameName(), Is.EqualTo(name), $"mode {mode} craft {craft}");
        }
        h.OwnsHome = false;
        h.InCubeMode = false; Assert.That(c.GetStateFrameName(), Is.EqualTo("other"));
        h.InCubeMode = true; Assert.That(c.GetStateFrameName(), Is.EqualTo("other_craft"));
    }

    [Test] public void NoPlayerIsNotCraftMode()
    {
        var (c, h, _, _, _, _) = Make();
        h.InCubeMode = true; h.HasPlayer = false;
        Assert.That(c.IsCraftMode(), Is.False);
    }

    [Test] public void RefreshSetsTheStateAndTurnsTheFreeCameraOffInCraftMode()
    {
        var (c, h, _, _, calls, _) = Make();
        h.InCubeMode = true;
        c.Refresh(true);
        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.That(calls[0].Item1, Is.EqualTo("setState"));
        Assert.That(calls[0].Item2, Is.EqualTo(new object[] { "default_craft" }));
        Assert.That(h.Log, Does.Contain("camera False"));
        h.Log.Clear();
        c.Refresh(false);
        Assert.That(h.Log, Does.Not.Contain("camera False"));
    }

    [Test] public void ButtonStatesFollowTheModes()
    {
        var (c, h, _, _, _, b) = Make();
        h.PlotMode = 1; h.Shop = false;
        var sets = new List<(string, string, object)>();
        c.SetMember = (o, m, v) => sets.Add(("", m, v));
        c.UpdateButtons();
        // craft, design, blueprint (selected, enabled), camera, save, import, export, shop
        Assert.That(sets.Count, Is.EqualTo(9));
        Assert.That(sets[1], Is.EqualTo(("", "selected", (object) true)), "design selected");
        Assert.That(sets[3], Is.EqualTo(("", "enabled", (object) false)), "blueprint mode is disabled in design mode");
        Assert.That(sets[8], Is.EqualTo(("", "enabled", (object) false)), "shop unavailable");
    }

    [Test] public void CraftModeToggleChecksTheOwnerThenSendsOffOrOn()
    {
        var (c, h, notices, _, _, _) = Make();
        c.ToggleCraftMode();
        Assert.That(notices, Is.EqualTo(new[] { "common1761" }));
        h.CanChange = true;
        c.ToggleCraftMode();
        Assert.That(h.Log, Is.EqualTo(new[] { "craftOn" }));
        h.InCubeMode = true;
        c.ToggleCraftMode();
        Assert.That(h.Log, Is.EqualTo(new[] { "craftOn", "craftOff" }));
    }

    [Test] public void CraftModeAlsoAllowedInAnOwnersHouse()
    {
        var (c, h, _, _, _, _) = Make();
        h.InOwnersHouse = true;
        c.OnCallback("ToggleCraftMode", null);
        Assert.That(h.Log, Is.EqualTo(new[] { "craftOn" }));
    }

    [Test] public void DesignModeSkipsTheOwnerCheckWithoutTheService()
    {
        var (c, h, notices, _, _, _) = Make();
        h.HasService = false;
        c.ToggleDesignMode();
        Assert.That(notices, Is.Empty);
        Assert.That(h.Log, Is.EqualTo(new[] { "move 4" }));
    }

    [Test] public void DesignModeEntersMode4AndLeavesWithMode3()
    {
        var (c, h, notices, boxes, _, _) = Make();
        c.ToggleDesignMode();
        Assert.That(notices, Has.Count.EqualTo(1));
        h.CanEditHome = true;
        c.ToggleDesignMode();
        Assert.That(h.Log, Is.EqualTo(new[] { "move 4" }));
        h.PlotMode = 1;
        c.ToggleDesignMode();
        Assert.That(h.Log[^1], Is.EqualTo("move 3"));
        h.DesignHasChanges = true;
        c.ToggleDesignMode();
        Assert.That(h.Log, Has.Count.EqualTo(2), "a question first");
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Msg, Is.EqualTo("s_home_info_exit_messagebox_save"));
        Assert.That(boxes[0].EnterAllowed, Is.False);
        boxes[0].InvokeCallback(1, null);
        Assert.That(h.Log, Has.Count.EqualTo(2), "no sends nothing");
        boxes[0].InvokeCallback(0, null);
        Assert.That(h.Log[^1], Is.EqualTo("move 3"));
    }

    [Test] public void BlueprintModeNeedsTheFeatureThenEntersMode5()
    {
        var (c, h, _, boxes, _, _) = Make();
        h.Blueprints = false;
        c.ToggleBlueprintMode();
        Assert.That(h.Log, Is.EqualTo(new[] { "bp True" }));
        h.Log.Clear(); h.Blueprints = true;
        c.ToggleBlueprintMode();
        Assert.That(h.Log, Is.EqualTo(new[] { "bp True", "move 5" }));
        h.Log.Clear(); h.PlotMode = 3; h.DesignHasChanges = true;
        c.ToggleBlueprintMode();
        Assert.That(boxes[0].Msg, Is.EqualTo("s_home_info_exit_blueprint_messagebox_save"));
    }

    [Test] public void DemolishAsksThenSendsOnYes()
    {
        var (c, h, _, boxes, _, _) = Make();
        c.OnCallback("OnHomeDemolish", null);
        var box = boxes[0];
        Assert.That(box.Type, Is.EqualTo(0x1f));
        Assert.That(box.Msg, Is.EqualTo("common1762"));
        Assert.That(box.Check0, Is.EqualTo("common2513"));
        Assert.That(box.Width, Is.EqualTo(0x1c0));
        Assert.That(box.Height, Is.EqualTo(400));
        Assert.That(h.Log, Is.Empty, "nothing before the answer");
        box.InvokeCallback(1, null);
        Assert.That(h.Log, Is.Empty);
        box.InvokeCallback(0, null);
        Assert.That(h.Log, Is.EqualTo(new[] { "demolish" }));
    }

    [Test] public void DemolishRunsThePreludeOnlyWithFeature0xd4()
    {
        var (c, h, _, _, _, _) = Make();
        c.SendRequestCube(0);
        Assert.That(h.Log, Is.EqualTo(new[] { "demolish" }));
        h.Log.Clear(); h.Features.Add(0xd4);
        c.SendRequestCube(0);
        Assert.That(h.Log, Is.EqualTo(new[] { "prelude", "demolish" }));
    }

    [Test] public void BlueprintExportNeedsCubes()
    {
        var (c, h, _, boxes, _, _) = Make();
        c.OnCallback("OnBlueprintExport", null);
        Assert.That(h.Log, Is.EqualTo(new[] { "bp True" }));
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Msg, Is.EqualTo("common3002"), "common 0xbba");
        h.CubeCount = 3; h.Log.Clear();
        c.OnCallback("OnBlueprintExport", null);
        Assert.That(h.Log, Is.EqualTo(new[] { "bp True", "toggle 4f7b" }));
    }

    [Test] public void FreeCameraCallFlipsTheFlag()
    {
        var (c, h, _, _, _, _) = Make();
        c.OnCallback("OnFreeCamera", null);
        Assert.That(c.FreeCamera, Is.True);
        c.OnCallback("OnFreeCamera", null);
        Assert.That(c.FreeCamera, Is.False);
        Assert.That(h.Log, Is.EqualTo(new[] { "camera True", "camera False" }));
    }

    [Test] public void UnknownCallsAreNotHandled()
    {
        var (c, _, _, _, _, _) = Make();
        Assert.That(c.OnCallback("Nothing", null), Is.False);
    }

    [Test] public void IdAndNames()
    {
        var (c, _, _, _, _, _) = Make();
        Assert.That(HomeMenuDialogController.UiId, Is.EqualTo(0x4f7c));
        Assert.That(c.TypeName, Is.EqualTo("HomeMenuDialog"));
        Assert.That(c.Movie, Is.EqualTo("uihomemenudialog"));
    }

    [Test] public void DesignLoadSetsTheLoadModeOfTheSlotDialog()
    {
        var (c, h, _, _, _, _) = Make();
        c.OnCallback("ondesignload", null);
        Assert.That(h.Log, Is.EqualTo(new[] { "slots load" }));
    }

    [Test] public void BlueprintImportNeedsBlueprintsAvailable()
    {
        var (c, h, _, _, _, _) = Make();
        h.Blueprints = false;
        c.OnCallback("OnBlueprintImport", null);
        Assert.That(h.Log, Is.EqualTo(new[] { "bp True" }));
        h.Log.Clear(); h.Blueprints = true;
        c.OnCallback("OnBlueprintImport", null);
        Assert.That(h.Log, Is.EqualTo(new[] { "bp True", "slots load" }));
    }

    /// <summary>The ctor's SetFlag(0, 1) (uihomemenudialog.cpp:225) with no vfunc_13 of its own: UIDialog's passes Esc on.</summary>
    [Test] public void TheHomeMenuIsAlwaysOnAndPassesEsc()
    {
        var (c, _, _, _, _, _) = Make();
        Assert.That(c.AlwaysOn, Is.True);
        Assert.That(c.EscapeKey(), Is.False, "Esc goes on to the field");
    }
}

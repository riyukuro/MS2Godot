using Ms2.Net.Wire;
using Ms2.Net.Packets;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The HUD movies run by their own ActionScript and fed by the Ms2.Ui controllers (needs MS2_DATA).</summary>
public class HudTests
{
    static PackSet packs;
    static KeyTable keys;
    static MovieSource src;
    static GameTables tables;

    static UiModel Model()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
            keys = new KeyTable(packs, "en");
        }
        var m = new UiModel(tables) { Keys = keys };
        UiFixture.Apply(m);
        m.ServerTime = () => new DateTime(2026, 1, 1, 9, 5, 0);
        return m;
    }

    static UiHost Load(UiController c, UiModel m)
    {
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        TestContext.WriteLine($"{c.Movie}: calls [{string.Join(", ", h.Calls)}] unhandled [{string.Join(", ", h.Unhandled)}]");
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        foreach (var (k, n) in rt.Missing) TestContext.WriteLine($"  missing: {k} x{n}");
        return h;
    }

    static string Text(UiHost h, string name, AsDisplayObject from = null) => (h.FindNamed(name, from) as AsTextField)?.Text;

    [Test]
    public void FeatureIdsGoThroughTheFeatureTable()
    {
        // CFeatureManager::IsFeatureEnabled: 0 off, 1 on, other ids by their name (FeatureTable)
        var m = new UiModel(GameTables.Empty) { FeatureEnabledByName = n => n == "SuperWorldChat" };
        Assert.That(m.IsFeatureEnabled(0), Is.False);
        Assert.That(m.IsFeatureEnabled(1), Is.True);
        Assert.That(m.IsFeatureEnabled(0x19), Is.True);
        Assert.That(m.IsFeatureEnabled(0x18), Is.False);
        Assert.That(m.IsFeatureEnabled(0x118), Is.False);
        // without the table, the ids the host listed
        Assert.That(new UiModel(GameTables.Empty).IsFeatureEnabled(Feature.GamePad), Is.True);
    }

    [Test]
    public void TablesReadNextExpAndJobNames()
    {
        var m = Model();
        Assert.That(m.Tables.NextExp(1), Is.GreaterThan(0));
        Assert.That(m.Tables.NextExp(21), Is.GreaterThan(m.Tables.NextExp(1)));
        Assert.That(m.Tables.JobName(100), Is.EqualTo("Knight"));
        Assert.That(m.Tables.Common("s_tooltip_rest_exp"), Does.Contain("{1:.2f}"));
    }

    [Test]
    public void ScreenMaskLoadsTheFieldMaskOverTheStage()
    {
        // UIScreenMask::OnLoadInit invokes load(bgMask); the movie adds the loaded movie on complete, stretched to the stage
        // (loadComplete, setSize: the loader one unit past the stage); vfunc_43 sizes it to the stage
        var h = Load(new ScreenMaskController("BG_mask_shadow.swf"), Model());
        var loader = ((AsContainer)h.Entity).Children.OfType<AsLoader>().SingleOrDefault();
        Assert.That(loader, Is.Not.Null, "m_loader, added on complete");
        Assert.That(loader.Children, Is.Not.Empty, "the bgMask movie's root");
        Assert.That(Rt.ToNumber(h.Get(loader, "width")), Is.EqualTo(1921).Within(0.5));
        Assert.That(Rt.ToNumber(h.Get(loader, "height")), Is.EqualTo(1081).Within(0.5));
        Assert.That(h.Player.RT.Errors, Is.Empty);

        // a field without a bgMask: the movie loads and shows nothing
        var none = Load(new ScreenMaskController(""), Model());
        Assert.That(((AsContainer)none.Entity).Children, Is.Empty);
    }

    [Test]
    public void ExpBarShowsPercentAndAnswersGetExp()
    {
        var m = Model();
        var c = new ExpBarController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(Text(h, "textField", h.Child("m_point") as AsDisplayObject), Is.EqualTo("38.00%"));
        // vfunc_43: the component's size is the stage width (its m_bg reflows to it) and alignment centres it
        var bg = h.Child("m_bg");
        Assert.That(Rt.ToNumber(h.Get(bg, "width")), Is.EqualTo(1920).Within(1));
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), w = Rt.ToNumber(h.Get(h.Entity, "width"));
        Assert.That(x + w / 2, Is.EqualTo(960).Within(1));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo(1080 - 21).Within(1));
        var ud = c.GetUserData("getExp", Array.Empty<object>());
        Assert.That((string)ud["text"], Does.StartWith($"{m.Player.Exp:N0} / {m.NextLevelExp:N0}".Replace(' ', ',')));
        Assert.That((string)ud["text"], Does.Contain("Level 21"));
        // status event 3 redraws
        m.Player.Exp = m.NextLevelExp / 2; m.Notify(StatusEvent.Exp); h.Player.Tick();
        Assert.That(Text(h, "textField", h.Child("m_point") as AsDisplayObject), Is.EqualTo("50.00%"));
    }

    [Test]
    public void AdventureLevelExpBarPointAndLayout()
    {
        var m = Model();
        m.Features.Add(AdventureLevelExpBarController.Feature);
        m.Player.Level = 60;
        m.Player.AdventureExp = m.Tables.AdventureLevelLvUpExp / 4;
        var c = new AdventureLevelExpBarController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // setPoint(25, ...): the movie's own label text
        Assert.That(Text(h, "textField", h.Child("m_point") as AsDisplayObject), Is.EqualTo("25.00 %"));
        // vfunc_43: stage width, alignment puts it right over the exp bar (setExpBarHeight 19)
        double y = Rt.ToNumber(h.Get(h.Entity, "y"));
        Assert.That(y, Is.EqualTo(1080 - 19 - 21).Within(1));
        var ud = c.GetUserData("getExp", Array.Empty<object>());
        Assert.That((string)ud["text"], Does.Contain($"{m.Player.AdventureExp:N0} / {m.Tables.AdventureLevelLvUpExp:N0}"));
    }

    [Test]
    public void MainPlayerInfoTakesTheServerKeyTable()
    {
        var m = Model();
        // the player's saved bindings: slot 0's action on key 30 (A) and slot 4's on key 16 (Q); page 2 of 3 is current
        var bindings = new[] { new KeyBinding(30, 1, 500001, 0, 0), new KeyBinding(16, 1, 500005, 0, 0) };
        var pages = new IReadOnlyList<(int, QuickSlotEntry)>[] { Array.Empty<(int, QuickSlotEntry)>(), new[] { (3, new QuickSlotEntry(10000001, 0, 0)) }, Array.Empty<(int, QuickSlotEntry)>() };
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, bindings, 1, pages)));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var c = new MainPlayerInfoController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.ShortkeyPage, Is.EqualTo(2));
        var slot0 = (AsObject)h.ToAs(c.GetBinding("SlotInfo.skill.0.0"));
        Assert.That(Rt.ToString(h.Get(slot0, "shortcutKey")), Is.EqualTo("A"));
        Assert.That(Rt.ToString(h.Get((AsObject)h.ToAs(c.GetBinding("SlotInfo.skill.0.4")), "shortcutKey")), Is.EqualTo("Q"));
        Assert.That(Rt.ToString(h.Get((AsObject)h.ToAs(c.GetBinding("SlotInfo.skill.0.1")), "shortcutKey")), Is.EqualTo(""));   // cleared, not in the packet
        // the filled slot 3 of the current page: skill 10000001's icon (not learnt: no skill book, so gray)
        var slot3 = (AsObject)h.ToAs(c.GetBinding("SlotInfo.skill.0.3"));
        TestContext.WriteLine(Rt.ToString(h.Get(slot3, "iconPath")));
        Assert.That(Rt.ToString(h.Get(slot3, "iconPath")), Is.EqualTo(m.Tables.Skills.GetSkillData(10000001)?.Icon ?? ""));
        Assert.That(Rt.ToNumber(h.Get(slot3, "grayColor")), Is.EqualTo(1));
    }

    [Test]
    public void KeyTableSyncsBindingsAndLoadsMousePreset()
    {
        Model(); // loads the shared packs (or ignores the test without MS2_DATA); the test must not rely on another test having run first
        var kt = new KeyTable(packs, "en");
        var sentSingle = new List<KeyBinding>();
        var sentAll = new List<IReadOnlyList<KeyBinding>>();
        kt.KeyBindingChanged += b => sentSingle.Add(b);
        kt.KeysSentToServer += list => sentAll.Add(list);

        // SetKeyBindingAndSync updates pair key (LShift 42 <-> RShift 54) and emits mode 1 for both (keytable.cpp:703-714)
        Assert.That(kt.SetKeyBindingAndSync(42, 1, 500001, 0, 0), Is.True);
        Assert.That(kt.IsDirty, Is.True);
        Assert.That(sentSingle.Select(b => b.Key), Is.EqualTo(new[] { 42, 54 }));

        // Locked suppresses outbound mode 1 sends
        sentSingle.Clear();
        kt.Locked = true;
        Assert.That(kt.SetKeyBindingAndSync(42, 1, 500002, 0, 0), Is.True);
        Assert.That(sentSingle, Is.Empty);

        // Unlock and load mouse preset with sendToServer: emits mode 2 with all bindings (keytable.cpp:429-496)
        kt.Locked = false;
        kt.LoadDefaultKeys(KeyTable.DefaultMouseControlKeyPath, sendToServer: true);
        Assert.That(sentAll, Has.Count.EqualTo(1));
        Assert.That(sentAll[0].Count, Is.GreaterThan(0));
        Assert.That(sentAll[0].Count, Is.EqualTo(kt.SnapshotBindings().Count));

        // InitKeys (keytable.cpp:632-652): 112 gamepad combine keys are present in m_keys / mode 2 payload
        int comboCount = sentAll[0].Count(b => b.Key >= 0xf00000);
        Assert.That(comboCount, Is.EqualTo(112));
        Assert.That(kt.GetKeyName(KeyTable.MakeCombineKey(0x96, 0x92, 0xf0)), Does.Contain(","));

        // OnKeyTable mode 0 backs up bindings (keytable.cpp:349) so RestoreKeysFromBackup restores them
        var st = new ServerKeyTable();
        st.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, new[] { new KeyBinding(30, 1, 500001, 0, 0) }, 0, Array.Empty<IReadOnlyList<(int, QuickSlotEntry)>>())));
        kt.Apply(st);
        Assert.That(kt.FindKeyByActionAndPriority(500001, 0), Is.EqualTo(30));
        kt.SetBinding(30, 1, 500002, 0, 0);
        Assert.That(kt.FindKeyByActionAndPriority(500001, 0), Is.EqualTo(0));
        kt.RestoreKeysFromBackup();
        Assert.That(kt.FindKeyByActionAndPriority(500001, 0), Is.EqualTo(30));
    }

    /// <summary>The client's player-info object holds curHP/maxHP as 64-bit values (ASObj AddInt64, uimainplayerinfo.cpp:809-810): a long member
    /// of the init object reaches the movie whole (the controller used to cast it to uint, which cut it at 2^32). The movie does not keep the
    /// member under its name, so this checks the value object the controller builds and sends.</summary>
    [Test]
    public void AHealthAboveTwoToThe32KeepsItsValueInTheInitObject()
    {
        var m = Model();
        var h = Load(new MainPlayerInfoController(), m);
        var o = h.Obj(("curHP", 6_000_000_000L), ("maxHP", 9_000_000_000L));
        Assert.That(Rt.ToNumber(h.Get(o, "curHP")), Is.EqualTo(6_000_000_000d));
        Assert.That(Rt.ToNumber(h.Get(o, "maxHP")), Is.EqualTo(9_000_000_000d));
        Assert.That(Rt.ToNumber(h.Get(h.Obj(("curHP", unchecked((uint)6_000_000_000L))), "curHP")), Is.Not.EqualTo(6_000_000_000d), "the old (uint) cast cut it");
    }

    [Test]
    public void MainPlayerInfoPageNavigationWrapsAndEmitsSetPage()
    {
        var m = Model();
        var pages = new IReadOnlyList<(int, QuickSlotEntry)>[]
        {
            new[] { (0, new QuickSlotEntry(10000001, 0, 0)) },
            Array.Empty<(int, QuickSlotEntry)>(),
            Array.Empty<(int, QuickSlotEntry)>(),
        };
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 1, pages)));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var c = new MainPlayerInfoController();
        var sentPages = new List<int>();
        c.QuickSlotPageChanged += p => sentPages.Add(p);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.ShortkeyPage, Is.EqualTo(2));

        // OnNextPage wraps 1 -> 2 -> 0, OnPrevPage wraps 0 -> 2 (uimainplayerinfo.cpp:1486-1513)
        c.OnCallback("onNextPage", Array.Empty<object>());
        Assert.That(t.CurrentPage, Is.EqualTo(2));
        Assert.That(c.ShortkeyPage, Is.EqualTo(3));

        c.OnCallback("onNextPage", Array.Empty<object>());
        Assert.That(t.CurrentPage, Is.EqualTo(0));
        Assert.That(c.ShortkeyPage, Is.EqualTo(1));

        c.OnCallback("onPrevPage", Array.Empty<object>());
        Assert.That(t.CurrentPage, Is.EqualTo(2));
        Assert.That(c.ShortkeyPage, Is.EqualTo(3));

        Assert.That(sentPages, Is.EqualTo(new[] { 2, 0, 2 }));

        // Skill-set guard (FUN_140930d70, uimainplayerinfo.cpp:1488-1505) blocks page switches
        c.HasSkillSet = () => true;
        c.OnCallback("onNextPage", Array.Empty<object>());
        c.OnCallback("onPrevPage", Array.Empty<object>());
        Assert.That(t.CurrentPage, Is.EqualTo(2));
        Assert.That(sentPages, Is.EqualTo(new[] { 2, 0, 2 }));

        // Loaded table with PageCount == 0 returns without altering ShortkeyPage (uimainplayerinfo.cpp:1491,1507)
        c.HasSkillSet = null;
        var emptyTable = new ServerKeyTable();
        emptyTable.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0, Array.Empty<IReadOnlyList<(int, QuickSlotEntry)>>())));
        m.ServerKeys = emptyTable;
        int pageBefore = c.ShortkeyPage;
        c.OnCallback("onNextPage", Array.Empty<object>());
        c.OnCallback("onPrevPage", Array.Empty<object>());
        Assert.That(c.ShortkeyPage, Is.EqualTo(pageBefore));
        Assert.That(sentPages, Is.EqualTo(new[] { 2, 0, 2 }));
    }

    [Test]
    public void UpperHudInputModeButtonTogglesKeySettingSelect()
    {
        var m = Model();
        m.Features.Add(Feature.InputMode);
        var c = new UpperHudController { MouseMode = false };
        int toggles = 0;
        c.ToggleKeySettingSelect += () => toggles++;
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);

        var kb = h.Child("m_keyboardMode");
        var ms = h.Child("m_mouseMode");
        Assert.That(kb, Is.Not.Null);
        Assert.That(ms, Is.Not.Null);
        Assert.That(Rt.ToBoolean(h.Get(kb, "visible")), Is.True);
        Assert.That(Rt.ToBoolean(h.Get(ms, "visible")), Is.False);

        var evCls = h.RT.GetClass("flash.events.Event");
        h.Player.Call(kb, "dispatchEvent", new object[] { evCls.Construct("buttonClick", false, false) });
        Assert.That(toggles, Is.EqualTo(1));

        c.SetMouseMode(true);
        Assert.That(Rt.ToBoolean(h.Get(kb, "visible")), Is.False);
        Assert.That(Rt.ToBoolean(h.Get(ms, "visible")), Is.True);
        h.Player.Call(ms, "dispatchEvent", new object[] { evCls.Construct("buttonClick", false, false) });
        Assert.That(toggles, Is.EqualTo(2));
    }

    // UIUpperHudDialog::SetGamePadMode / OnGamePadOnClick / OnGamePadOffClick (uiupperhuddialog.cpp:525-534, 630-638)
    [Test]
    public void UpperHudGamePadButtonsFollowTheModeAndAskForTheOther()
    {
        var m = Model();
        var c = new UpperHudController { GamePadMode = false };
        var asked = new List<bool>();
        c.GamePadClick += use => asked.Add(use);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        var on = h.Child("m_gamePadOn");
        var off = h.Child("m_gamePadOff");
        Assert.That(Rt.ToBoolean(h.Get(on, "visible")), Is.False);
        Assert.That(Rt.ToBoolean(h.Get(off, "visible")), Is.True);

        c.SetGamePadMode(true);
        Assert.That(Rt.ToBoolean(h.Get(on, "visible")), Is.True);
        Assert.That(Rt.ToBoolean(h.Get(off, "visible")), Is.False);

        var ev = h.RT.GetClass("flash.events.Event");
        h.Player.Call(on, "dispatchEvent", new object[] { ev.Construct("buttonClick", false, false) });
        h.Player.Call(off, "dispatchEvent", new object[] { ev.Construct("buttonClick", false, false) });
        Assert.That(asked, Is.EqualTo(new[] { false, true }));

        m.Features.Remove(Feature.GamePad);
        c.SetGamePadMode(false);
        Assert.That(Rt.ToBoolean(h.Get(on, "visible")), Is.True, "without the pad feature the mode is stored, the buttons stay");
    }

    [Test]
    public void KeySettingSelectDialogSwitchesPresetsAndRespectsLock()
    {
        var m = Model();
        m.Keys = new KeyTable(packs, "en");
        var sentAll = new List<IReadOnlyList<KeyBinding>>();
        m.Keys.KeysSentToServer += list => sentAll.Add(list);
        var selectedModes = new List<bool>();
        var c = new KeySettingSelectController();
        c.ModeSelected += mouse => selectedModes.Add(mouse);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);

        var btn1 = h.Child("m_button1");
        var btn2 = h.Child("m_button2");
        Assert.That(btn1, Is.Not.Null);
        Assert.That(btn2, Is.Not.Null);
        Assert.That(Rt.ToBoolean(h.Get(btn1, "enabled")), Is.True);
        Assert.That(Rt.ToBoolean(h.Get(btn2, "enabled")), Is.True);

        // Clicking m_button2 selects mouse mode, loads defaultmousecontrolkey.xml and sends mode 2 (uikeysettingselectdialog.cpp:96-122)
        h.Player.Call(btn2, "dispatchEvent", new object[] { h.RT.GetClass("flash.events.Event").Construct("buttonClick", false, false) });
        Assert.That(selectedModes, Is.EqualTo(new[] { true }));
        Assert.That(sentAll, Has.Count.EqualTo(1));
        Assert.That(sentAll[0].Count, Is.GreaterThan(0));

        // Reopen and click m_button1 -> keyboard mode
        var c2 = new KeySettingSelectController();
        c2.ModeSelected += mouse => selectedModes.Add(mouse);
        var h2 = Load(c2, m);
        Assert.That(h2.RT.Errors, Is.Empty);
        h2.Player.Call(h2.Child("m_button1"), "dispatchEvent", new object[] { h2.RT.GetClass("flash.events.Event").Construct("buttonClick", false, false) });
        Assert.That(selectedModes, Is.EqualTo(new[] { true, false }));
        Assert.That(sentAll, Has.Count.EqualTo(2));

        // Locked key table disables both buttons (uikeysettingselectdialog.cpp:68-74)
        m.Keys.Locked = true;
        var c3 = new KeySettingSelectController();
        var h3 = Load(c3, m);
        Assert.That(h3.RT.Errors, Is.Empty);
        Assert.That(Rt.ToBoolean(h3.Get(h3.Child("m_button1"), "enabled")), Is.False);
        Assert.That(Rt.ToBoolean(h3.Get(h3.Child("m_button2"), "enabled")), Is.False);
    }

    [Test]
    public void QuickSlotTooltipsFollowTheSlotKind()
    {
        // SkillSlot::vfunc_0 (skillslot.cpp:716): a skill slot's skill tooltip with the remove hint, a key-bound slot's action
        // label, nothing for an empty slot
        var m = Model();
        m.HasLocalPlayer = true;   // vfunc_0 shows a skill or item tooltip only with a local PC
        var pages = new IReadOnlyList<(int, QuickSlotEntry)>[] { new[] { (3, new QuickSlotEntry(10500001, 0, 0)), (4, new QuickSlotEntry(0, 11300451, 77)) } };   // Arrow Stream; an item not held
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0, pages)));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var c = new MainPlayerInfoController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        string hint = m.Tables.Common("s_msg_rclick_putoff_qslot");
        Assert.That(hint, Is.Not.Empty);
        var skill = c.GetSlotTooltip(MainPlayerInfoController.SlotBag + ".3");
        Assert.That(skill, Is.Not.Null.And.Not.Empty);
        Assert.That(skill["enablePuton"], Is.EqualTo(hint));
        var key = c.GetSlotTooltip(MainPlayerInfoController.SlotBag + ".22");
        Assert.That(key["text"], Is.EqualTo(m.Tables.Common("s_quick_slot_item_get")));
        Assert.That(key["tooltipAlign"], Is.EqualTo(5));
        Assert.That(c.GetSlotTooltip(MainPlayerInfoController.SlotBag + ".1"), Is.Empty);
        Assert.That(c.GetSlotTooltip("SlotInfo.other.1"), Is.Null);
        // an item id whose item is gone (:719): FUN_1409e1770 at the data's rank, flags 0x40 (align 7), with the remove hint
        var gone = c.GetSlotTooltip(MainPlayerInfoController.SlotBag + ".4");
        Assert.That(gone["tooltip"], Is.EqualTo("export_common_tooltip_kindsItem"));
        Assert.That(gone["itemName"], Is.EqualTo(m.Tables.ItemName(11300451)));
        Assert.That(gone["itemGrade"], Is.EqualTo(m.Tables.Items.Get(11300451).Rank is var r && r != 0 ? r : 1));
        Assert.That(gone["tooltipAlign"], Is.EqualTo(7));
        Assert.That(gone["enablePuton"], Is.EqualTo(hint));
    }

    [Test]
    public void AchieveItemTooltipIsTheTooltipById()
    {
        // UIAchieveDialog::vfunc_49 (@0x1418a8690): ASTooltip(player, 0x20, 2).FUN_1409e1770(id, GetItemRank(id))
        var m = Model();
        var book = new AchieveBook(new Dictionary<int, AchieveData>(), Array.Empty<AchieveData>());
        var c = new AchieveController(book, new AchieveTexts(new(), new()) { Data = book.DataOf, Find = book.Find },
            new AchieveCategoryTable(new List<AchieveCategory>()));
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        var tip = c.GetTooltip(new object[] { 11300451u });
        Assert.That(tip["itemName"], Is.EqualTo(m.Tables.ItemName(11300451)));
        Assert.That(tip["itemGrade"], Is.EqualTo(Ms2.Ui.Items.ItemTooltip.ItemRank(m.Tables.Items, 11300451)));
        Assert.That(tip["tooltipAlign"], Is.EqualTo(5));   // flags 0x20: neither 0x40 nor 2
        // an id the data has no item for (GMS2 has an item 1)
        uint missing = (uint)Enumerable.Range(1, 1000).First(i => m.Tables.Items.Get(i) == null);
        Assert.That(c.GetTooltip(new object[] { missing }), Is.Null);
    }

    [Test]
    public void QuickSlotsSwapByDragAndEmptyByRightClick()
    {
        // SkillSlot::vfunc_18 (skillslot.cpp:507) from another SkillSlot: FUN_14092f690 of the source's entry on the target
        // (KeyTable mode 3), then the two swap; the right click (NotifyDragSlot::SlotUse button 1, vfunc_14 -> vfunc_17 :463)
        // sends mode 5 (SendKeyTableMode5) and empties the slot
        var m = Model();
        var pages = new IReadOnlyList<(int, QuickSlotEntry)>[] { new[] { (3, new QuickSlotEntry(10500001, 0, 0)), (5, new QuickSlotEntry(10500002, 0, 0)) } };
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0, pages)));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var c = new MainPlayerInfoController();
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        c.Send = sent.Add;
        var h = Load(c, m);
        string s3 = MainPlayerInfoController.SlotBag + ".3", s5 = MainPlayerInfoController.SlotBag + ".5";
        Assert.That(c.IsDragSlot(s3), Is.True);
        Assert.That(c.IsDragSlot("SlotInfo.other.1"), Is.False);

        c.SlotDroppedOn(s5, s3, c);
        Assert.That(sent, Is.EqualTo(new[] { new KeyTableSetQuickSlotRequest(0, 10500001, 0, 0, 5) }));
        Assert.That((c.ShownEntry(3), c.ShownEntry(5)), Is.EqualTo((new QuickSlotEntry(10500002, 0, 0), new QuickSlotEntry(10500001, 0, 0))));
        // outside a skill set the page is not written (the server's KeyTable mode 0 / 7 reloads it)
        Assert.That((t.Entry(0, 3), t.Entry(0, 5)), Is.EqualTo((new QuickSlotEntry(10500001, 0, 0), new QuickSlotEntry(10500002, 0, 0))));

        // button 0 (vfunc_13): the slot's key is pressed (FUN_141bc9fd0), else its skill used; with shift held, nothing
        var pressed = new List<int>();
        var used = new List<uint>();
        c.PressKey = pressed.Add;
        c.UseSkill = used.Add;
        c.ShiftHeld = () => true;
        Assert.That(c.OnCallback("notifySlotUse", new object[] { s3, 0.0 }), Is.True);
        Assert.That(pressed.Count + used.Count, Is.Zero);
        c.ShiftHeld = () => false;
        Assert.That(c.OnCallback("notifySlotUse", new object[] { s3, 0.0 }), Is.True);
        int key3 = m.Keys.FindKeyByQuickSlot(3, 0);
        Assert.That(key3, Is.Not.Zero);
        Assert.That(pressed, Is.EqualTo(new[] { key3 }));
        Assert.That(sent, Has.Count.EqualTo(1), "a click sends no edit");
        Assert.That(c.OnCallback("notifySlotUse", new object[] { s3, 1.0 }), Is.True);
        Assert.That(sent[1], Is.EqualTo(new KeyTableRemoveQuickSlotRequest(0, 10500002, 0)));
        Assert.That(c.ShownEntry(3), Is.EqualTo(default(QuickSlotEntry)));
        Assert.That(t.Entry(0, 3), Is.EqualTo(new QuickSlotEntry(10500001, 0, 0)), "no skill set: the page is not written");
        Assert.That(Rt.ToString(h.Get((AsObject)h.ToAs(c.GetBinding(s3)), "iconPath")), Is.EqualTo(""));
        c.OnCallback("notifySlotUse", new object[] { s3, 1.0 });                         // empty now: no send
        c.OnCallback("notifySlotUse", new object[] { MainPlayerInfoController.SlotBag + ".22", 1.0 });   // key bound: no send
        Assert.That(sent, Has.Count.EqualTo(2));
    }

    [Test]
    public void AClickedOrRightClickedSlotStartsItsIconTweenUnlessInputIsBlocked()
    {
        // SkillSlot::FUN_14175f200 (skillslot.cpp:902): vfunc_13 (the click, :401) and vfunc_14 (the right click, after the fixed
        // or bound check, :458) call the slot's startIconTween (bound in vfunc_9, :676) unless FUN_14116d7a0(0xf) blocks input;
        // BaseSlot.startIconTween runs m_iconTween, which shrinks the icon's contentCanvas to 0.8 in 100 ms, then m_iconTween2
        // grows it back in 100 ms
        var m = Model();
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0,
            new IReadOnlyList<(int, QuickSlotEntry)>[] { new[] { (3, new QuickSlotEntry(10500001, 0, 0)) } })));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        bool blocked = false;
        var c = new MainPlayerInfoController { InputBlocked = () => blocked, Send = _ => { } };
        var h = Load(c, m);
        double Scale(int slot)
        {
            var o = c.SlotObject(slot);
            Assert.That(o, Is.InstanceOf<AsObject>(), "slot " + slot + " bound");
            return Rt.ToNumber(h.Get(h.Get(o, "contentCanvas"), "scaleX"));
        }
        void Frames(int n) { for (int i = 0; i < n; i++) h.Player.Tick(); }
        bool Shrinks(int slot)
        {
            Frames(3);
            bool shrunk = Scale(slot) < 0.99;
            Frames(30);                                                      // both tweens done
            Assert.That(Scale(slot), Is.EqualTo(1).Within(1e-6), "slot " + slot + " back to full size");
            return shrunk;
        }
        Frames(30);
        foreach (int slot in new[] { 1, 3, 22 }) Assert.That(Scale(slot), Is.EqualTo(1).Within(1e-6), "slot " + slot + " at rest");

        blocked = true;
        c.UseSlot(3);
        Assert.That(Shrinks(3), Is.False, "input blocked: no tween on the click");
        blocked = false;
        c.UseSlot(3);
        Assert.That(Shrinks(3), Is.True, "the click runs the tween");

        blocked = true;
        c.RemoveSlot(1);
        Assert.That(Shrinks(1), Is.False, "input blocked: no tween on the right click");
        blocked = false;
        c.RemoveSlot(1);                                                     // an empty slot: the tween still runs
        Assert.That(Shrinks(1), Is.True, "the right click runs the tween");
        c.RemoveSlot(22);                                                    // key bound (FUN_141761990): returns first
        Assert.That(Shrinks(22), Is.False, "a key bound slot has no tween");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ASkillSetSwapWritesThePageAndThePageNotifyReloadsTheSlots()
    {
        // SkillSlot::vfunc_18 (skillslot.cpp:516-545): the swap writes the page (SetQuickSlotEntry) only with a skill set
        // (FUN_140930d70); UIMainPlayerInfo::OnShortkeyPageNotify (uimainplayerinfo.cpp:997, UpdateQuickSlots :1209) after
        // OnKeyTable mode 7 (keytable.cpp:285-370) reloads the slots from the new page
        var m = Model();
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0,
            new IReadOnlyList<(int, QuickSlotEntry)>[] {
                new[] { (3, new QuickSlotEntry(10500001, 0, 0)), (5, new QuickSlotEntry(10500002, 0, 0)) },
                new[] { (3, new QuickSlotEntry(10500003, 0, 0)) } })));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        bool skillSet = true;
        var c = new MainPlayerInfoController { SlotWorld = () => new Ms2.Ui.Skills.SlotWorld { Tables = m.Tables.Skills, HasPc = true, SkillSetActive = skillSet } };
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        c.Send = sent.Add;
        Load(c, m);
        string s3 = MainPlayerInfoController.SlotBag + ".3", s5 = MainPlayerInfoController.SlotBag + ".5";

        c.SlotDroppedOn(s5, s3, c);
        Assert.That(sent, Is.Empty, "a skill set sends no mode 3");
        Assert.That((t.Entry(0, 3), t.Entry(0, 5)), Is.EqualTo((new QuickSlotEntry(10500002, 0, 0), new QuickSlotEntry(10500001, 0, 0))));
        Assert.That((c.ShownEntry(3), c.ShownEntry(5)), Is.EqualTo((new QuickSlotEntry(10500002, 0, 0), new QuickSlotEntry(10500001, 0, 0))));

        skillSet = false;
        c.SlotDroppedOn(s3, s5, c);                                           // slot 5 (10500001) back on slot 3
        Assert.That(sent, Is.EqualTo(new[] { new KeyTableSetQuickSlotRequest(0, 10500001, 0, 0, 3) }));
        Assert.That((c.ShownEntry(3), c.ShownEntry(5)), Is.EqualTo((new QuickSlotEntry(10500001, 0, 0), new QuickSlotEntry(10500002, 0, 0))));
        Assert.That((t.Entry(0, 3), t.Entry(0, 5)), Is.EqualTo((new QuickSlotEntry(10500002, 0, 0), new QuickSlotEntry(10500001, 0, 0))));

        c.OnShortkeyPageNotify(0, 0);                                         // the same page: the slots take the page again
        Assert.That((c.ShownEntry(3), c.ShownEntry(5)), Is.EqualTo((new QuickSlotEntry(10500002, 0, 0), new QuickSlotEntry(10500001, 0, 0))));

        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(7, false, null, 1,
            new IReadOnlyList<(int, QuickSlotEntry)>[] { Array.Empty<(int, QuickSlotEntry)>(), new[] { (3, new QuickSlotEntry(10500003, 0, 0)) } })));
        c.OnShortkeyPageNotify(0, t.CurrentPage);
        Assert.That(c.ShortkeyPage, Is.EqualTo(2));
        Assert.That((c.ShownEntry(3), c.ShownEntry(5)), Is.EqualTo((new QuickSlotEntry(10500003, 0, 0), default(QuickSlotEntry))));
    }

    sealed class Bag(Dictionary<string, long> cells) : IInvenSlotOwner
    {
        public readonly List<string> Refreshed = new();
        public bool IsDragSlot(string name) => cells.ContainsKey(name);
        public long? InvenSlotUid(string name) => cells.TryGetValue(name, out var u) ? u : null;
        public Item FindItem(long uid) => null;
        public bool CanEquipItem(Item item, int slot, int layer) => false;
        public void RequestEquipItem(Item item, int slot, int layer) { }
        public void RefreshSlot(string name) => Refreshed.Add(name);
        public void SlotDropUnresolved(string name) { }
    }

    [Test]
    public void ABagPotionDroppedOnAQuickSlotIsSentAndKept()
    {
        // SkillSlot::vfunc_18 (skillslot.cpp:561-604) from an InvenSlot: the bag item of the slot's uid, then FUN_14092f690
        var m = Model();
        var t = new ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0,
            new IReadOnlyList<(int, QuickSlotEntry)>[] { Array.Empty<(int, QuickSlotEntry)>() })));
        m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var potion = ItemData.Load(20000001, System.Xml.Linq.XElement.Parse("<e><property type=\"2\" slotMax=\"100\"/></e>"));
        var c = new MainPlayerInfoController
        {
            SlotWorld = () => new Ms2.Ui.Skills.SlotWorld
            {
                Tables = m.Tables.Skills, ItemData = id => id == 20000001 ? potion : null, HasPc = true,
                Inventory = new[] { new EquipItem { Id = 20000001, Uid = 55 } },
            },
        };
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        c.Send = sent.Add;
        Load(c, m);
        var bag = new Bag(new() { ["InventoryDialog.0"] = 55, ["InventoryDialog.1"] = 0 });
        string s4 = MainPlayerInfoController.SlotBag + ".4";

        c.SlotDroppedOn(s4, "InventoryDialog.1", bag);                       // an empty cell: nothing
        Assert.That(sent, Is.Empty);
        c.SlotDroppedOn(s4, "InventoryDialog.0", bag);
        Assert.That(sent, Is.EqualTo(new[] { new KeyTableSetQuickSlotRequest(0, 0, 20000001, 55, 4) }));
        Assert.That(c.ShownEntry(4), Is.EqualTo(new QuickSlotEntry(0, 20000001, 55)));
        Assert.That(t.Entry(0, 4).IsEmpty, Is.True, "no skill set: the page is not written");
        Assert.That(bag.Refreshed, Is.EqualTo(new[] { "InventoryDialog.1", "InventoryDialog.0" }));
    }

    [Test]
    public void RemovingASlotPutsTheBagJobItemOfThatSlotBack()
    {
        // SkillSlot::vfunc_17 (skillslot.cpp:463): SendKeyTableMode5, whose CKeyTable::FUN_14142f470 (keytable.cpp:1976)
        // puts the category 10 job item of slot 8 (FUN_141432650: 20000028) back unless a shown slot holds it
        var m = Model();
        var job = ItemData.Load(20000028, System.Xml.Linq.XElement.Parse("<e><property type=\"2\" slotMax=\"1\"/></e>"));
        var potion = ItemData.Load(20000001, System.Xml.Linq.XElement.Parse("<e><property type=\"2\" slotMax=\"100\"/></e>"));
        bool skillSet = false;
        var c = new MainPlayerInfoController
        {
            SlotWorld = () => new Ms2.Ui.Skills.SlotWorld
            {
                Tables = m.Tables.Skills, HasPc = true, SkillSetActive = skillSet,
                ItemData = id => id == 20000028 ? job : id == 20000001 ? potion : null,
                TabOf = id => id == 20000028 ? 10 : 2,
                Inventory = new[] { new EquipItem { Id = 20000001, Uid = 55 }, new EquipItem { Id = 20000028, Uid = 77, InventorySlot = 3 } },
            },
        };
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        c.Send = sent.Add;
        ServerKeyTable Table(params (int, QuickSlotEntry)[] slots)
        {
            var t = new ServerKeyTable();
            t.Apply(new Maple2.PacketLib.Tools.ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0,
                new IReadOnlyList<(int, QuickSlotEntry)>[] { slots })));
            m.Keys = new KeyTable(packs, "en"); m.ServerKeys = t; m.Keys.Apply(t);
            return t;
        }

        var t = Table((8, new QuickSlotEntry(0, 20000001, 55)));
        Load(c, m);
        c.RemoveSlot(8);
        Assert.That(sent, Is.EqualTo(new Ms2.Net.Wire.IOutPacket[] { new KeyTableRemoveQuickSlotRequest(0, 0, 55),
            new KeyTableSetQuickSlotRequest(0, 0, 20000028, 77, 8) }));
        Assert.That(t.Entry(0, 8), Is.EqualTo(new QuickSlotEntry(0, 20000028, 77)), "the page keeps the job item");
        Assert.That(c.ShownEntry(8), Is.EqualTo(default(QuickSlotEntry)), "vfunc_11 empties the shown slot after the put back");
        c.RemoveSlot(8);                                                      // nothing shown: no send
        Assert.That(sent, Has.Count.EqualTo(2));

        sent.Clear();                                                         // the page notify reloads it; then the job item
        c.OnShortkeyPageNotify(0, 0);                                         // itself: the shown slot still holds it
        Assert.That(c.ShownEntry(8), Is.EqualTo(new QuickSlotEntry(0, 20000028, 77)));
        c.RemoveSlot(8);
        Assert.That(sent, Is.EqualTo(new[] { new KeyTableRemoveQuickSlotRequest(0, 0, 77) }));
        Assert.That(c.ShownEntry(8), Is.EqualTo(default(QuickSlotEntry)));

        sent.Clear();                                                         // another slot than the job slot: none
        t = Table((5, new QuickSlotEntry(0, 20000001, 55)));
        c.OnShortkeyPageNotify(0, 0);
        c.RemoveSlot(5);
        Assert.That(sent, Is.EqualTo(new[] { new KeyTableRemoveQuickSlotRequest(0, 0, 55) }));
        Assert.That(c.ShownEntry(5), Is.EqualTo(default(QuickSlotEntry)));
        Assert.That(t.Entry(0, 5), Is.EqualTo(new QuickSlotEntry(0, 20000001, 55)), "no skill set: the page is not written");

        sent.Clear();                                                         // a skill set: no send, so no put back; the page
        skillSet = true;                                                      // entry FindQuickSlotBySkillAndItem finds empties
        t = Table((8, new QuickSlotEntry(0, 20000001, 55)));
        c.OnShortkeyPageNotify(0, 0);
        c.RemoveSlot(8);
        Assert.That(sent, Is.Empty);
        Assert.That(t.Entry(0, 8), Is.EqualTo(default(QuickSlotEntry)));
        Assert.That(c.ShownEntry(8), Is.EqualTo(default(QuickSlotEntry)));
    }

    [Test]
    public void TheOwnStatusEventsReachThePartyFrame()
    {
        // NotifyStatusChanged (uistatuseventlistener.cpp:257) -> UpdateMemberHp / UpdateMemberJob / UpdateMemberLevel: the own member's
        // value changes and the window gets its flag (2 / 4 / 8)
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var seen = new List<(long Id, int Flags)>();
        book.MemberUpdated += (mem, f) => seen.Add((mem.CharacterId, f));
        var self = PartyFixture.SelfCharacterId;
        var p = m.Player;
        p.HpCur = 321; p.HpMax = 654; p.Level = 41; p.JobGroup = 20;
        PartyOwnStatus.Apply(book, self, p, StatusEvent.Hp);
        PartyOwnStatus.Apply(book, self, p, StatusEvent.Job);
        PartyOwnStatus.Apply(book, self, p, StatusEvent.Level);
        PartyOwnStatus.Apply(book, self, p, StatusEvent.Sp);        // not a party event
        Assert.That(seen, Is.EqualTo(new[] { (self, 2), (self, 4), (self, 8) }));
        var u = book.Find(self).User;
        Assert.That((u.Hp, u.MaxHp, u.Level, u.JobGroup), Is.EqualTo((321, 654, (short)41, 20)));
        var c = new PartyController { Party = book, SelfCharacterId = self };
        Assert.That(c.MemberObject(book.Find(self), 2)["hp"], Is.EqualTo(321));
        Assert.That(c.MemberObject(book.Find(self), 2)["maxhp"], Is.EqualTo(654));
        Assert.That(c.MemberObject(book.Find(self), 8)["level"], Is.EqualTo(41));
        // no party, or an id that is not a member: nothing
        seen.Clear();
        PartyOwnStatus.Apply(book, 999, p, StatusEvent.Hp);
        PartyOwnStatus.Apply(new PartyBook(), self, p, StatusEvent.Level);
        Assert.That(seen, Is.Empty);
    }

    [Test]
    public void TheMemberFrameShowsOfflineSleepAndTombStates()
    {
        // FillMemberObject (:975): state 0 offline (tombType 0), 2 the sleep state (m_184), else 1; tombType from the HP flags
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PartyController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId };
        var archer = book.Find(3);
        Assert.That(c.MemberObject(archer, 1)["state"], Is.EqualTo(1));
        book.Apply(new PartyMemberFlags(3, archer.User.AccountId, 1800, 0, 2), PartyFixture.SelfCharacterId, true);
        Assert.That(c.MemberObject(archer, 2)["tombType"], Is.EqualTo(2));
        Assert.That(c.MemberObject(archer, 2)["hp"], Is.EqualTo(0));
        archer.User.IsSleeping = true;
        Assert.That(c.MemberObject(archer, 1)["state"], Is.EqualTo(2));
        // offline wins over both: state 0 and the tomb hidden
        book.Apply(new PartyOffline(3), PartyFixture.SelfCharacterId, true);
        var off = c.MemberObject(archer, 3);
        Assert.That((off["state"], off["tombType"]), Is.EqualTo((0, 0)));
        // coming online again shows the frame as it was
        book.Apply(new PartyOnline(archer.User), PartyFixture.SelfCharacterId, true);
        Assert.That(c.MemberObject(archer, 3)["state"], Is.EqualTo(2));
        Assert.That(c.MemberObject(archer, 3)["tombType"], Is.EqualTo(2));
    }

    [Test]
    public void PartyWindowShowsTheFixtureParty()
    {
        // UIPartyDialog: RefreshAll's initMembers with each member's FillMemberObject, then the party packets' window calls
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PartyController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, MapName = id => id == 2000001 ? "Tria" : "Field" };
        book.MemberAdded += c.AddMember;
        book.MemberRemoved += c.RemoveMember;
        book.MemberUpdated += c.UpdateMember;
        book.ChiefChanged += c.ChangeChief;
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        for (int i = 0; i < 120; i++) h.Player.Tick();      // the movie adds the members one by one on a 100 ms timer
        AsObject Member(long id) => h.Invoke(h.Entity, "getMemberAni", id.ToString()) as AsObject;
        foreach (long id in new long[] { 2, 3, 4, 5 }) Assert.That(Member(id), Is.Not.Null, $"member {id}");
        AsObject Data(long id) => (AsObject)h.Get(Member(id), "object");
        Assert.That(Rt.ToBoolean(h.Get(Data(2), "chief")), Is.True);
        // the own member leads; the leader passes to member 3 (OnPartyLeaderChange -> changeChief)
        book.Apply(new PartyLeaderChange(3), PartyFixture.SelfCharacterId, true);
        Assert.That(Rt.ToBoolean(h.Get(Data(2), "chief")), Is.False);
        Assert.That(Rt.ToBoolean(h.Get(Data(3), "chief")), Is.True);
        // the channel members: member 4 is in another map and channel ("diff"), member 3 is with the own member
        var elsewhere = c.MemberObject(book.Find(4), 0x80);
        Assert.That(elsewhere["ch"], Is.EqualTo("diff"));
        Assert.That(elsewhere["chName"], Is.EqualTo(m.Tables.Common("s_party_ch").Replace("{0}", "1")));
        Assert.That(c.MemberObject(book.Find(3), 0x80)["ch"], Is.EqualTo(""));
        // member 4 leaves (removeMember): the movie takes it out of its map
        book.Apply(new PartyLeave(4, false), PartyFixture.SelfCharacterId, true);
        Assert.That(Member(4), Is.Null);
        Assert.That(h.RT.Errors, Is.Empty);
        // the tooltip: online with channel and map, offline
        string Tip(long id) => (string)c.GetUserData("getTooltip", new object[] { id.ToString() })?["text"];
        Assert.That(Tip(3), Is.EqualTo(m.Tables.Common("s_tooltip_party").Replace("{0}", m.Tables.Common("s_word_online"))
            .Replace("{1}", m.Tables.Common("s_tooltip_party_ch").Replace("{0}", m.Player.Channel.ToString()) + " Field")));
        Assert.That(Tip(5), Is.EqualTo(m.Tables.Common("s_word_offline")));
        // a right click on member 3's slot: Main.onHandleMouse turns the stage's click (buttonIdx 1) into rightClick, which
        // PartyDialog.OnRightClickParty sends as onPopupMenu(x, y, charSN); the plain click stops at Main's listener
        long menuFor = 0;
        c.MemberMenu += (x, y, id) => menuFor = id;
        var seen = new List<string>();
        foreach (var type in new[] { "click", "rightClick", "rightMouseDown", "rightMouseUp" })
            h.Player.Call(h.Player.Stage, "addEventListener", new object[] { type,
                new AsPlainFunction(h.RT, new NativeMethod(type, (_, a) => { seen.Add(type); return Rt.Undef; })), false, 0 });
        var slot = (AsDisplayObject)Member(3);
        var r = slot.GetBounds(null);
        (double X, double Y)? at = null;
        for (double y = r.Y + 2; at == null && y < r.Y + r.H; y += 2)
            for (double x = r.X + 2; at == null && x < r.X + r.W; x += 2)
                for (var t = (AsDisplayObject)h.Player.MouseTarget(x, y); t != null; t = t.Parent)
                    if (t == slot) { at = (x, y); break; }
        Assert.That(at, Is.Not.Null, "a point on member 3's slot");
        h.Player.MouseMove(at.Value.X, at.Value.Y);
        h.Player.MouseButton(true, Player.RightButton);
        h.Player.MouseButton(false, Player.RightButton);
        Assert.That(menuFor, Is.EqualTo(3));
        Assert.That(seen, Is.EqualTo(new[] { "rightMouseDown", "rightMouseUp", "rightClick" }));
        Assert.That(h.RT.Errors, Is.Empty);
        // the left button's events go on as they are
        seen.Clear();
        menuFor = 0;
        h.Player.MouseButton(true);
        h.Player.MouseButton(false);
        Assert.That(seen, Is.EqualTo(new[] { "click" }));
        Assert.That(menuFor, Is.EqualTo(0));
    }

    [Test]
    public void FieldPcMenuListsTheCoupleActionAndSitsBesideThePc()
    {
        // FUN_141a8ab90 (uipartydialog.cpp:3776): mode 1, flags 0x3f3fe; FUN_141a83810 adds 0x1fc (AddCoupleActionMenu, flag
        // 0x8000, not for the own PC) after the block items and before the report; Open (:2475-2489) places it with
        // CalcMenuPosition and the camera height offset
        var m = Model();
        var projected = new List<System.Numerics.Vector3>();
        bool rightSide = true;
        var c = new PopupMenuController
        {
            SelfCharacterId = 12, ProfileImagePath = (_, _) => "", PlayerInField = _ => true, OffsetPcPopupMenu = 10,
            WorldToMovie = w => { projected.Add(w); return w.X > 100 && !rightSide ? null : (w.X, w.Y); },
            MenuHeightOffset = () => 7,
        };
        var h = Load(c, m);
        PopupMenuTarget Other() => new() { Name = "Other", CharacterId = 4002, Level = 30, Alive = true, World = new(100, 200, 0) };
        c.OpenOnFieldPc(99, 2, Other());
        Assert.That(c.IsClosed, Is.False);
        var items = c.Items.ToList();
        Assert.That(items, Does.Contain(PopupMenuController.CoupleAction));
        Assert.That(items.IndexOf(PopupMenuController.CoupleAction), Is.EqualTo(items.IndexOf(PopupMenuController.Report) - 1));
        Assert.That(items.IndexOf(PopupMenuController.CoupleAction), Is.GreaterThan(items.IndexOf(PopupMenuController.Block)));
        Assert.That(projected, Is.EqualTo(new[] { new System.Numerics.Vector3(110, 200, 0) }));
        // the same PC again in mode 1: nothing (the menu stays)
        c.OpenOnFieldPc(99, 2, Other());
        Assert.That(projected, Has.Count.EqualTo(1));
        // the couple action item opens the dialog for the target's character id
        long opened = 0;
        c.OpenCoupleAction += id => opened = id;
        c.OnCallback("MenuItemClick", new object[] { (uint)PopupMenuController.CoupleAction });
        Assert.That(opened, Is.EqualTo(4002));
        c.ClosePopup();
        // the own PC (kind 1): no couple action
        c.OpenOnFieldPc(98, 1, new PopupMenuTarget { Name = "Me", CharacterId = 12, Alive = true, World = new(0, 0, 0) });
        Assert.That(c.Items, Does.Not.Contain(PopupMenuController.CoupleAction));
        c.ClosePopup();
        // the right side does not project: the left side (dir -1); neither: no menu
        rightSide = false;
        projected.Clear();
        c.OpenOnFieldPc(99, 2, Other());
        Assert.That(projected, Is.EqualTo(new[] { new System.Numerics.Vector3(110, 200, 0), new System.Numerics.Vector3(90, 200, 0) }));
        Assert.That(c.IsClosed, Is.False);
        c.ClosePopup();
        c.WorldToMovie = _ => null;
        c.OpenOnFieldPc(99, 2, Other());
        Assert.That(c.IsClosed, Is.True);
        // kind 0 or 3, or no name: nothing
        c.WorldToMovie = w => (w.X, w.Y);
        c.OpenOnFieldPc(97, 3, Other());
        c.OpenOnFieldPc(97, 2, new PopupMenuTarget());
        Assert.That(c.IsClosed, Is.True);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void TheCameraHeightOffsetFollowsTheZoom()
    {
        // GetCameraHeightOffset (@0x141a83db0): ceil((far / h) * ((far - h) / (far - near)) * 60 * screenH / 1050)
        Assert.That(PopupMenuController.CameraHeightOffset(400, 1100, 800, 1050), Is.EqualTo(36f));
        Assert.That(PopupMenuController.CameraHeightOffset(400, 1100, 1100, 1050), Is.EqualTo(0f));
        Assert.That(PopupMenuController.CameraHeightOffset(400, 1100, 400, 2100), Is.EqualTo(MathF.Ceiling(1100f / 400 * 60 * 2)));
        Assert.That(PopupMenuController.CameraHeightOffset(500, 500, 500, 1050), Is.EqualTo(0f));
    }

    [Test]
    public void PartySlotMenuListsTheClientItems()
    {
        // UIPopUpMenu for the party slots (FUN_141a83810 with 0x63fe): the own slot, an online member, an offline member,
        // as the leader and after the lead passes; the movie's "open" takes the list and MenuItemClick raises the item
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PopupMenuController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, ProfileImagePath = (_, _) => "" };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        bool voteKick = m.IsFeatureEnabled(0xe0);
        int[] Menu(long id)
        {
            c.OpenForPartySlot(100, 200, id, m.Player);
            var items = c.Items.ToArray();
            c.ClosePopup();
            return items;
        }
        int[] With(bool on, params int[] ids) => on ? ids : Array.Empty<int>();
        const int self = (int)PartyFixture.SelfCharacterId;
        // the own slot: view character, leave, ready check (the leader)
        Assert.That(Menu(self), Is.EqualTo(new[] { PopupMenuController.ViewOwnCharacter, PopupMenuController.LeaveParty, PopupMenuController.ReadyCheck }));
        // an online member as the leader: view, whisper, group chat, house, home, friend, kick, leader, (vote), block, report
        Assert.That(Menu(3), Is.EqualTo(new[] { 0x66, 0x65, 0x1f6, 0x1f7, 0x1f8, 0x12d, 0xca, 0xcb }.Concat(With(voteKick, 0xcc)).Concat(new[] { 400, 0x1f5 })));
        // an offline member: no whisper, group chat, leader change or report
        Assert.That(Menu(5), Is.EqualTo(new[] { 0x66, 0x1f7, 0x1f8, 0x12d, 0xca }.Concat(With(voteKick, 0xcc)).Concat(new[] { 400 })));
        // not the leader: the own slot only leaves, a member only has the vote kick of the party items
        book.Apply(new PartyLeaderChange(3), PartyFixture.SelfCharacterId, true);
        Assert.That(Menu(self), Is.EqualTo(new[] { PopupMenuController.ViewOwnCharacter, PopupMenuController.LeaveParty }));
        Assert.That(Menu(4), Is.EqualTo(new[] { 0x66, 0x65, 0x1f6, 0x1f7, 0x1f8, 0x12d }.Concat(With(voteKick, 0xcc)).Concat(new[] { 400, 0x1f5 })));
        // the movie's list: open, labelled from string table 7, and a click raises the item for the target
        c.OpenForPartySlot(100, 200, 3, m.Player);
        Assert.That(c.IsClosed, Is.False);
        Assert.That(c.InfoString, Is.EqualTo("partySlot"));
        Assert.That(m.Tables.PopupMenuName(0x1f5), Is.Not.Empty);
        var menu = h.Child("m_menu") as AsObject;
        Assert.That(Rt.ToBoolean(h.Get(menu, "visible")), Is.True);
        (int id, string name) clicked = default;
        c.ItemClicked += (id, t) => clicked = (id, t.Name);
        c.OnCallback("MenuItemClick", new object[] { (uint)PopupMenuController.Report });     // unported: raised
        Assert.That(clicked, Is.EqualTo((PopupMenuController.Report, "FixtureArcher")));
        // a disband closes it (FUN_141a80a50 mode 7)
        c.OnPartyMessage(new PartyDisband());
        Assert.That(c.IsClosed, Is.True);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void GuildMemberMenuListsTheClientItemsAndThePartyMenuGetsTheGuildInvite()
    {
        // PopUpMenuFromGuildMember (FUN_141a83810 with 0x63de): no trade, no guild invite for a member; the party slot's 0x63fe
        // lists the guild invite (FUN_141a84b40) for a character outside the own guild, and its click is FUN_1413b71c0
        var m = Model();
        var guild = GuildDialogTests.InGuild();
        var sent = new List<IOutPacket>();
        guild.Send = sent.Add;
        var bo = new GuildMember { Mode = 3, Grade = 1, Name = "Bo", CharId = 13, Level = 20, Triple = (1, 2, 3), F14 = 10, JobCode = 100, Offline = 0 };
        guild.Info.Members.Add(bo);
        var c = new PopupMenuController { SelfCharacterId = 12, ProfileImagePath = (id, code) => $"{id}:{code}", Guild = guild };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        c.OpenForGuildMember(100, 200, bo);
        Assert.That(c.Items, Is.EqualTo(new[] { 0x66, 0x65, 0x1f6, 0x1f7, 0x1f8, 0x12d, 0xc9, 400, 0x1f5 }));
        Assert.That((c.InfoString, c.Target.Name, c.Target.Trophy, c.Target.JobCode, c.Target.Online), Is.EqualTo(("", "Bo", 6, 100, true)));
        c.ClosePopup();
        bo.Offline = 1;
        c.OpenForGuildMember(100, 200, bo);
        Assert.That(c.Items, Is.EqualTo(new[] { 0x66, 0x1f7, 0x1f8, 0x12d, 400 }));
        c.ClosePopup();
        c.OpenForGuildMember(100, 200, guild.FindMemberByName("Ma"));                 // the own character: kind 1
        Assert.That(c.Items, Is.EqualTo(new[] { PopupMenuController.ViewOwnCharacter }));
        c.ClosePopup();
        // the party slot as the leader: the guild invite after the party items for a character outside the guild
        c.Party = PartyFixture.Book(2000062, m.Player);
        c.SelfCharacterId = PartyFixture.SelfCharacterId;
        c.OpenForPartySlot(100, 200, 3, m.Player);
        Assert.That(c.Items, Does.Contain(PopupMenuController.InviteToGuild));
        var listed = c.Items.ToList();
        Assert.That(listed.IndexOf(PopupMenuController.InviteToGuild), Is.EqualTo(listed.IndexOf(400) - 1));
        c.OnCallback("MenuItemClick", new object[] { (uint)PopupMenuController.InviteToGuild });
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new GuildInviteRequest("FixtureArcher") }));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void PartySearchChiefMenuListsTheClientItems()
    {
        // UIPartySearchDialog::FUN_140f8e790 (flags 0x40da, "partySearchSlot"): the own character opens PopUpMenu (kind 1), a
        // character outside the field OpenFromCharName by name (view, whisper, group chat, friend by name, block by name, report;
        // no party, trade or house items), a character in the field FUN_141a7eb60 from its user: the same items by id, a header with
        // the user's level, image, trophy sum and job, kind 2 and the buddy it finds in m_d0
        var m = Model();
        var buddies = new BuddyBook();
        buddies.Add(new BuddyInfo { Id = 1, CharacterId = 0, Name = "Chief" });
        buddies.Add(new BuddyInfo { Id = 5, CharacterId = 78, Name = "Nearby" });
        var inField = new Dictionary<long, UserBlock>();
        var c = new PopupMenuController
        {
            Buddies = buddies, SelfCharacterId = 12, ProfileImagePath = (id, pic) => $"img{id}:{pic}",
            PlayerInField = inField.ContainsKey, FieldUser = id => inField.GetValueOrDefault(id),
        };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        c.OpenForPartySearch(100, 200, 1, 12, m.Player.Name, m.Player);                   // the own character
        Assert.That(c.Items, Is.EqualTo(new[] { PopupMenuController.ViewOwnCharacter }));
        Assert.That(c.InfoString, Is.EqualTo("partySearchSlot"));
        c.ClosePopup();
        c.OpenForPartySearch(100, 200, 2, 77, "Stranger", m.Player);                       // by name, no buddy entry
        Assert.That(c.Items, Is.EqualTo(new[] { 0x66, 0x65, 0x1f6, 0x12d, 400, 0x1f5 }));
        Assert.That((c.InfoString, c.Target.Name, c.Target.CharacterId, c.Target.Online), Is.EqualTo(("partySearchSlot", "Stranger", 77L, true)));
        c.ClosePopup();
        c.OpenForPartySearch(100, 200, 2, 0, "Chief", m.Player);                           // no character id (no 0x66); a buddy by name
        Assert.That(c.Items.Contains(PopupMenuController.ViewCharacter), Is.False);
        Assert.That(c.Items.Contains(PopupMenuController.AddFriend) != c.Items.Contains(PopupMenuController.Unfriend), Is.True);
        c.ClosePopup();
        inField[78] = new UserBlock
        {
            CharacterId = 78, Name = "Nearby", Level = 41, Picture = "pic", JobCode = 30, Job = 3010,
            AchievementCombat = 5, AchievementAdventure = 6, AchievementLifestyle = 7,
        };
        c.OpenForPartySearch(100, 200, 2, 78, "Chief", m.Player);                          // a user in the field: its own name and data
        Assert.That(c.IsClosed, Is.False);
        Assert.That(c.Items, Is.EqualTo(new[] { 0x66, 0x65, 0x1f6, 300, 400, 0x1f5 }));   // id 78: the buddy by id gives unfriend
        Assert.That((c.InfoString, c.Target.Name, c.Target.CharacterId, c.Target.ByName, c.Target.Online, c.Target.Alive),
            Is.EqualTo(("partySearchSlot", "Nearby", 78L, false, true, true)));
        Assert.That((c.Target.Level, c.Target.Image, c.Target.Trophy, c.Target.JobGroup, c.Target.JobCode),
            Is.EqualTo(((ushort)41, "img78:pic", 18, 30, 3010)));
        Assert.That(c.BuddyId, Is.EqualTo(5L));
        c.ClosePopup();
        inField[79] = new UserBlock { CharacterId = 79, Name = "" };
        c.OpenForPartySearch(100, 200, 2, 79, "Nobody", m.Player);                         // a proxy with no name opens nothing
        Assert.That(c.IsClosed, Is.True);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void PartySlotBuddyItemsFollowTheBuddyList()
    {
        // FUN_141a847c0 / FUN_141a84860 by FindByAccount: a friend gets 300 for 0x12d, a blocked entry 0x191 + 0x192 for 400 and
        // no 0x12d, a waiting request no 0x12d; the items go to the query dialog through FUN_141a83ed0 (OnMessage)
        var m = Model();
        var party = PartyFixture.Book(2000062, m.Player);
        var buddies = new BuddyBook();
        buddies.Add(new BuddyInfo { Id = 1, CharacterId = 3, Name = "FixtureArcher" });
        buddies.Add(new BuddyInfo { Id = 2, CharacterId = 4, Name = "FixturePriest", Blocked = true, Memo = "spam" });
        buddies.Add(new BuddyInfo { Id = 3, CharacterId = 5, Name = "FixtureWizard", Waiting = true });
        var boxes = new List<MessageBoxData>();
        var query = new CommunityQueryController(buddies) { Common = m.Tables.Common, ShowBox = boxes.Add, NextBox = () => { } };
        int opens = 0;
        query.OpenRequested += () => opens++;
        var c = new PopupMenuController
        {
            Party = party, Buddies = buddies, SelfCharacterId = PartyFixture.SelfCharacterId, ProfileImagePath = (_, _) => "",
            BuddyQuery = (r, k, n, b, ch) => query.OnQuery(r, k, n, b, ch),
        };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        int[] Menu(long id)
        {
            c.OpenForPartySlot(100, 200, id, m.Player);
            var items = c.Items.ToArray();
            c.ClosePopup();
            return items;
        }
        Assert.That(Menu(3), Does.Contain(PopupMenuController.Unfriend).And.Contain(PopupMenuController.Block).And.Not.Contain(PopupMenuController.AddFriend));
        Assert.That(Menu(4), Does.Contain(PopupMenuController.Unblock).And.Contain(PopupMenuController.EditBlockReason)
                                 .And.Not.Contain(PopupMenuController.Block).And.Not.Contain(PopupMenuController.AddFriend));
        Assert.That(Menu(5), Does.Contain(PopupMenuController.Block).And.Not.Contain(PopupMenuController.AddFriend).And.Not.Contain(PopupMenuController.Unfriend));
        // the remove item asks first (OnMessage result 1, mode 6): the 0x5f8 box on the query; the party slot's m_d0 is 0
        c.OpenForPartySlot(100, 200, 3, m.Player);
        c.ItemClick(PopupMenuController.Unfriend);
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That((boxes[0].Type, boxes[0].Extra, boxes[0].Listener), Is.EqualTo((1, "delBuddy", (object)query)));
        Assert.That(boxes[0].Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(m.Tables.Common(0x5f8), "FixtureArcher")));
        c.ItemClick(PopupMenuController.Block);                         // result 0, mode 2: the closed dialog opens
        Assert.That((opens, query.Mode), Is.EqualTo((1, 2)));
        c.ClosePopup();
        c.OpenForPartySlot(100, 200, 4, m.Player);
        c.ItemClick(PopupMenuController.Unblock);
        Assert.That((boxes[^1].Extra, boxes[^1].Msg), Is.EqualTo(("delBanBuddy", Ms2.Quest.ClientFormat.Format(m.Tables.Common(0x5f9), "FixturePriest"))));
        c.ItemClick(PopupMenuController.EditBlockReason);
        Assert.That((opens, query.Mode), Is.EqualTo((2, 4)));
        // with a buddy id (m_d0), the entry's own id is sent, and an add for a received request shows 0x623 instead
        var received = new BuddyBook();
        received.Add(new BuddyInfo { Id = 7, CharacterId = 5, Name = "FixtureWizard", Received = true });
        var asked = new List<string>();
        c.Buddies = received;
        c.BuddyQuery = (r, k, n, b, ch) => asked.Add($"{r} {k} {n} {b} {ch}");
        c.ShowBox = boxes.Add;
        c.ClosePopup();
        c.OpenForPartySlot(100, 200, 5, m.Player);
        c.BuddyId = 99;                                                 // m_d0 is set after ApplyMenuInfo (which clears it) by the opener
        c.ItemClick(PopupMenuController.AddFriend);
        Assert.That(asked, Is.Empty);
        Assert.That((boxes[^1].Type, boxes[^1].Msg), Is.EqualTo((0, m.Tables.Common(0x623))));
        c.ItemClick(PopupMenuController.Block);
        Assert.That(asked, Is.EqualTo(new[] { "0 2 FixtureWizard 7 5" }));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void PartySlotMenuItemsSendTheClientRequests()
    {
        // UIPopUpMenu's party handlers (RegisterMenuHandlers): leave / ready check send at once, kick and leader change ask
        // first (OnMessage on yes), the vote kick asks with its own callback; the own slot's view opens the own info
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PopupMenuController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, ProfileImagePath = (_, _) => "" };
        var sent = new List<object>();
        var boxes = new List<MessageBoxData>();
        int nexts = 0, myInfo = 0;
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.NextBox = () => nexts++;
        c.OpenMyInfo += () => myInfo++;
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        c.OpenForPartySlot(100, 200, PartyFixture.SelfCharacterId, m.Player);
        c.ItemClick(PopupMenuController.ViewOwnCharacter);
        c.ItemClick(PopupMenuController.LeaveParty);
        c.ItemClick(PopupMenuController.ReadyCheck);
        Assert.That(myInfo, Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new object[] { new PartyLeaveRequest(), new PartyReadyCheckRequest() }));
        c.ClosePopup();
        sent.Clear();

        c.OpenForPartySlot(100, 200, 3, m.Player);
        c.ItemClick(PopupMenuController.ViewOwnCharacter);              // not the own character
        Assert.That(myInfo, Is.EqualTo(1));
        c.ItemClick(PopupMenuController.KickFromParty);
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Type, Is.EqualTo(1));
        Assert.That(boxes[0].Extra, Is.EqualTo("expelMember:FixtureArcher"));
        Assert.That(boxes[0].Listener, Is.SameAs(c));
        Assert.That(boxes[0].Msg, Is.EqualTo(Ms2.Quest.ClientFormat.Format(m.Tables.Common("s_party_check_expel_member"), "FixtureArcher")));
        c.OnMessageBox(null, 1, boxes[0].Extra);                        // no: nothing sent, the box goes on
        Assert.That(sent, Is.Empty);
        Assert.That(nexts, Is.EqualTo(1));
        c.OnMessageBox(null, 0, boxes[0].Extra);
        Assert.That(sent, Is.EqualTo(new object[] { new PartyKickRequest(3) }));
        c.OnMessageBox(null, 0, "expelMember:NoSuchMember");            // no member by that name: nothing
        Assert.That(sent, Has.Count.EqualTo(1));
        c.ItemClick(PopupMenuController.MakePartyLeader);
        Assert.That(boxes[1].Extra, Is.EqualTo("changeChief:FixtureArcher"));
        c.OnMessageBox(null, 0, boxes[1].Extra);
        Assert.That(sent[^1], Is.EqualTo(new PartyLeaderChangeRequest("FixtureArcher")));
        c.ItemClick(PopupMenuController.VoteToKick);
        Assert.That(boxes[2].HasCallback, Is.True);
        Assert.That(boxes[2].Callback(1, boxes[2]), Is.True);
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(boxes[2].Callback(0, boxes[2]), Is.True);
        Assert.That(sent[^1], Is.EqualTo(new PartyVoteKickRequest(3)));
        // Invite to My Home: RequestHome u8 1 + the name (SendRequestHome), a local player and not the own character
        c.ItemClick(PopupMenuController.InviteToMyHome);                // no local player (FUN_1418fd580): nothing
        Assert.That(sent, Has.Count.EqualTo(3));
        m.HasLocalPlayer = true;
        c.ItemClick(PopupMenuController.InviteToMyHome);
        Assert.That(sent[^1], Is.EqualTo(new HomeInviteRequest("FixtureArcher")));
        // furnishing rights: RequestCube u8 0x39 + the name with feature 0x100 only (SendRequestCube); both settings of the
        // feature, whatever the data's table holds
        Assert.That(m.FeatureEnabledByName, Is.Null, "the fixture model takes its features from the id set");
        bool had = m.Features.Contains(0x100);
        m.Features.Remove(0x100);
        int before = sent.Count;
        c.ItemClick(PopupMenuController.GrantFurnishingRights);
        Assert.That(sent, Has.Count.EqualTo(before));
        m.Features.Add(0x100);
        c.ItemClick(PopupMenuController.GrantFurnishingRights);
        Assert.That(sent[^1], Is.EqualTo(new FurnishingRightsRequest("FixtureArcher")));
        if (!had) m.Features.Remove(0x100);
        // an unported item still raises
        int raised = 0;
        c.ItemClicked += (_, _) => raised++;
        c.ItemClick(PopupMenuController.Report);
        Assert.That(raised, Is.EqualTo(1));
        // Trade asks through the trade book (SendTradeRequest); with none set it sends nothing and does not raise
        c.ItemClick(PopupMenuController.Trade);
        Assert.That(raised, Is.EqualTo(1));
        // Whisper with no messenger browser: nothing, and it does not raise
        c.ItemClick(PopupMenuController.Whisper);
        Assert.That(raised, Is.EqualTo(1));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void PartySlotViewCharacterSendsTheCharacterInfoRequest()
    {
        // SendCharacterInfo (:3135): TryBeginRequest 0 closes the 0x4ec8 window and sends SendOp 0x1E with the id; 1 (the
        // same character within 5000 ms, unanswered) shows s_char_info_already_request; 2 (open on it) does nothing
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        uint now = 1000;
        bool open = false;
        var req = new PcInfoRequests { TickMs = () => now };
        var c = new PopupMenuController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, ProfileImagePath = (_, _) => "" };
        var sent = new List<object>();
        var boxes = new List<MessageBoxData>();
        int closes = 0;
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.CharacterInfo = req;
        c.CharacterInfoOpen = () => open;
        c.CloseCharacterInfo += () => closes++;
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        c.OpenForPartySlot(100, 200, 3, m.Player);
        c.ItemClick(PopupMenuController.ViewCharacter);
        Assert.That(sent, Is.EqualTo(new object[] { new CharacterInfoRequest(3) }));
        Assert.That(closes, Is.EqualTo(1));
        now += 4999;
        c.ItemClick(PopupMenuController.ViewCharacter);
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(boxes, Has.Count.EqualTo(1));
        Assert.That(boxes[0].Type, Is.EqualTo(0));
        Assert.That(boxes[0].Msg, Is.EqualTo(m.Tables.Common("s_char_info_already_request")));
        now += 1;
        c.ItemClick(PopupMenuController.ViewCharacter);
        Assert.That(sent, Is.EqualTo(new object[] { new CharacterInfoRequest(3), new CharacterInfoRequest(3) }));
        req.OnAnswer(new CharacterInfoAnswer(3, 1, new PcInfo { CharacterId = 3 }), true, false);
        open = true;
        now += 10000;
        c.ItemClick(PopupMenuController.ViewCharacter);                 // open on the same character: nothing
        Assert.That((sent.Count, boxes.Count, closes), Is.EqualTo((2, 1, 2)));
        c.ClosePopup();
        c.ItemClick(PopupMenuController.ViewCharacter);                 // m_100: nothing
        Assert.That(sent, Has.Count.EqualTo(2));
        c.CharacterInfo = null;                                         // no 0x4ec8 dialog: nothing
        c.OpenForPartySlot(100, 200, 3, m.Player);
        c.ItemClick(PopupMenuController.ViewCharacter);
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void PartySlotWhisperStartsTheChatWindowsWhisper()
    {
        // FUN_141a84da0 -> UIMessengerBrowser FUN_141a252a0 (the valid window) -> UIMessenger FUN_141a14bb0 -> BeginWhisper:
        // the input gets the whisper command (common 0xaa5), the name and a space, focused with the caret at the end
        var m = Model();
        m.HasLocalPlayer = true;
        var chat = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var mh = Load(chat, m);
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PopupMenuController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, ProfileImagePath = (_, _) => "", Messenger = chat.Browser };
        var h = Load(c, m);
        c.OpenForPartySlot(100, 200, 3, m.Player);
        c.ItemClick(PopupMenuController.Whisper);
        var input = (AsObject)mh.Child("m_textInput");
        string command = m.Tables.Common(0xaa5);
        Assert.That(command, Is.Not.Empty);
        Assert.That(Rt.ToString(mh.Get(input, "text")), Is.EqualTo(command + " FixtureArcher "));
        Assert.That(Rt.ToNumber(mh.Get(input, "focused")), Is.EqualTo(1));
        // the own name is not whispered (FUN_141a14bb0)
        chat.Browser.Whisper(m.Player.Name);
        Assert.That(Rt.ToString(mh.Get(input, "text")), Is.EqualTo(command + " FixtureArcher "));
        Assert.That(mh.RT.Errors, Is.Empty);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ThePartyWindowClosesAtOnceSoTheNextOpenLoadsItAgain()
    {
        // OnPartyInfo (@0x14162d5b0) does CloseDialog(0x4e45) then OpenDialog(0x4e45). UIPartyDialog's ctor (@0x141a74b40) has no
        // SetTweenEnabled, so FUN_141924330 (uidialog.cpp:393) unloads at once (m_c0 2) and the open (vfunc_65, :564, only for a
        // closed dialog) loads the movie again. A close tween here left the dialog "closing" and dropped that second open.
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PartyController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, MapName = _ => "Field" };
        var h = Load(c, m);
        Assert.That(c.IsClosed, Is.False);
        bool unloaded = false;
        c.Unloaded += () => unloaded = true;
        c.Close();
        Assert.That(c.IsClosed, Is.True, "the close is at once, with no tween running");
        Assert.That(unloaded, Is.True);
        var again = Load(c, m);
        Assert.That(c.IsClosed, Is.False, "the open after the close loads it again");
        Assert.That(again.RT.Errors, Is.Empty);
    }

    [Test]
    public void PartyWindowButtonsSendTheClientRequests()
    {
        // UIPartyDialog: enter dungeon sends at once, give up asks, create club asks for a name and checks it (CreateClub)
        var m = Model();
        var book = PartyFixture.Book(2000062, m.Player);
        var c = new PartyController { Party = book, SelfCharacterId = PartyFixture.SelfCharacterId, MapName = _ => "Field" };
        var sent = new List<object>();
        var boxes = new List<MessageBoxData>();
        var notices = new List<string>();
        c.Send = sent.Add;
        c.ShowBox = boxes.Add;
        c.SystemNotice += notices.Add;
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        c.OnEnterDungeonClick();
        Assert.That(sent, Is.EqualTo(new object[] { new RoomDungeonEnterRequest(0) }));
        c.OnGiveUpDungeonClick();
        Assert.That(boxes[0].Type, Is.EqualTo(1));
        Assert.That(boxes[0].Msg, Is.EqualTo(m.Tables.Common("s_party_dungeon_giveup")));
        boxes[0].Callback(1, boxes[0]);
        Assert.That(sent, Has.Count.EqualTo(1));
        boxes[0].Callback(0, boxes[0]);
        Assert.That(sent[^1], Is.EqualTo(new DungeonGiveUpRequest()));
        sent.Clear();

        c.OnCreateClubClick();
        var box = boxes[1];
        Assert.That(box.Type, Is.EqualTo(10));
        Assert.That(box.InputMaxChars, Is.EqualTo(m.Tables.ClubNameLengthMax));
        box.ClickedTag = "ab cd";
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(notices, Is.EqualTo(new[] { m.Tables.Common("s_club_err_clubname_has_blank") }));
        box.ClickedTag = "a";                                           // too short: silent
        box.Callback(0, box);
        Assert.That(notices, Has.Count.EqualTo(1));
        box.ClickedTag = "";                                            // empty: the box stays
        Assert.That(box.Callback(0, box), Is.False);
        Assert.That(sent, Is.Empty);
        box.ClickedTag = "Club1,";
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(sent, Is.EqualTo(new object[] { new ClubCreateRequest("Club1") }));
        // not the leader: no box, and a create is refused
        book.Apply(new PartyLeaderChange(3), PartyFixture.SelfCharacterId, true);
        c.OnCreateClubClick();
        Assert.That(boxes, Has.Count.EqualTo(2));
        Assert.That(c.CreateClub("Club2"), Is.False);
        Assert.That(notices[^1], Is.EqualTo(m.Tables.Common("s_club_err_create_no_party")));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MainPlayerInfoInitAndStatusEvents()
    {
        var m = Model();
        var h = Load(new MainPlayerInfoController(), m);
        Assert.That(h.RT.Errors, Is.Empty);
        // vfunc_43: bottom centre above the exp bar
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), w = Rt.ToNumber(h.Get(h.Entity, "width"));
        double y = Rt.ToNumber(h.Get(h.Entity, "y")), ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(x + w / 2, Is.EqualTo(960).Within(1));
        Assert.That(y + ht, Is.EqualTo(1080 - 19).Within(1));
        var pi = h.Child("m_playerInfo") as AsDisplayObject;
        Assert.That(pi, Is.Not.Null);
        var texts = AllTexts(pi);
        TestContext.WriteLine("texts: " + string.Join(" | ", texts));
        // the quick slots bind "SlotInfo.skill.0.N" and show the key bound to each slot's action (DefaultKey.xml)
        Assert.That(h.BindingNames, Does.Contain("SlotInfo.skill.0.0").And.Contain("SlotInfo.skill.0.24"));
        Assert.That(h.RT.Errors, Is.Empty);
        var slotTexts = AllTexts(h.Child("m_slot") as AsDisplayObject);
        Assert.That(slotTexts.Where(t => t.StartsWith("textField=")).Take(48).Where((_, n) => n % 2 == 0).Select(t => t[10..]),
            Is.EqualTo(new[] { "Q", "W", "E", "R", "A", "S", "D", "F", "1", "2", "3", "4", "5", "6", "7", "8", "Insert", "Home", "Page Up", "Delete", "End", "Page Dn", "Z", "X" }));
        Assert.That(texts, Has.Some.Contains("1630"));
        Assert.That(texts, Has.Some.Contains("2100"));
        m.Player.HpCur = 1000; m.Notify(StatusEvent.Hp);
        for (int i = 0; i < 60; i++) h.Player.Tick();
        Assert.That(AllTexts(pi), Has.Some.Contains("1000"));
    }

    /// <summary>The search button's tooltip is GFxManager::Localize("$s_gamedictionary_tooltip_search") (uiupperhuddialog.cpp:277),
    /// the Flash table's text, not the common table's.</summary>
    [Test]
    public void UpperHudSearchTooltipIsTheFlashTablesText()
    {
        var m = Model();
        m.Features.Add(Feature.SearchTooltip);
        var flash = Ms2.Formats.World.GameStrings.FlashTable(packs, "en", LobbyFeatures.LocaleOf(packs));
        Assert.That(flash.TryGetValue("$s_gamedictionary_tooltip_search", out var want), Is.True, "the data's Flash row");
        var c = new UpperHudController();
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        p.Translate = k => flash.TryGetValue(k, out var t) ? t : k;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        // changeTooltipText (the movie's button): _tooltipData.text = the argument while no external tooltip getter is set
        var tip = h.Get(h.Get(h.Child("m_searchButton"), "_tooltipData"), "text");
        Assert.That(Rt.ToString(tip), Is.EqualTo(want));
    }

    [Test]
    public void UpperHudMoneyAndClock()
    {
        var m = Model();
        var h = Load(new UpperHudController(), m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(Text(h, "mesoText"), Is.EqualTo("1,284,530"));
        Assert.That(Text(h, "maratText"), Is.EqualTo("350"));
        Assert.That(Text(h, "timeText"), Is.EqualTo("09:05"));
        m.Player.Mesos = 7; m.Notify(StatusEvent.Meso);
        Assert.That(Text(h, "mesoText"), Is.EqualTo("7"));
    }

    [Test]
    public void UpperHudMeratFeatureAndTick()
    {
        var m = Model();
        var now = new DateTime(2026, 1, 1, 9, 5, 0);
        m.ServerTime = () => now;
        m.Features.Remove(Feature.Merat);
        var c = new UpperHudController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // vfunc_46: no merats without feature 0x3d
        Assert.That((h.FindNamed("m_marat") as AsDisplayObject)?.visible, Is.False);
        // vfunc_15: the clock follows the server time once a second
        now = now.AddMinutes(2);
        c.Process(500);
        Assert.That(Text(h, "timeText"), Is.EqualTo("09:05"));
        c.Process(1500);
        Assert.That(Text(h, "timeText"), Is.EqualTo("09:07"));
    }

    [Test]
    public void UpperHudSearchButton()
    {
        static void Click(UiHost h, string name)
        {
            var b = ((AsDisplayObject)h.Child(name)).GetBounds(null);
            h.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
            for (int i = 0; i < 5; i++) h.Player.Tick();
        }
        // OnSearchClick with feature 0x2f: ToggleDialog(0x4f0b)
        var c = new UpperHudController();
        int toggles = 0;
        c.ToggleDictionary += () => toggles++;
        var h = Load(c, Model());
        Click(h, "m_searchButton");
        Assert.That(toggles, Is.EqualTo(1));
        Assert.That(h.RT.Errors, Is.Empty);
        // without it: the search input shows and the button is disabled
        var m = Model();
        m.Features.Remove(Feature.SearchTooltip);
        c = new UpperHudController();
        c.ToggleDictionary += () => toggles++;
        h = Load(c, m);
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_searchInput"), "visible")), Is.False);
        Click(h, "m_searchButton");
        Assert.That(toggles, Is.EqualTo(1));
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_searchInput"), "visible")), Is.True);
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_searchButton"), "enabled")), Is.False);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void UpperHpBarShowsTheTarget()
    {
        var m = Model();
        var mob = new UpperHpBarController.Actor { ObjectId = 7, IsNpc = true, NpcClass = 1, Hp = 600, MaxHp = 1000, BaseMaxHp = 1000, Name = "Mob", TypeName = "Plant", Level = 3 };
        var c = new UpperHpBarController { LocalTargetId = 1, FindActor = id => id == 7 ? mob : null };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // a hit by someone else does not bring a new target up
        c.Hit(mob, 2);
        Assert.That(c.Shown, Is.EqualTo(0));
        c.Hit(mob, 1);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.Shown, Is.EqualTo(7));
        var texts = AllTexts(h.Entity as AsDisplayObject);
        TestContext.WriteLine(string.Join(" | ", texts));
        Assert.That(texts, Has.Some.Contains("Mob"));
        // the per-frame update follows the HP
        mob.Hp = 250;
        c.Process();
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(AllTexts(h.Entity as AsDisplayObject), Has.Some.Contains("250"));
        c.Remove(mob);
        Assert.That(c.Shown, Is.EqualTo(0));
    }

    [Test]
    public void MinimapInitDataAndEntities()
    {
        var m = Model();
        m.Field.Name = "Tria";
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        c.Marks.Add(new() { Entity = new Ms2.Formats.World.MinimapIcons.Entity("portal", "", "portal", "Exit", true, false), Position = () => default, ShowGuide = true });
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.FieldClip, Is.Not.Null);
        // the movie's onFieldLoadComplete: BuildEntities through createEntity
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        Assert.That(c.Marks[0].Clip, Is.Not.Null);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MinimapEnableTaxiFrameSetsTheTaxiMarkEnabledOutsideAHouseField()
    {
        // EnableTaxiFrame (uiminimapentity.cpp:1987): changeEntityFrame(the taxi's clip, "enabled"); nothing in a house field
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        c.Marks.Add(new() { Entity = Ms2.Formats.World.MinimapIcons.Taxi(false, false), Position = () => default, IsTaxi = true });
        var h = Load(c, Model());
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        var clip = c.Marks[0].Clip;
        Assert.That(clip, Is.Not.Null);
        Assert.That(h.Get(clip, "currentLabel"), Is.EqualTo("disabled"));
        c.EnableTaxiFrame(true);
        Assert.That(h.Get(clip, "currentLabel"), Is.EqualTo("disabled"), "a house field: nothing");
        c.EnableTaxiFrame(false);
        Assert.That(h.Get(clip, "currentLabel"), Is.EqualTo("enabled"));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MinimapPortalMarkClickOpensTheDungeonRoomBehindFeature0x1a()
    {
        // UIMinimapEntityProxy::vfunc_3 (uiminimapentity.cpp:680-715) through the entity's userFunction "click" (:495-503):
        // feature 0x1a, a portal of type 8, OpenDungeonOrRoom(the portal's id)
        var opened = new List<uint>();
        var m = Model();
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false), OpenDungeonOrRoomDialog = id => { opened.Add(id); return true; } };
        var icon = new Ms2.Formats.World.MinimapIcons.Entity("portal", "", "portal", "Exit", true, false);
        c.Marks.Add(new() { Entity = icon, Position = () => default, ShowGuide = true, HasPortal = true, PortalType = 8, PortalId = 27003001 });
        c.Marks.Add(new() { Entity = icon, Position = () => default, ShowGuide = true, HasPortal = true, PortalType = 1, PortalId = 5 });
        c.Marks.Add(new() { Entity = icon, Position = () => default });
        var h = Load(c, m);
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.Marks[0].UserFunction, Is.Not.Null);
        Assert.That(c.Marks[2].UserFunction, Is.Not.Null);

        m.Features.Remove(MinimapController.DungeonRoomFeature);
        c.Marks[0].UserFunction(new object[] { "click" });
        Assert.That(opened, Is.Empty, "feature 0x1a off");

        m.Features.Add(MinimapController.DungeonRoomFeature);
        c.Marks[0].UserFunction(new object[] { "tooltip" });
        Assert.That(opened, Is.Empty, "only the click opens");
        c.Marks[0].UserFunction(new object[] { "click" });
        Assert.That(opened, Is.EqualTo(new[] { 27003001u }));
        c.Marks[1].UserFunction(new object[] { "click" });
        c.Marks[2].UserFunction(new object[] { "click" });
        Assert.That(opened, Has.Count.EqualTo(1), "another portal type and a proxy without a portal open nothing");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    static (MinimapController c, UiHost h) PingMinimap(bool allowed, List<string> sounds)
    {
        var m = Model();
        var c = new MinimapController
        {
            ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false), PingAllowed = allowed,
            CharacterName = id => id == 7 ? "seven" : "", SystemSound = sounds.Add,
        };
        var h = Load(c, m);
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        return (c, h);
    }

    [Test]
    public void MinimapAddPingMakesOnePingPerUidAndUpdatesIt()
    {
        // AddPing (uiminimapentity.cpp:2127-2143): a new uid is a ping entity; the same uid again is UpdatePing on it
        var sounds = new List<string>();
        var (c, h) = PingMinimap(true, sounds);
        c.AddPing(7, 0, new Ms2.Formats.Model.Vec3(10, 20, 0));
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.TryGetPing(7, out var clip, out var text), Is.True);
        Assert.That(clip, Is.Not.Null);
        Assert.That(text, Is.EqualTo("seven"));
        Assert.That(sounds, Is.EqualTo(new[] { MinimapController.RedPingSound }));
        c.AddPing(7, 1, new Ms2.Formats.Model.Vec3(30, 40, 0));
        Assert.That(c.TryGetPing(7, out var again, out _), Is.True);
        Assert.That(again, Is.SameAs(clip), "the same uid keeps its entity");
        Assert.That(sounds, Is.EqualTo(new[] { MinimapController.RedPingSound, MinimapController.BluePingSound }));
        Assert.That(System.Convert.ToDouble(h.Get(clip, "x")), Is.EqualTo(30));
        Assert.That(System.Convert.ToDouble(h.Get(clip, "y")), Is.EqualTo(40));
        c.AddPing(8, 0, new Ms2.Formats.Model.Vec3(1, 2, 0));
        Assert.That(c.TryGetPing(8, out var other, out var otherText), Is.True);
        Assert.That(other, Is.Not.SameAs(clip));
        Assert.That(otherText, Is.EqualTo(""), "no field pc with that id: an empty name");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MinimapPingNeedsTheFieldToAllowIt()
    {
        // IsPingAllowed (uiminimapentity.cpp:2521) gates AddPing and SendMinimapPing; the field data's minimapPing is off
        var sounds = new List<string>();
        var (c, h) = PingMinimap(false, sounds);
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        c.Send = sent.Add;
        c.AddPing(7, 0, new Ms2.Formats.Model.Vec3(10, 20, 0));
        Assert.That(c.TryGetPing(7, out _, out _), Is.False);
        Assert.That(sounds, Is.Empty);
        c.RedPing(); c.BluePing();
        Assert.That(sent, Is.Empty);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MinimapPingSendNeedsTheLocalPlayer()
    {
        // SendMinimapPing (uiminimapentity.cpp:2545-2548): no local pc, nothing is sent
        var (c, h) = PingMinimap(true, new List<string>());
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        c.Send = sent.Add;
        c.LocalPlayer = null;
        c.RedPing(); c.BluePing();
        Assert.That(sent, Is.Empty);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MinimapClickAndRightClickOnTheDialogSendPings()
    {
        // OnLoadComplete (uiminimapentity.cpp:1127-1131): m_90, the dialog root (uidialog.cpp:930-935), gets "click" ->
        // FUN_141a456c0 -> SendMinimapPing(0) and "rightClick" -> FUN_141a45720 -> SendMinimapPing(1); SendMinimapPing
        // (:2545-2564) sends 0xB1 with the movie's getLocal() x, y and z 0 when mouseContains() is true
        var (c, h) = PingMinimap(true, new List<string>());
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        c.Send = sent.Add;
        c.CharacterId = 77;
        var root = h.Entity;
        double N(string k) => System.Convert.ToDouble(h.Get(root, k));
        var (x, y) = (N("x") + N("m_windowWidth") / 2, N("y") + N("m_windowHeight") / 2);
        h.Player.Click(x, y);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(sent, Has.Count.EqualTo(1), "a left click on the dialog sends one ping");
        var local = (AsObject)h.Invoke("getLocal");
        var at = new System.Numerics.Vector3((float)System.Convert.ToDouble(h.Get(local, "x")), (float)System.Convert.ToDouble(h.Get(local, "y")), 0);
        var red = (MinimapPingRequest)sent[0];
        Assert.That((red.CharacterId, red.Kind, red.Position), Is.EqualTo((77L, (byte)0, at)));
        h.Player.MouseButton(true, Player.RightButton);
        h.Player.MouseButton(false, Player.RightButton);
        Assert.That(sent, Has.Count.EqualTo(2), "a right click on the dialog sends one ping");
        var blue = (MinimapPingRequest)sent[1];
        Assert.That((blue.CharacterId, blue.Kind, blue.Position), Is.EqualTo((77L, (byte)1, at)));
        // a second OnLoadComplete on the same movie does not add the listeners again
        c.OnLoadComplete();
        h.Player.Click(x, y);
        Assert.That(sent, Has.Count.EqualTo(3), "one click, one ping after a second OnLoadComplete");
        // outside the window mouseContains() is false: nothing
        h.Player.Click(N("x") + N("m_windowWidth") + 50, N("y") + N("m_windowHeight") + 50);
        Assert.That(sent, Has.Count.EqualTo(3));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MinimapRebuildRemovesThePings()
    {
        // RemoveAllEntities: RemovePingEntities (@0x141a47190) removes each ping's clip, ClearEntityContainers drops m_pings
        var (c, h) = PingMinimap(true, new List<string>());
        c.AddPing(7, 0, new Ms2.Formats.Model.Vec3(10, 20, 0));
        Assert.That(c.TryGetPing(7, out _, out _), Is.True);
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        Assert.That(c.TryGetPing(7, out _, out _), Is.False);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    static MinimapController.OtherPlayer OtherPc(long id, string name, Func<Ms2.Formats.Model.Vec3> at, Func<bool> dead = null) =>
        new() { ObjectId = (int)id, CharacterId = id, Name = name, TodayMemo = "hello", Image = "", Position = at, Dead = dead ?? (() => false) };

    [Test]
    public void MinimapOtherPlayerProxyIsAddedAndRemoved()
    {
        // AddProxy (uiminimapentity.cpp:3539-3580): another pc is createPlayer + a kind 1 entity (UpdateOtherPlayer's
        // normal/pc); before the movie it waits for BuildEntities; RemoveProxy (:3632) drops it; GetCharacterName (:2625)
        // falls back to its name
        var (c, h) = PingMinimap(true, new List<string>());
        c.AddOtherPlayer(OtherPc(21, "other", () => default));
        var o = c.Others[0];
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(o.Clip, Is.Not.Null);
        Assert.That((o.State, o.Layer), Is.EqualTo(("normal", "pc")));
        c.AddOtherPlayer(OtherPc(21, "again", () => default));
        Assert.That(c.Others, Has.Count.EqualTo(1), "a held pc is not added twice");
        c.AddPing(21, 0, new Ms2.Formats.Model.Vec3(1, 2, 0));
        Assert.That(c.TryGetPing(21, out _, out var text) && text == "other", Is.True, "the ping names the pc proxy");
        // a rebuild makes its entity again
        var first = o.Clip;
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        Assert.That(o.Clip, Is.Not.Null.And.Not.SameAs(first));
        c.RemoveOtherPlayer(21);
        Assert.That(c.Others, Is.Empty);
        Assert.That(o.Clip, Is.Null);
        Assert.That(h.RT.Errors, Is.Empty);
        // before the movie's field load the proxy is only held
        var late = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        late.AddOtherPlayer(OtherPc(5, "five", () => default));
        Assert.That(late.Others[0].Clip, Is.Null);
        var lh = Load(late, Model());
        Assert.That(late.OnCallback("onFieldLoadComplete", new object[] { late.FieldClip }), Is.True);
        Assert.That(late.Others[0].Clip, Is.Not.Null);
        Assert.That(lh.RT.Errors, Is.Empty);
    }

    [Test]
    public void MinimapOtherPlayerStateFollowsTheRelation()
    {
        // UpdateOtherPlayer (uiminimapentity.cpp:3193-3227) in RefreshOtherPlayer (:3106), the m_47c round 1 of 4: a state
        // change goes through changeEntityData and its clip is kept; dead is "die" over any tier
        var (c, h) = PingMinimap(true, new List<string>());
        var rel = MinimapController.PlayerRelation.Normal;
        bool dead = false;
        c.Relation = (_, _) => rel;
        c.AddOtherPlayer(OtherPc(21, "other", () => default, () => dead));
        var o = c.Others[0];
        ulong now = 1000;
        void Round() { for (int i = 0; i < 4; i++) { c.Process(now); now += 60; } }
        foreach (var (r, state, layer) in new[]
        {
            (MinimapController.PlayerRelation.PartyChief, "party_chief", "party"),
            (MinimapController.PlayerRelation.Party, "party", "party"),
            (MinimapController.PlayerRelation.Guild, "guild", "guild"),
            (MinimapController.PlayerRelation.Buddy, "buddy", "buddy"),
            (MinimapController.PlayerRelation.QuestTarget, "quest_target", "quest_target"),
        })
        {
            rel = r;
            Round();
            Assert.That((o.State, o.Layer), Is.EqualTo((state, layer)), r.ToString());
            Assert.That(o.Clip, Is.Not.Null);
        }
        dead = true;
        Round();
        Assert.That((o.State, o.Layer), Is.EqualTo(("die", "quest_target")));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MinimapOtherPlayersAllocateNothingPerFrame()
    {
        // vfunc_15 with moving pc proxies and the own one, after the first rounds: no state change, so no
        // changeEntityData; each move is DisplayObject's own x / y: 0 B per frame
        var (c, h) = PingMinimap(true, new List<string>());
        c.Viewport = new Ms2.Formats.World.MinimapInfo { FrLeft = -1000, FrRight = 1000, FrBottom = -1000, FrTop = 1000, ImgRight = 1920, ImgBottom = 1200 };
        float t = 0;
        c.LocalPlayer = () => (new Ms2.Formats.Model.Vec3(t, 0, 0), 90f, false);
        for (int i = 0; i < 8; i++)
        {
            int k = i;
            c.AddOtherPlayer(OtherPc(100 + i, "pc" + i, () => new Ms2.Formats.Model.Vec3(t + k, -t, t - k)));
        }
        ulong now = 1000;
        for (int i = 0; i < 16; i++) { c.Process(now); now += 60; t += 1; }
        Assert.That(c.Others.All(o => o.Visible), Is.True, "the proxies project");
        double At3() => System.Convert.ToDouble(h.Get(c.Others[3].Clip, "x")) + System.Convert.ToDouble(h.Get(c.Others[3].Clip, "y"));
        var before3 = At3();
        // least of up to 5 windows: the 24 B a single window saw on its first read comes before any Process call (three
        // bare GetAllocatedBytesForCurrentThread reads ahead of the window take it, and no window around Process calls has it)
        long used = Alloc.Least(() => { for (int i = 0; i < 400; i++) { c.Process(now); now += 16; t += 0.5f; } });
        Assert.That(used, Is.EqualTo(0), "bytes over 400 frames");
        Assert.That(At3(), Is.Not.EqualTo(before3), "the proxy moved");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void UpperHudTokenAndMeratTooltips()
    {
        var m = Model();
        m.Features.Add(Feature.PvPRankingDuelMode);
        m.Features.Add(Feature.RedMerat);
        m.Player.Tokens[4] = 1234; m.Player.Tokens[6] = 5;
        m.Player.MeratWallet = new long[] { 100, 20, 0, 7 };
        m.Player.RedMerat = 3;
        var c = new UpperHudController();
        Load(c, m);
        var t = c.GetUserData("getTooltip", new object[] { "m_tokenPocketButton" });
        Assert.That(t["text"], Is.EqualTo(m.Tables.Common("s_upper_hud_token_pocket")
            + "\n<img src='export_common_icon_honor' vspace='-6'/> 1,234 / " + N(m.Tables.TokenCaps[4])
            + "\n<img src='export_common_icon_lu' vspace='-6'/> 5 / " + N(m.Tables.TokenCaps[6])));
        static string N(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        Assert.That((t["onDelay"], t["tooltipAlign"], t["offsetX"], t["offsetY"]), Is.EqualTo((0, 7, 5, 0x16)));
        TestContext.WriteLine("token pocket: " + string.Join(" | ", TooltipTests.Draw(t)));
        var mt = c.GetUserData("getTooltip", new object[] { "m_marat" });
        var shown = TooltipTests.Draw(mt);
        TestContext.WriteLine("merat: " + mt["text"] + " => " + string.Join(" | ", shown));
        Assert.That((string)mt["text"], Does.Contain("120").And.Contain("7").And.Not.Contain("{"));
        Assert.That(c.GetUserData("getTooltip", new object[] { "m_effectHotTime" }), Is.Null, "no hot time event, no tooltip");
        c.HotTimeTexts.Add("Hot time A"); c.HotTimeTexts.Add("Hot time B");
        Assert.That(c.GetUserData("getTooltip", new object[] { "m_effectHotTime" })["text"], Is.EqualTo("Hot time A\nHot time B"));
    }

    [Test]
    public void MinimapOnChangeChannelAsksTheWorldAndKeepsTheSelectionWhenRefused()
    {
        // OnChangeChannel (uiminimapentity.cpp:1274): PopInt, WorldService::FUN_141bc80c0(channel); false: ApplySelectedChannel
        var m = Model();
        m.Field.Channel = 1;
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        Load(c, m);
        var asked = new List<int>();
        c.ChangeChannel = ch => { asked.Add(ch); return ch == 2; };
        Assert.That(c.OnCallback("OnChangeChannel", new object[] { 3 }), Is.True);
        Assert.That(c.OnCallback("OnChangeChannel", new object[] { 2 }), Is.True);
        Assert.That(asked, Is.EqualTo(new[] { 3, 2 }));
    }

    [Test]
    public void MinimapFieldNameTooltip()
    {
        var m = Model();
        m.Field.Name = "Tria";
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        Load(c, m);
        Assert.That(c.GetUserData("getTooltip", new object[] { "m_fieldname" }), Is.Null, "no field data, no tooltip");
        m.Field.MapLevel = 10;
        var d = c.GetUserData("getTooltip", new object[] { "m_fieldname" });
        Assert.That(d["text"], Is.EqualTo(Ms2.Quest.ClientFormat.Format(m.Tables.Common("s_minimap_tooltip_field_info"), "Tria", 10u)));
        Assert.That(d["tooltipAlign"], Is.EqualTo(7));
        var shown = TooltipTests.Draw(d);
        TestContext.WriteLine("m_fieldname: " + string.Join(" | ", shown));
        Assert.That(shown, Has.Some.Contains("Tria"));
        // Tria is fishing spot 02000001 (water, minMastery 0): s_worldmap_tooltip_fishingspot_low with the habitat and
        // Grade(1, 0) = 1 -> s_fishing_grade_Lv1; the restored client shows "Freshwater / Fishing - Beginner I"
        m.Field.MapId = 2000001;
        var f = c.GetUserData("getTooltip", new object[] { "m_fieldname" });
        var line = Ms2.Quest.ClientFormat.Format(m.Tables.Common("s_worldmap_tooltip_fishingspot_low"),
            m.Tables.Common("s_fishing_habitat_water"), m.Tables.Common("s_fishing_grade_Lv1"));
        Assert.That(f["text"], Is.EqualTo((string)d["text"] + "\n" + line));
        var fishing = TooltipTests.Draw(f);
        TestContext.WriteLine("m_fieldname with spot: " + string.Join(" | ", fishing));
        Assert.That(string.Join(" ", fishing), Does.Contain("Freshwater").And.Contain("Beginner I"));
        Assert.That(m.Tables.Fishing.Grade(0), Is.EqualTo(1));
        Assert.That(m.Tables.Fishing.Grade(80), Is.EqualTo(2));
    }

    [Test]
    public void QuestGuideBuildsTrackedQuests()
    {
        var m = Model();
        // synthetic quests: 1 and 2 in progress and tracked, 3 in progress and not tracked
        var t = new Ms2.Quest.QuestTable("KR", _ => true) { Common = k => k == "s_quest_name_with_level" ? "{0} (Lv. {1})" : "" };
        t.AddQuests(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest id=""1""><basic questType=""0"" standardLevel=""3"" /><condition type=""npc"" code=""7"" value=""1"" /></quest>
            <quest id=""2""><basic questType=""1"" standardLevel=""4"" /><condition type=""npc"" code=""7"" value=""2"" /></quest>
            <quest id=""3""><basic questType=""1"" standardLevel=""5"" /><condition type=""npc"" code=""7"" value=""1"" /></quest></ms2>"));
        t.BuildIndexes();
        t.AddDescriptions(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest questID=""1"" name=""First"" /><quest questID=""2"" name=""Second"" /><quest questID=""3"" name=""Third"" /></ms2>"));
        var state = new Ms2.Quest.QuestShotState(t, 10, new[] { 1, 2 }, tracking: 1);
        var c = new QuestGuideController { Table = t, State = state };
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        var container = h.Get(h.Entity, "m_dragContainer") as AsContainer;
        Assert.That(container, Is.Not.Null);
        Assert.That(container.Children.Count, Is.EqualTo(2), "one entity per tracked quest in progress");
        // shown at gotoAndStop(11): the "show" slide's last move (an identity matrix) puts the body back at x 0
        foreach (AsContainer e in container.Children) Assert.That(e.Children[0].x, Is.EqualTo(0));
        var texts = AllTexts(container);
        Assert.That(texts, Has.Some.Contains("First (Lv. 3)"));
        Assert.That(texts, Has.Some.Contains("Second (Lv. 4)"));
        Assert.That(texts, Has.None.Contains("Third"));
    }

    /// <summary>A quest state the test changes, as the live quest book does.</summary>
    sealed class ChangingQuests : Ms2.Quest.IQuestState
    {
        public readonly List<Ms2.Quest.QuestRecord> List = new();
        public IReadOnlyList<Ms2.Quest.QuestRecord> Records => List.OrderBy(r => r.Id).ToList();
        public Ms2.Quest.QuestRecord Get(int questId) => List.Find(r => r.Id == questId);
        public void Set(Ms2.Quest.QuestRecord r) { List.RemoveAll(x => x.Id == r.Id); List.Add(r); }
        public bool CanStart(int questId) => false;
        public int PlayerLevel => 10;
        public long CharacterId => 0;
        public bool NpcFieldOpen(int npcId, int fieldId) => false;
    }

    [Test]
    public void QuestGuideFollowsTheQuestEvents()
    {
        // UIQuestGuideDialog as a UIQuestEventListener (uiquesteventlistener.cpp:613-669, vfunc_15 :424): a quest started after
        // the tracker loaded is queued and added on the next frame, a rewarded one queued for removal, a removed one goes at
        // once, the tracking flag adds or removes, and a rebuild lists the state again
        var m = Model();
        var t = new Ms2.Quest.QuestTable("KR", _ => true) { Common = k => k == "s_quest_name_with_level" ? "{0} (Lv. {1})" : "" };
        t.AddQuests(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest id=""1""><basic questType=""0"" standardLevel=""3"" /><condition type=""npc"" code=""7"" value=""1"" /></quest>
            <quest id=""2""><basic questType=""1"" standardLevel=""4"" /><condition type=""npc"" code=""7"" value=""2"" /></quest>
            <quest id=""3""><basic questType=""1"" standardLevel=""5"" /><condition type=""npc"" code=""7"" value=""1"" /></quest>
            <quest id=""70000001""><basic questType=""3"" standardLevel=""0"" /><condition type=""level"" value=""78"" /></quest></ms2>"));
        t.BuildIndexes();
        t.AddDescriptions(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest questID=""1"" name=""First"" /><quest questID=""2"" name=""Second"" /><quest questID=""3"" name=""Third"" />
            <quest questID=""70000001"" name=""Reach Level 78"" /></ms2>"));
        static Ms2.Quest.QuestRecord Q(int id, int state, int tracking, long started) => new(id, state, started, 0, tracking, false, false, new long[] { 0 });
        var state = new ChangingQuests();
        state.Set(Q(1, 1, 1, 10));
        var notices = new List<int>();
        var c = new QuestGuideController { Table = t, State = state, Notice = notices.Add };
        var h = Load(c, m);
        var container = (AsContainer)h.Get(h.Entity, "m_dragContainer");
        List<string> Shown() => AllTexts(container);
        // removeEntity takes the entry out of m_entities at once and plays the clip's "hide" before it leaves the stage
        int Listed() => container.Children.Count(e => Rt.ToString(h.Get(e, "currentLabel")) != "hide");
        Assert.That(Listed(), Is.EqualTo(1));

        state.Set(Q(2, 1, 1, 20));
        c.OnQuestStarted(2);
        Assert.That(Listed(), Is.EqualTo(1), "queued: added on the frame");
        c.Tick();
        Assert.That(Listed(), Is.EqualTo(2), "a quest started after the load is listed");
        Assert.That(Shown(), Has.Some.Contains("Second (Lv. 4)"));

        state.Set(Q(3, 1, 0, 30));
        c.OnQuestStarted(3);
        state.Set(Q(70000001, 1, 1, 40));
        c.OnQuestStarted(70000001);
        c.Tick();
        Assert.That(Listed(), Is.EqualTo(2), "an untracked quest and a mission type quest stay out");

        state.Set(Q(2, 1, 1, 20) with { Complete = true, Progress = new long[] { 2 } });
        c.OnQuestProgress(2);
        Assert.That(h.RT.Errors, Is.Empty);

        state.Set(Q(2, 2, 1, 20));
        c.OnQuestRewarded(2);
        c.Tick();
        Assert.That(Listed(), Is.EqualTo(1), "a rewarded quest leaves on the frame");
        c.OnQuestTracking(2);
        c.OnQuestResult(2, 0);
        c.OnQuestResult(99, 3);
        Assert.That(Listed(), Is.EqualTo(1), "AddQuestEntity skips a rewarded quest and one the character does not have");

        state.List.RemoveAll(r => r.Id == 1);
        c.OnQuestRemoved(1);
        Assert.That(Listed(), Is.EqualTo(0), "a removed quest leaves at once");

        state.Set(Q(3, 1, 1, 30));
        c.OnQuestTracking(3);
        Assert.That(Listed(), Is.EqualTo(1));
        Assert.That(Shown(), Has.Some.Contains("Third (Lv. 5)"));
        state.Set(Q(3, 1, 0, 30));
        c.OnQuestTracking(3);
        Assert.That(Listed(), Is.EqualTo(0));

        state.Set(Q(1, 1, 1, 10));
        state.Set(Q(3, 1, 1, 30));
        c.QueueRebuild();
        c.Tick();
        Assert.That(Listed(), Is.EqualTo(2), "the rebuild lists the tracked quests in progress");
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(notices, Is.Empty);
    }

    [Test]
    public void QuestGuideListsAQuestStartedBeforeItsMovieOnceAndKeepsAForcedRemoval()
    {
        // a start before the movie is up: queued, listed by the load's BuildAll, then added again on the next frame; the movie's
        // createEntity takes the entity of that id off first, so the quest is listed once
        var m = Model();
        var t = new Ms2.Quest.QuestTable("KR", _ => true) { Common = k => k == "s_quest_name_with_level" ? "{0} (Lv. {1})" : "" };
        t.AddQuests(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest id=""1""><basic questType=""0"" standardLevel=""3"" /><condition type=""npc"" code=""7"" value=""1"" /></quest>
            <quest id=""2""><basic questType=""1"" standardLevel=""4"" /><condition type=""npc"" code=""7"" value=""2"" /></quest></ms2>"));
        t.BuildIndexes();
        t.AddDescriptions(System.Xml.Linq.XDocument.Parse(@"<ms2><quest questID=""1"" name=""First"" /><quest questID=""2"" name=""Second"" /></ms2>"));
        static Ms2.Quest.QuestRecord Q(int id, int tracking) => new(id, 1, id * 10, 0, tracking, false, false, new long[] { 0 });
        var state = new ChangingQuests();
        state.Set(Q(1, 1));
        state.Set(Q(2, 1));
        var notices = new List<int>();
        var c = new QuestGuideController { Table = t, State = state, Notice = notices.Add };
        c.OnQuestStarted(2);
        var h = Load(c, m);
        var container = (AsContainer)h.Get(h.Entity, "m_dragContainer");
        int Listed() => container.Children.Count(e => Rt.ToString(h.Get(e, "currentLabel")) != "hide");
        int Named(string name) => AllTexts(container).Count(x => x.Contains(name));
        Assert.That(Listed(), Is.EqualTo(2), "the load lists both tracked quests");
        c.Tick();
        Assert.That(Listed(), Is.EqualTo(2), "the start seen before the load is listed once");
        Assert.That(Named("Second (Lv. 4)"), Is.EqualTo(1));
        Assert.That(notices, Is.Empty);
        // the movie drops one entity per removeEntity: with a second entity of the id, one would stay
        state.List.RemoveAll(r => r.Id == 2);
        c.OnQuestRemoved(2);
        Assert.That(Listed(), Is.EqualTo(1), "no second entity of the quest");
        state.Set(Q(2, 1));
        c.OnQuestTracking(2);
        Assert.That(Listed(), Is.EqualTo(2));

        // OnQuest mode 5 result 0 (the epic restart's forced removal): RemoveQuest(id, 1) then slot 7's AddQuestEntity, which
        // finds nothing; the client leaves the entity listed
        state.List.RemoveAll(r => r.Id == 2);
        c.OnQuestResult(2, 0);
        c.Tick();
        Assert.That(Listed(), Is.EqualTo(2), "mode 5 alone takes no entity off");
        // a give-up is answered with mode 6: slot 5 takes it off at once
        c.OnQuestRemoved(2);
        Assert.That(Listed(), Is.EqualTo(1));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void QuestGuideTickAllocatesNothingWithEmptyQueues()
    {
        // vfunc_15 (uiquesteventlistener.cpp:424) runs every frame: with nothing queued it only checks the three queues
        var m = Model();
        var t = new Ms2.Quest.QuestTable("KR", _ => true) { Common = k => k == "s_quest_name_with_level" ? "{0} (Lv. {1})" : "" };
        t.AddQuests(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest id=""1""><basic questType=""0"" standardLevel=""3"" /><condition type=""npc"" code=""7"" value=""3"" /></quest></ms2>"));
        t.BuildIndexes();
        t.AddDescriptions(System.Xml.Linq.XDocument.Parse(@"<ms2><quest questID=""1"" name=""First"" /></ms2>"));
        var state = new ChangingQuests();
        state.Set(new(1, 1, 10, 0, 1, false, false, new long[] { 0 }));
        var c = new QuestGuideController { Table = t, State = state };
        var h = Load(c, m);
        c.Tick();
        long used = Alloc.Least(() => { for (int i = 0; i < 400; i++) c.Tick(); });
        Assert.That(used, Is.EqualTo(0), "bytes over 400 frames with empty queues");

        // a mode 3 progress packet: one UpdateQuestEntity (the entity's data and updateEntity in the movie)
        long n = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long refresh = Alloc.Least(() =>
        {
            for (int i = 0; i < 50; i++) { state.Set(new(1, 1, 10, 0, 1, false, false, new long[] { ++n % 3 })); c.OnQuestProgress(1); }
        }) / 50;
        sw.Stop();
        TestContext.Out.WriteLine($"quest guide: empty tick {used} B / 400 frames; mode 3 refresh {refresh} B each, {sw.Elapsed.TotalMilliseconds / 250:F3} ms each (5 windows of 50)");
        // measured 129992 B on both data sets: the entity data and the movie's updateEntity redraw
        Assert.That(refresh, Is.LessThan(192 * 1024), "bytes for one progress refresh");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void QuestGuideSkipsMissionTypeQuests()
    {
        // CQuestManager::AddQuest (questmanager_2.cpp:843) keeps quest types 3, 4, 5, 7 and 10 (the missions, which includes the
        // level-up package's type 3) in their own maps; the guide lists m_quests20 only (GetQuestsFiltered, :915)
        var m = Model();
        var t = new Ms2.Quest.QuestTable("KR", _ => true) { Common = k => k == "s_quest_name_with_level" ? "{0} (Lv. {1})" : "" };
        t.AddQuests(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest id=""1""><basic questType=""0"" standardLevel=""3"" /><condition type=""npc"" code=""7"" value=""1"" /></quest>
            <quest id=""70000001""><basic questType=""3"" standardLevel=""0"" /><condition type=""level"" value=""78"" /></quest></ms2>"));
        t.BuildIndexes();
        t.AddDescriptions(System.Xml.Linq.XDocument.Parse(@"<ms2><quest questID=""1"" name=""First"" /><quest questID=""70000001"" name=""Reach Level 78"" /></ms2>"));
        var state = new Ms2.Quest.QuestShotState(t, 10, new[] { 1, 70000001 }, tracking: 1);
        var c = new QuestGuideController { Table = t, State = state };
        var h = Load(c, m);
        var container = (AsContainer)h.Get(h.Entity, "m_dragContainer");
        Assert.That(container.Children.Count, Is.EqualTo(1));
        var texts = AllTexts(container);
        Assert.That(texts, Has.Some.Contains("First (Lv. 3)"));
        Assert.That(texts, Has.None.Contains("Reach Level"));
    }

    [Test]
    public void QuestGuideSkipsTheGms2LevelUpPackageQuests()
    {
        // the GMS2 quest files hold the level-up package as type 3 quests (70000001 and up, chapter 0, standardLevel 0)
        var data = System.Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(data) || !System.IO.Directory.Exists(data)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var t = Ms2.Quest.QuestTable.Load(new Ms2.Formats.Pack.PackSet(data), new[] { "en" }, "NA", _ => true);
        Assert.That(t.Quest(70000001)?.Type, Is.EqualTo(3));
        Assert.That(Ms2.Quest.QuestCommon.InMainQuestMap(t.Quest(70000001)), Is.False);
        Assert.That(Ms2.Quest.QuestCommon.InMainQuestMap(t.Quest(10001172)), Is.True);
        var state = new Ms2.Quest.QuestShotState(t, 10, new[] { 10001172, 70000001 }, tracking: 1);
        var c = new QuestGuideController { Table = t, State = state };
        var h = Load(c, Model());
        var container = (AsContainer)h.Get(h.Entity, "m_dragContainer");
        Assert.That(container.Children.Count, Is.EqualTo(1));
    }

    [Test]
    public void MessengerTabsTargetsAndKeys()
    {
        var m = Model();
        Assert.That(m.Tables.DefaultMessengers, Is.Not.Empty);
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // defaultmessenger0.xml: two tabs, 408 x 270, bottom left at offset (0, -38)
        Assert.That(c.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "$messenger_tab_name_entire", "$messenger_tab_name_item" }));
        var bar = (AsObject)h.Child("m_buttonBar");
        Assert.That(Rt.ToNumber(h.Get(h.Get(bar, "dataProvider"), "length")), Is.EqualTo(2));
        Assert.That(Rt.ToNumber(h.Get(bar, "selectedIndex")), Is.EqualTo(0));
        double x = Rt.ToNumber(h.Get(h.Entity, "x")), y = Rt.ToNumber(h.Get(h.Entity, "y")), ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(x, Is.EqualTo(0));
        Assert.That(y, Is.EqualTo(1080 - (int)ht - 38));
        // the target list without feature 0x19: world, channel, social, party, guild, guild megaphone; social selected
        var subject = (AsObject)h.Child("m_subject");
        Assert.That(Rt.ToNumber(h.Get(h.Get(subject, "dataProvider"), "length")), Is.EqualTo(6));
        Assert.That(Rt.ToNumber(h.Get(subject, "selectedIndex")), Is.EqualTo(2));
        Assert.That(Rt.ToNumber(h.Get(subject, "menuRowCount")), Is.EqualTo(6));
        // Return focuses the input; Return again with text releases it and clears it (no local player: nothing is sent)
        var input = (AsObject)h.Child("m_textInput");
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(1));
        h.Set(h.Get(input, "textField"), "text", "HelloWorld");
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(0));
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo(""));
        // Escape is used only while the input is focused
        Assert.That(c.OnKey(0, true), Is.False);
        c.OnKey(MessengerController.DikReturn, false);
        Assert.That(c.OnKey(0, true), Is.True);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test, NonParallelizable]   // raises the process-wide loading cover
    public void ChatKeysWaitWhileInputIsBlocked()
    {
        // UIMessenger::FUN_141a12f30 (uimessenger.cpp:1604): an unfocused input takes no key while
        // CInputKeyHandler::FUN_14116d7a0(0xf) is set (:1613), here the loading screen's flag; a focused input still does
        var m = Model();
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        var input = (AsObject)h.Child("m_textInput");
        try
        {
            FieldMoveCover.Raise();
            Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.False, "Return under the loading screen");
            Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(0));
            FieldMoveCover.Lower();
            Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True, "Return once the screen is down");
            Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(1));
            FieldMoveCover.Raise();
            Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True, "Return with the input focused");
            Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(0));
        }
        finally { FieldMoveCover.Lower(); }
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void AFocusedChatInputIsTheStagesTextFieldFocus()
    {
        // CUIService::IsTextFieldFocused (uiservice.cpp:1364) asks main.gfx's IME manager, so the chat's focused TextInput
        // is FUN_14116d7a0's mask 2; sending releases the focus (ASUIComponent::SetFocused 0, stage.focus = null)
        var m = Model();
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        var stage = new UiStage();
        stage.Add(h.Player, () => (0, 0), () => 0);
        var input = (AsObject)h.Child("m_textInput");
        Assert.That(stage.TextFieldFocused, Is.False);
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(h.Player.Focus, Is.SameAs(h.Get(input, "textField")));
        Assert.That(stage.TextFieldFocused, Is.True, "Return focused the input");
        h.Set(h.Get(input, "textField"), "text", "HelloWorld");
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(stage.TextFieldFocused, Is.False, "Return with text released it");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void AClickAwayFromTheChatInputReleasesTheTextFieldFocus()
    {
        // a left press moves main.gfx's focus to the top-most entity (MovieImpl, sfr_gfx_01i.cpp:3673), NULL for the empty
        // stage (QueueSetFocusTo, :2672), so the chat's TextInput loses it and FUN_14116d7a0's mask 2 clears
        var m = Model();
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        var stage = new UiStage();
        stage.Add(h.Player, () => (0, 0), () => 0);
        var input = (AsObject)h.Child("m_textInput");
        Assert.That(c.OnKey(MessengerController.DikReturn, false), Is.True);
        Assert.That(stage.TextFieldFocused, Is.True, "Return focused the input");
        double w = Rt.ToNumber(h.Get(h.Player.Stage, "stageWidth")), hh = Rt.ToNumber(h.Get(h.Player.Stage, "stageHeight"));
        h.Player.MouseMove(w - 2, 2);
        h.Player.MouseButton(true);
        h.Player.MouseButton(false);
        Assert.That(stage.TextFieldFocused, Is.False, $"a press at ({w - 2}, 2) of {w}x{hh}");
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(0), "the TextInput's focused");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    /// <summary>A loaded chat window with a local player "Me", its log's sends and notices collected.</summary>
    static (MessengerController c, UiHost h, UiModel m, List<UserChatRequest> sent, List<string> notices) ChatWindow()
    {
        var m = Model();
        m.HasLocalPlayer = true;
        m.Player.Name = "Me";
        var log = new MessengerBrowser { Model = m };
        var sent = new List<UserChatRequest>();
        var notices = new List<string>();
        log.SendPacket = sent.Add;
        log.ShowSystemNotice = (t, _, _) => notices.Add(t);
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true, log);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        return (c, h, m, sent, notices);
    }

    [Test]
    public void MessengerSendsTheTypedLineToTheSelectedTarget()
    {
        var (c, h, m, sent, _) = ChatWindow();
        var input = (AsObject)h.Child("m_textInput");
        c.OnKey(MessengerController.DikReturn, false);
        // typed into the component's text field, where key presses land
        var field = h.Get(input, "textField");
        h.Set(field, "text", "  hello <b> ");
        c.OnKey(MessengerController.DikReturn, false);
        // social (0) is selected: kind 0, the text trimmed and escaped (FUN_141842320); the input released
        Assert.That(sent, Is.EqualTo(new[] { new UserChatRequest(0, "hello &lt;b&gt;", "", 0) }));
        Assert.That(c.Browser.LastChannel, Is.EqualTo(1));
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(0));
        // an empty line sends nothing
        c.OnKey(MessengerController.DikReturn, false);
        h.Set(h.Get(input, "textField"), "text", "   ");
        c.OnKey(MessengerController.DikReturn, false);
        Assert.That(sent.Count, Is.EqualTo(1));
        // no local player: SendChat sends nothing
        m.HasLocalPlayer = false;
        c.SubmitLine("hi", "");
        Assert.That(sent.Count, Is.EqualTo(1));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerItemLinksColourAndTooltip()
    {
        var (c, h, m, _, _) = ChatWindow();
        c.Browser.OnItemLinks(new ChatItemLinks(new[] { new EquipItem { Id = 15000001, Uid = 101, Rarity = 3, Amount = 1 } }));
        Assert.That(c.Browser.LinkedItem(101)?.ItemId, Is.EqualTo(15000001));
        // the uid is the link's second ","-separated number; FindLinks takes each "<A HREF=" through the next "/A>"
        string link = "<A HREF='event:itemTooltip,101,15000001,3,0'>[Item]</A>", other = "<A HREF='event:itemTooltip,999,1,1,0'>[X]</A>";
        Assert.That(MessengerBrowser.LinkUid(link), Is.EqualTo(101));
        Assert.That(MessengerBrowser.LinkUid("<a,b>"), Is.EqualTo(0));
        Assert.That(MessengerBrowser.FindLinks($"x{link}y{other}", "<A HREF=", "/A>"), Is.EqualTo(new[] { link, other }));
        Assert.That(c.Browser.ColorItemLinks($"x{link}y{other}"),
            Is.EqualTo($"x<FONT color='#{m.Tables.Items.TextColor(3):x}'>{link}</FONT>y<FONT color='#ffffff'>{other}</FONT>"));
        Assert.That(c.Browser.ColorItemLinks("plain"), Is.EqualTo("plain"));
        // getUserData "getTooltip": the linked item's tooltip with its close button; nothing for an unlinked uid
        var tip = c.GetUserData("getTooltip", new object[] { "101" });
        Assert.That(tip, Is.Not.Null);
        Assert.That(tip["closeButton"], Is.EqualTo(true));
        Assert.That(c.GetUserData("getTooltip", new object[] { "999" }), Is.Null);
        Assert.That(c.GetUserData("itemPreview", new object[] { "101" }), Is.Null);
        m.HasLocalPlayer = false;
        Assert.That(c.GetUserData("getTooltip", new object[] { "101" }), Is.Null);
        c.Browser.Clear();
        Assert.That(c.Browser.LinkedItem(101), Is.Null);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerItemLinkIntoTheInputIsSentAsHtml()
    {
        var (c, h, m, sent, _) = ChatWindow();
        var input = (AsObject)h.Child("m_textInput");
        var held = Item.Create(m.Tables.Items, 15000001, 3);
        held.Uid = 7;
        int r = held.Rarity;
        c.Browser.FindHeldItem = uid => uid == 7 ? held : null;
        string name = m.Tables.ItemName(15000001), color = $"#{m.Tables.Items.TextColor(r):x}";
        string href = $"'event:itemTooltip,7,3,7,15000001,{r}'";
        // FUN_140d2e2b0 / FUN_140d2ef00: the link markup, "&" escaped in the name
        string link = c.Browser.ItemLink(15000001, r, 7);
        Assert.That(link, Is.EqualTo($"<a href={href}><FONT color='{color}'>[{name}]</FONT></a>"));
        Assert.That(MessengerBrowser.LinkMarkup("'h'", "#1", "a&b"), Is.EqualTo("<a href='h'><FONT color='#1'>[a&amp;b]</FONT></a>"));
        // FUN_141a24fb0 -> AppendToInput: the link and a space after the input's html, the caret after it, the link kept
        h.Set(h.Get(input, "textField"), "text", "look");
        c.Browser.LinkItem(held);
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo($"look[{name}] "));
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.Not.EqualTo(0));
        Assert.That(c.InputLinks, Is.EqualTo(new[] { link }));
        // Return: SubmitLine sends the html (it has a link) without its P, BR, TEXTFORMAT and FONT tags
        c.OnKey(MessengerController.DikReturn, false);
        Assert.That(sent.Count, Is.EqualTo(1));
        string a = $"<A HREF=\"event:itemTooltip,7,3,7,15000001,{r}\">[{name}]</A>";
        Assert.That(sent[0].Message, Is.EqualTo($"look{a} "));
        Assert.That(MessengerController.HasChatLink(sent[0].Message), Is.True);
        Assert.That(c.InputLinks, Is.Empty, "released with the input");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ChatInputHistoryUpAndDown()
    {
        // FUN_141a11e30 -> FUN_141a0f1d0(bHistory 1) -> FUN_141a25f70: a sent line joins the browser's history (ten at most,
        // the last text not twice); FUN_141a13c20 (vfunc_13 :810, keys 0x79 / 0x7e, input focused) puts the entry in the input
        var (c, h, m, sent, _) = ChatWindow();
        var input = (AsObject)h.Child("m_textInput");
        string Text() => Rt.ToString(h.Get(input, "text"));
        void Type(string t)
        {
            c.OnKey(MessengerController.DikReturn, false);              // focuses the input
            h.Set(h.Get(input, "textField"), "text", t);
        }
        Assert.That(c.OnKey(MessengerController.DikUp, false), Is.False, "Up with the input not focused");
        for (int i = 1; i <= 12; i++) { Type("line" + i); c.OnKey(MessengerController.DikReturn, false); }
        Type("line12"); c.OnKey(MessengerController.DikReturn, false);                      // the same text again: one entry
        Assert.That(sent.Count, Is.EqualTo(13));
        Assert.That(c.Browser.History.Count, Is.EqualTo(MessengerBrowser.MaxHistory));
        Assert.That(c.Browser.History[0].Text, Is.EqualTo("line3"), "the two oldest went");
        Assert.That(c.Browser.History[^1].Text, Is.EqualTo("line12"));
        Type("");
        Assert.That(c.OnKey(MessengerController.DikUp, false), Is.True);
        Assert.That(Text(), Is.EqualTo("line12"));
        c.OnKey(MessengerController.DikUp, false);
        Assert.That(Text(), Is.EqualTo("line11"));
        for (int i = 0; i < 20; i++) c.OnKey(MessengerController.DikUp, false);              // none past the first entry
        Assert.That(Text(), Is.EqualTo("line3"));
        c.OnKey(MessengerController.DikDown, false);
        Assert.That(Text(), Is.EqualTo("line4"));
        for (int i = 0; i < 20; i++) c.OnKey(MessengerController.DikDown, false);
        Assert.That(Text(), Is.EqualTo(""), "down onto the end empties the input; none beyond it");
        // eventFocusOut (vfunc_91) puts the position back to the end: Up gives the last line again
        c.OnCallback("eventFocusOut", new object[0]);
        c.OnKey(MessengerController.DikUp, false);
        c.OnKey(MessengerController.DikUp, false);
        c.OnCallback("eventFocusOut", new object[0]);
        c.OnKey(MessengerController.DikUp, false);
        Assert.That(Text(), Is.EqualTo("line12"));
        // Return sends the entry again and does not add it a second time
        c.OnKey(MessengerController.DikReturn, false);
        Assert.That(sent[^1].Message, Is.EqualTo("line12"));
        Assert.That(c.Browser.History.Count, Is.EqualTo(MessengerBrowser.MaxHistory));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ChatTabKeyStepsToTheNextTarget()
    {
        // FUN_141a14760 (:1958), vfunc_13 (:810) key 0xf with the input focused: the list's next row, the first after the last
        // (menuRowCount rows), then SelectSubject. Rows without feature 0x19: world, channel, social, party, guild, megaphone.
        var (c, h, m, sent, _) = ChatWindow();
        var subject = (AsObject)h.Child("m_subject");
        var input = (AsObject)h.Child("m_textInput");
        int Sel() => (int)Rt.ToNumber(h.Get(subject, "selectedIndex"));
        Assert.That(Sel(), Is.EqualTo(2));
        Assert.That(c.OnKey(MessengerController.DikTab, false), Is.False, "Tab with the input not focused");
        Assert.That(Sel(), Is.EqualTo(2));
        c.OnKey(MessengerController.DikReturn, false);                      // focuses the input
        var seen = new List<int>();
        for (int i = 0; i < 7; i++) { Assert.That(c.OnKey(MessengerController.DikTab, false), Is.True); seen.Add(Sel()); }
        Assert.That(seen, Is.EqualTo(new[] { 3, 4, 5, 0, 1, 2, 3 }), "wraps after the last row");
        // vfunc_93 follows: the command word of a "/" line goes, the rest stays; a line without one is kept
        h.Set(h.Get(input, "textField"), "text", "/w hello");
        c.OnKey(MessengerController.DikTab, false);
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo("hello"));
        Assert.That(Sel(), Is.EqualTo(4));
        c.OnKey(MessengerController.DikTab, false);
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo("hello"));
        Assert.That(Sel(), Is.EqualTo(5));
        Assert.That(sent, Is.Empty, "Tab sends nothing");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ChatTabKeySkipsTheSuperRowWithoutASuperChatItem()
    {
        // FUN_141a14760: a next row of type 0x10 with no super chat item (FUN_140a5e2e0) selects the world (0xb), or the first
        // type when the world is hidden. Rows with feature 0x19: super, world, channel, social, party, guild, megaphone.
        var m = Model();
        m.FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f);
        var log = new MessengerBrowser { Model = m };
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true, log);
        var h = Load(c, m);
        var subject = (AsObject)h.Child("m_subject");
        int Sel() => (int)Rt.ToNumber(h.Get(subject, "selectedIndex"));
        c.OnKey(MessengerController.DikReturn, false);
        Assert.That(Sel(), Is.EqualTo(3));
        var seen = new List<int>();
        for (int i = 0; i < 5; i++) { c.OnKey(MessengerController.DikTab, false); seen.Add(Sel()); }
        Assert.That(seen, Is.EqualTo(new[] { 4, 5, 6, 1, 2 }), "past the last row the super row is skipped to the world row");
        log.HasSuperChatItem = () => true;
        for (int i = 0; i < 5; i++) c.OnKey(MessengerController.DikTab, false);             // 3 4 5 6 0
        Assert.That(Sel(), Is.EqualTo(0), "with a super chat item the super row is taken");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ChatSpaceKeyActsOnTheSlashCommandTypedSoFar()
    {
        // vfunc_13 (:834) key 0x39 with a text field focused: FUN_141a14600 (:1922) on GetInputChatType (:1969). A target type
        // selects it and drops the command word (SelectSubject, vfunc_93), a whisper (4) starts "whisper command + last target",
        // a talk (0x14) the same with the club command; any other line is left. The key is used either way.
        var (c, h, m, sent, _) = ChatWindow();
        var subject = (AsObject)h.Child("m_subject");
        var input = (AsObject)h.Child("m_textInput");
        int Sel() => (int)Rt.ToNumber(h.Get(subject, "selectedIndex"));
        string Text() => Rt.ToString(h.Get(input, "text"));
        void Type(string t) => h.Set(h.Get(input, "textField"), "text", t);
        Assert.That(c.OnKey(MessengerController.DikSpace, false), Is.False, "Space with the input not focused");
        c.OnKey(MessengerController.DikReturn, false);
        Type("/p");
        Assert.That(c.OnKey(MessengerController.DikSpace, false), Is.True);
        Assert.That(Sel(), Is.EqualTo(3), "the party row");
        Type("/p ");
        c.OnKey(MessengerController.DikSpace, false);
        Assert.That(Text(), Is.EqualTo(""), "the command word goes");
        Type("/p hello");
        c.OnKey(MessengerController.DikSpace, false);
        Assert.That(Text(), Is.EqualTo("/p hello"), "ChatType looks the whole trimmed text up: no alias, left alone");
        c.Browser.WhisperTarget = "Bob";
        Type("/w");
        c.OnKey(MessengerController.DikSpace, false);
        Assert.That(Text(), Is.EqualTo(m.Tables.Common(0xaa5) + " Bob "));
        c.Browser.WhisperTarget = "";
        Type("/w");
        c.OnKey(MessengerController.DikSpace, false);
        Assert.That(Text(), Is.EqualTo(m.Tables.Common(0xaa5) + " "), "no target yet");
        Type("plain words");
        int before = Sel();
        Assert.That(c.OnKey(MessengerController.DikSpace, false), Is.True, "used, nothing else happens");
        Assert.That(Text(), Is.EqualTo("plain words"));
        Assert.That(Sel(), Is.EqualTo(before));
        Type("/");
        c.OnKey(MessengerController.DikSpace, false);
        Assert.That(Text(), Is.EqualTo("/"), "under two characters");
        Assert.That(sent, Is.Empty);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ChatWhisperActionKeyStartsAWhisperWhenTheInputIsNotFocused()
    {
        // vfunc_13 (:823): the whisper action (0x49405, the H key in defaultkey.xml) with the input not focused focuses it and
        // BeginWhisper(last whisper target); with the input focused the key is not used and the letter goes to the field.
        var (c, h, m, sent, _) = ChatWindow();
        var input = (AsObject)h.Child("m_textInput");
        Assert.That(m.Keys.KeysOfAction(MessengerController.ActionWhisper), Is.EqualTo(new[] { 35 }));
        c.Browser.WhisperTarget = "Bob";
        Assert.That(c.OnKey(35, false), Is.True);
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.Not.EqualTo(0.0));
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo(m.Tables.Common(0xaa5) + " Bob "));
        h.Set(h.Get(input, "textField"), "text", "hi");
        Assert.That(c.OnKey(35, false), Is.False, "focused: the letter is typed");
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo("hi"));
        Assert.That(c.OnKey(36, false), Is.False, "another key");
        Assert.That(sent, Is.Empty);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ClearingTheChatBrowserClearsTheInputHistoryAndTheTargets()
    {
        // FUN_141a23c20 (:3114): m_history and m_itHistory, the whisper target, the talk target, m_1b0 and m_reports go with the log
        var (c, h, m, sent, _) = ChatWindow();
        c.Browser.RecordHistory("one", System.Array.Empty<string>());
        c.Browser.RecordHistory("two", System.Array.Empty<string>());
        c.Browser.WhisperTarget = "Bob";
        c.Browser.TalkTarget = "Club";
        c.Browser.LastChannel = 4;
        c.Browser.Clear();
        Assert.That(c.Browser.History, Is.Empty);
        Assert.That(c.Browser.GetHistoryEntry(true, out _), Is.False, "Up on an empty history");
        Assert.That(c.Browser.WhisperTarget, Is.EqualTo(""));
        Assert.That(c.Browser.TalkTarget, Is.EqualTo(""));
        Assert.That(c.Browser.LastChannel, Is.EqualTo(0));
        Assert.That(c.Browser.Reports, Is.Empty);
    }

    [Test]
    public void ChatInputHistoryKeepsTheLinksAndTheCommands()
    {
        var (c, h, m, sent, _) = ChatWindow();
        var input = (AsObject)h.Child("m_textInput");
        var held = Item.Create(m.Tables.Items, 15000001, 3);
        held.Uid = 7;
        c.Browser.FindHeldItem = uid => uid == 7 ? held : null;
        string link = c.Browser.ItemLink(15000001, held.Rarity, 7);
        c.Browser.LinkItem(held);
        c.OnKey(MessengerController.DikReturn, false);
        Assert.That(c.InputLinks, Is.Empty);
        Assert.That(c.Browser.History.Count, Is.EqualTo(1));
        Assert.That(c.Browser.History[0].Links, Is.EqualTo(new[] { link }), "the links the input held");
        Assert.That(MessengerController.HasChatLink(c.Browser.History[0].Text), Is.True, "a line with links is kept as its html");
        // Up puts the line and its links back, so the link survives eventTextInput's removal of typed links
        c.OnKey(MessengerController.DikReturn, false);
        c.OnKey(MessengerController.DikUp, false);
        Assert.That(c.InputLinks, Is.EqualTo(new[] { link }));
        c.OnCallback("eventTextInput", new object[0]);
        Assert.That(Rt.ToString(h.Get(input, "htmlText")), Does.Contain("itemTooltip,7,"));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerDropsHandTypedLinks()
    {
        var (c, h, m, sent, _) = ChatWindow();
        var input = (AsObject)h.Child("m_textInput");
        c.Browser.LinkItem(15000001, 3, 7);
        string html = Rt.ToString(h.Get(input, "htmlText"));
        // a typed link of another uid (FUN_140d2c2a0): removed on eventTextInput; the kept one stays
        string typed = "<A HREF=\"event:itemTooltip,8,3,8,15000001,3\">[x]</A>";
        h.Set(input, "htmlText", html + typed);
        Assert.That(c.OnCallback("eventTextInput", new object[0]), Is.True);
        string after = Rt.ToString(h.Get(input, "htmlText"));
        Assert.That(after, Does.Not.Contain("itemTooltip,8,"));
        Assert.That(after, Does.Contain("itemTooltip,7,"));
        Assert.That(MessengerBrowser.IsListedLink("<A HREF=\"event:itemTooltip,7,3\">[n]</A>", new[] { "<a href='event:itemTooltip,7,3'><FONT color='#1'>[n]</FONT></a>" }), Is.True);
        Assert.That(MessengerBrowser.IsListedLink("<A HREF=\"event:itemTooltip,7,3\">[m]</A>", new[] { "<a href='event:itemTooltip,7,3'><FONT color='#1'>[n]</FONT></a>" }), Is.False);
        // FUN_141a14ce0: the length without tags
        Assert.That(MessengerController.FitsWithoutTags("<a>abc</a>", 3), Is.True);
        Assert.That(MessengerController.FitsWithoutTags("<a>abcd", 3), Is.False);
        Assert.That(MessengerController.FitsWithoutTags("ab<c", 3), Is.False, "an unclosed '<' stays");
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerWhisperCommand()
    {
        var (c, h, m, sent, notices) = ChatWindow();
        c.SubmitLine("/w Bob hi there", "");
        Assert.That(sent, Is.EqualTo(new[] { new UserChatRequest(4, "hi there", "Bob", 0) }));
        Assert.That((c.Browser.LastChannel, c.Browser.WhisperTarget), Is.EqualTo((4, "Bob")));
        // Return on the unfocused input after a whisper starts the next one: "{0} {1} " of common 0xaa5 and the target
        var input = (AsObject)h.Child("m_textInput");
        c.OnKey(MessengerController.DikReturn, false);
        Assert.That(Rt.ToNumber(h.Get(input, "focused")), Is.EqualTo(1));
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo(m.Tables.Common(0xaa5) + " Bob "));
        // "/w" alone: the rest of the line (CStringW::Mid(-1) is the whole line) is not empty but the target is: notice 0x4e
        c.SubmitLine("/w", "");
        Assert.That(notices, Is.EqualTo(new[] { m.Tables.Common(0x4e) }));
        // a target name but no message sends nothing, nor does a quoted empty target
        c.SubmitLine("/w Bob", "");
        Assert.That(sent.Count, Is.EqualTo(1));
        // to oneself (any case): the system line 0x430, not sent
        int logs = c.Browser.Logs.Count;
        c.SubmitLine("/w me hello", "");
        Assert.That(sent.Count, Is.EqualTo(1));
        Assert.That(c.Browser.Logs.Count, Is.EqualTo(logs + 1));
        Assert.That(c.Browser.Logs.Last().Msg, Does.Contain(m.Tables.Common(0x430)));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerTargetCommandsAndPartyGuildRouting()
    {
        var (c, h, m, sent, notices) = ChatWindow();
        var subject = (AsObject)h.Child("m_subject");
        var input = (AsObject)h.Child("m_textInput");
        // "/p" selects the party target (world, channel, social, party: row 3) and keeps the rest of the line
        c.OnKey(MessengerController.DikReturn, false);
        c.SubmitLine("/p", "");
        Assert.That(Rt.ToNumber(h.Get(subject, "selectedIndex")), Is.EqualTo(3));
        Assert.That(sent, Is.Empty);
        // not in a party: notice 0x335; in one: kind 7
        c.SubmitLine("hi", "");
        Assert.That(notices, Is.EqualTo(new[] { m.Tables.Common(0x335) }));
        c.Browser.IsInParty = () => true;
        c.SubmitLine("hi", "");
        Assert.That(sent, Is.EqualTo(new[] { new UserChatRequest(7, "hi", "", 0) }));
        Assert.That(c.Browser.LastChannel, Is.EqualTo(2));
        // guild without a guild: notice 0x388
        c.SendChat(MessengerController.Guild, "hi", "", 0);
        Assert.That(notices.Last(), Is.EqualTo(m.Tables.Common(0x388)));
        c.Browser.IsInGuild = () => true;
        c.SendChat(MessengerController.Guild, "hi", "", 0);
        Assert.That(sent.Last(), Is.EqualTo(new UserChatRequest(8, "hi", "", 0)));
        // guild megaphone without permission 0x800: notice 0x415
        c.SendChat(MessengerController.GuildMegaphone, "hi", "", 0);
        Assert.That(notices.Last(), Is.EqualTo(m.Tables.Common(0x415)));
        // world and channel go out as their type
        c.SendChat(MessengerController.World, "hi", "", 0);
        c.SendChat(MessengerController.Channel, "hi", "", 0);
        Assert.That(sent.TakeLast(2).Select(s => s.Kind), Is.EqualTo(new[] { 0xb, 0xc }));
        // an unknown club: notice 0x50, nothing sent
        int count = sent.Count;
        c.SubmitLine("/club Nobody hi", "");
        Assert.That(sent.Count, Is.EqualTo(count));
        // an input started with a command loses the command when the target changes (vfunc_93)
        h.Set(h.Get(input, "textField"), "text", "/p hello");
        c.OnCallback("eventChangeTalkTarget", Array.Empty<object>());
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo("hello"));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerUnknownAndFunctionCommandsGoOutAsTheyAre()
    {
        var (c, h, m, sent, _) = ChatWindow();
        // no such command: the whole line, with the selected target's type (social)
        c.SubmitLine("/nosuchcommand hello", "");
        Assert.That(sent, Is.EqualTo(new[] { new UserChatRequest(0, "/nosuchcommand hello", "", 0) }));
        Assert.That(c.Browser.LastChannel, Is.EqualTo(1));
        // a chat type 10 command with a function (Roll): its name replaced by "/" + the function
        var roll = m.Tables.UserCommands.Aliases.Where(a => a.Value.Function == "Roll").Select(a => a.Key).FirstOrDefault();
        Assert.That(roll, Is.Not.Null, "a Roll alias in table/chatcommand.xml");
        c.SubmitLine(roll + " 1", "");
        Assert.That(sent.Last(), Is.EqualTo(new UserChatRequest(0, "/Roll 1", "", 0)));
        // help: the help lines go to the log, nothing is sent
        int n = sent.Count, logs = c.Browser.Logs.Count;
        var help = m.Tables.UserCommands.Aliases.Where(a => a.Value.Function == "Help").Select(a => a.Key).First();
        c.Browser.CommandHooks.ChatLine = c.Browser.AddSystemLine;
        c.SubmitLine(help, "");
        Assert.That(sent.Count, Is.EqualTo(n));
        Assert.That(c.Browser.Logs.Count, Is.EqualTo(logs + 1));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ChatCommandTable()
    {
        var m = Model();
        var u = m.Tables.UserCommands;
        // case-blind aliases; the whisper and party commands' chat types
        Assert.That(u.ChatType("/w"), Is.EqualTo(4));
        Assert.That(u.ChatType("/W"), Is.EqualTo(4));
        Assert.That(u.ChatType("/p"), Is.EqualTo(7));
        Assert.That(u.ChatType("/nosuchcommand"), Is.EqualTo(UserCommands.NoChatType));
        Assert.That(u.Tooltip(), Is.Not.Empty);
    }

    [Test]
    public void ChatSendHelpers()
    {
        Assert.That(MessengerController.HasChatLink("<a href='event:itemTooltip,1'>x</a>"), Is.True);
        Assert.That(MessengerController.HasChatLink("event:itemTooltip"), Is.False, "found past the first character only");
        Assert.That(MessengerController.HasChatLink("plain"), Is.False);
        Assert.That(MessengerController.Escape("<a>"), Is.EqualTo("&lt;a&gt;"));
        int pos = 0;
        Assert.That(UserCommands.Tokenize("/w  Bob hi", " ", ref pos), Is.EqualTo("/w"));
        Assert.That(UserCommands.Tokenize("/w  Bob hi", " ", ref pos), Is.EqualTo("Bob"));
        Assert.That(UserCommands.Mid("/w  Bob hi", pos), Is.EqualTo("hi"));
        pos = 0;
        UserCommands.Tokenize("/w", " ", ref pos);
        Assert.That(UserCommands.Tokenize("/w", " ", ref pos), Is.EqualTo(""));
        Assert.That(pos, Is.EqualTo(-1));
        Assert.That(UserCommands.Mid("/w", pos), Is.EqualTo("/w"));
    }

    [Test]
    public void MessengerSuperChatRowWithTheLiveFeatures()
    {
        var m = Model();
        m.FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f);      // KR Live: SuperWorldChat (0x19) and ChatEmoticon on
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // super, world, channel, social, party, guild, guild megaphone; social still selected (the first of m_subjects)
        var subject = (AsObject)h.Child("m_subject");
        var dp = h.Get(subject, "dataProvider");
        Assert.That(Rt.ToNumber(h.Get(dp, "length")), Is.EqualTo(7));
        Assert.That(Rt.ToString(h.Get(h.Get(dp, "0"), "frame")), Is.EqualTo("super"));
        Assert.That(Rt.ToString(h.Get(h.Get(dp, "0"), "iconPath")), Is.EqualTo("export_common_icon_megaphone6"));
        Assert.That(Rt.ToNumber(h.Get(subject, "selectedIndex")), Is.EqualTo(3));
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_chatEmoticonOpen"), "visible")), Is.True);
    }

    [Test]
    public void MessengerTabsFilterTheLogByTheirPanelSettings()
    {
        var m = Model();
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        Load(c, m);
        PanelSetting entire = c.Tabs[0].Panel, items = c.Tabs[1].Panel;
        // the entire tab: the system lines and the item lines (Require all="0" is recomputed to 1: token and auth stay 1),
        // not meso or experience
        Assert.That(entire["Require", "all"], Is.EqualTo(1));
        Assert.That(entire.Hidden(TalkKind.SystemNotice), Is.False);
        Assert.That(entire.Hidden(TalkKind.Item), Is.False);
        Assert.That(entire.Hidden(5), Is.True);
        Assert.That(entire.Hidden(8), Is.True);
        Assert.That(entire.Hidden(1), Is.False);
        // the item tab: no system lines (all recomputed to 0), the item lines, no social chat; friend notices stay
        // (Notice all="0" is recomputed to 1 by friend and the rest, which default to 1)
        Assert.That(items["System", "all"], Is.EqualTo(0));
        Assert.That(items["Notice", "all"], Is.EqualTo(1));
        Assert.That(items.Hidden(TalkKind.SystemNotice), Is.True);
        Assert.That(items.Hidden(TalkKind.Item), Is.False);
        Assert.That(items.Hidden(1), Is.True);
        Assert.That(items.Hidden(0xc), Is.False);
        Assert.That(items.Hidden(10), Is.True);
        // kind 0x1d is never hidden; global="1" makes the shared screen setting the one both tabs use
        Assert.That(entire.Hidden(TalkKind.TopLayer) || items.Hidden(TalkKind.TopLayer), Is.False);
        Assert.That(c.Browser.Global, Is.True);
        Assert.That(c.Browser.GlobalScreen.FontSize, Is.EqualTo(17));
    }

    [Test]
    public void MeratLinesAreHiddenInTheEntireTabAndShownInTheItemTab()
    {
        // The shipped default tabs: "entire" has Require all=0 merat=0 (the client hides kind 6 there, FUN_1414818b0),
        // the item tab has every Require switch on.
        var m = Model();
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        var feed = new CurrencyFeed(m) { Line = c.Browser.AddSystemLine };
        feed.OnMeret(new Ms2.Net.Packets.MeretUpdate(1200, 0, 0, 0, 500));
        Assert.That(c.Browser.Logs.Any(l => l.Kind == TalkKind.Merat), Is.True, "a merat line was logged");
        c.Browser.Process();
        var output = (AsObject)h.Child("m_textOutput");
        string Html() => Rt.ToString(h.Get(output, "htmlText"));
        Assert.That(Html(), Does.Not.Contain("500"), "entire tab");
        c.OnCallback("eventChangeTab", new object[] { "$messenger_tab_name_item" });
        Assert.That(Html(), Does.Contain("500"), "item tab");
    }

    [Test]
    public void MessengerShowsTheSystemAndItemLinesOfItsTab()
    {
        var m = Model();
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        var log = c.Browser;
        string welcome = m.Tables.Common("s_msg_chatting_welcome");
        Assert.That(welcome, Is.Not.Empty);
        log.AddSystemLine(welcome, TalkKind.SystemNotice);
        // s_html_chat_notice with chatTextSystemMessage and the shared font size
        Assert.That(log.Logs.Last().Msg, Is.EqualTo(
            $"<P><FONT color='#{m.Tables.ChatTextColors["chatTextSystemMessage"]:x}' size='17'>{welcome}</FONT></P>"));
        var item = Item.Create(m.Tables.Items, 20300327, 0);
        Assert.That(item, Is.Not.Null);
        log.AddItemLine(item, 3);
        string name = new ItemTooltip(m, 0).Name(item);
        Assert.That(log.Logs.Last().Kind, Is.EqualTo(TalkKind.Item));
        Assert.That(log.Logs.Last().Msg, Does.Contain($"size='17'>{name}</FONT></a> x3"));
        // two pending lines: appended to the entire tab
        log.Process();
        Assert.That(log.Pending, Is.Empty);
        var output = (AsObject)h.Child("m_textOutput");
        string Html() => Rt.ToString(h.Get(output, "htmlText"));
        Assert.That(Html(), Does.Contain(welcome));
        Assert.That(Html(), Does.Contain(name));
        // the item tab redraws from the log: the item line only
        c.OnCallback("eventChangeTab", new object[] { "$messenger_tab_name_item" });
        Assert.That(Html(), Does.Not.Contain(welcome));
        Assert.That(Html(), Does.Contain(name));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ASystemLineOfManyParagraphsKeepsItsColourOnEveryParagraph()
    {
        // the server's command help is one notice of "\n"-joined lines, html encoded, "word:" in <b>: every paragraph of
        // it is in the system colour, not only the ones after the first formatted run
        var m = Model();
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true);
        var h = Load(c, m);
        // the text of CommandRouter.GetCommandList through GameOutputStreamWriter (HtmlEncode, "word:" in <b>, 2+ spaces to 2)
        string[] lines =
        {
            "perm  Set/Remove admin permissions.", "alert  Send alert to the entire server.",
            "ban  Ban a player by character name. Usage: ban <character> [-t type] [-d days] [reason...]",
            "baninfo  Show all active bans for a character. Usage: baninfo <character>",
            "unban  Remove bans by ID. Usage: unban <banId> [banId ...]",
            "banword  The chat ban-word list: list, add, remove.", "buff  Add buff to player.",
            "movefield  Move to a field (the client's GM map window).", "addfield  Move to a field (the client's GM map window).",
            "clearinven  Empty the inventory (the client's GM item window).",
            "toggleinvisiblefromnpc  Monsters ignore you (GM hotkey).", "toggledeathpower  Your hits kill monsters (GM hotkey).",
        };
        var sb = new System.Text.StringBuilder("Commands:\n");
        foreach (string l in lines) sb.Append("  " + l + "\n");
        string enc = System.Net.WebUtility.HtmlEncode(sb.ToString().TrimEnd('\n'));
        enc = System.Text.RegularExpressions.Regex.Replace(enc, "(\\w+:)", "<b>$1</b>");
        string text = System.Text.RegularExpressions.Regex.Replace(enc, " {2,}", "  ");
        c.Browser.AddSystemLine(text, TalkKind.SystemNotice);
        c.Browser.Process();
        var output = (AsObject)h.Get(h.Child("m_textOutput"), "textField");
        string plain = Rt.ToString(h.Get(output, "text"));
        uint want = m.Tables.ChatTextColors["chatTextSystemMessage"];
        int at = 0, paragraphs = 0;
        foreach (string para in plain.Split('\r', '\n'))
        {
            if (para.Length > 0)
            {
                var fmt = h.Invoke(output, "getTextFormat", at, at + 1);
                Assert.That((uint)Rt.ToNumber(h.Get(fmt, "color")), Is.EqualTo(want), $"paragraph {paragraphs} \"{para}\"");
                paragraphs++;
            }
            at += para.Length + 1;
        }
        Assert.That(paragraphs, Is.GreaterThanOrEqualTo(lines.Length));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    static List<string> BarLabels(UiHost h)
    {
        var dp = h.Get(h.Child("m_buttonBar"), "dataProvider");
        int n = (int)Rt.ToNumber(h.Get(dp, "length"));
        var r = new List<string>();
        for (int i = 0; i < n; i++) r.Add(Rt.ToString(h.Get(h.Get(dp, i.ToString(System.Globalization.CultureInfo.InvariantCulture)), "label")));
        return r;
    }

    [Test]
    public void MessengerTabMenuRemovesTheSelectedTabAndClosesTheEmptyWindow()
    {
        var (c, h, _, _, _) = ChatWindow();
        var closed = new List<MessengerController>();
        c.Browser.WindowClosed += closed.Add;
        Assert.That(c.CreateTab("t1", 1, 1, 0), Is.True);
        Assert.That(c.CreateTab("t1", 1, 1, 0), Is.False, "a name the window has");
        c.SelectTabByName("t1");
        Assert.That(BarLabels(h), Does.Contain("t1"));
        // vfunc_96(1): RemoveTabByName(GetSelectedTabName())
        Assert.That(c.OnCallback("eventTabMenu", new object[] { 1.0 }), Is.True);
        Assert.That(c.Tabs.Select(t => t.Name), Does.Not.Contain("t1"));
        Assert.That(BarLabels(h), Is.EqualTo(c.Tabs.Select(t => t.Name).ToList()));
        Assert.That(closed, Is.Empty, "the window still has tabs");
        // removing every tab closes the window (CloseEmptyWindow)
        while (c.Tabs.Count > 0) { c.SelectTabByName(c.Tabs[0].Name); c.OnCallback("eventTabMenu", new object[] { 1.0 }); }
        Assert.That(closed, Is.EqualTo(new[] { c }));
        Assert.That(c.Browser.Windows, Does.Not.Contain(c));
        // menu 0 asks for the tab name dialog in new tab mode
        int opened = 0;
        c.Browser.OpenNewTabDialog = () => opened++;
        c.OnCallback("eventTabMenu", new object[] { 0.0 });
        Assert.That((opened, c.Browser.NewTabMode, c.Browser.NewTabWindow), Is.EqualTo((1, 1, c)));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerAdditiveTabTakesTheNextFreeNameAndAsksForTheTabNameDialog()
    {
        var (c, h, m, _, _) = ChatWindow();
        int opened = 0;
        c.Browser.OpenNewTabDialog = () => opened++;
        string first = Ms2.Quest.ClientFormat.Format(m.Tables.Common(0x57d), 1), second = Ms2.Quest.ClientFormat.Format(m.Tables.Common(0x57d), 2);
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(c.OnCallback("eventAdditiveTab", Array.Empty<object>()), Is.True);
        Assert.That(c.Tabs.Select(t => t.Name), Does.Contain(first));
        Assert.That((opened, c.Browser.NewTabMode, c.Browser.NewTabWindow), Is.EqualTo((1, 2, c)));
        Assert.That(BarLabels(h)[(int)Rt.ToNumber(h.Get(h.Child("m_buttonBar"), "selectedIndex"))], Is.EqualTo(first));
        c.OnCallback("eventAdditiveTab", Array.Empty<object>());
        Assert.That(c.Tabs.Select(t => t.Name), Does.Contain(second), "the first name is taken");
        // the dialog open: nothing
        c.Browser.IsNewTabDialogClosed = () => false;
        int n = c.Tabs.Count;
        c.OnCallback("eventAdditiveTab", Array.Empty<object>());
        Assert.That(c.Tabs.Count, Is.EqualTo(n));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerDetachedTabGetsItsOwnWindowAndAttachesBack()
    {
        var (c, h, m, _, _) = ChatWindow();
        c.CreateTab("t1", 1, 1, 0);
        c.CreateTab("t2", 1, 1, 0);
        c.Browser.Global = false; // s_nGlobal writes the shared screen over the tab's
        c.Tabs.First(t => t.Name == "t1").Panel.Screen.FontSize = 14;
        var created = new List<MessengerController>();
        c.Browser.WindowCreated += created.Add;
        // FUN_141a16730: name, x, y, width, height (floats), target
        Assert.That(c.OnCallback("eventDetachTab", new object[] { "t1", 100.7, 50.0, 400.0, 300.0, 2.0 }), Is.True);
        Assert.That(created.Count, Is.EqualTo(1));
        var w = created[0];
        Assert.That(w.Name, Is.EqualTo("messengerdialog1.xml"), "messengerdialog0.xml is taken");
        Assert.That(c.Browser.Windows, Does.Contain(w));
        Assert.That(c.Tabs.Select(t => t.Name), Does.Not.Contain("t1"));
        var wh = Load(w, m);
        Assert.That(w.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "t1" }));
        Assert.That(w.Tabs[0].Panel.Screen.FontSize, Is.EqualTo(14), "the panel goes with the tab");
        Assert.That(BarLabels(wh), Is.EqualTo(new[] { "t1" }));
        // the last tab never detaches
        w.OnCallback("eventDetachTab", new object[] { "t1", 0.0, 0.0, 400.0, 300.0, 0.0 });
        Assert.That(created.Count, Is.EqualTo(1));
        // vfunc_98: back to the first window, which selects it; the emptied window closes
        var closed = new List<MessengerController>();
        c.Browser.WindowClosed += closed.Add;
        Assert.That(w.OnCallback("eventAttachTab", new object[] { "t1", c.Name }), Is.True);
        Assert.That(c.Tabs.Select(t => t.Name), Does.Contain("t1"));
        Assert.That(BarLabels(h)[(int)Rt.ToNumber(h.Get(h.Child("m_buttonBar"), "selectedIndex"))], Is.EqualTo("t1"));
        Assert.That(closed, Is.EqualTo(new[] { w }));
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(wh.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerTabPanelWritesWhatItReads()
    {
        var m = Model();
        var browser = new MessengerBrowser { Model = m };
        var panel = new System.Xml.Linq.XElement("PanelSetting",
            new System.Xml.Linq.XElement("Notice", new System.Xml.Linq.XAttribute("trade", 0)),
            new System.Xml.Linq.XElement("Subject", new System.Xml.Linq.XAttribute("world", 0), new System.Xml.Linq.XAttribute("whisper", 0)),
            new System.Xml.Linq.XElement("Screen", new System.Xml.Linq.XAttribute("fontSize", 13), new System.Xml.Linq.XAttribute("backgroundAlpha", 70)));
        var a = new PanelSetting();
        a.Read(panel, browser, m.IsFeatureEnabled);
        var tab = new System.Xml.Linq.XElement("Tab");
        a.Write(tab, browser, m.IsFeatureEnabled);
        var b = new PanelSetting();
        b.Read(tab.Element("PanelSetting"), browser, m.IsFeatureEnabled);
        for (int kind = 0; kind < 0x1e; kind++) Assert.That(b.Hidden(kind), Is.EqualTo(a.Hidden(kind)), "kind " + kind);
        Assert.That((b.Screen.FontSize, b.Screen.BackgroundAlpha), Is.EqualTo((13, 70)));
        Assert.That(b.Hidden(0x12) && b.Hidden(0x15) && b.Hidden(4), Is.True);
        Assert.That(tab.Element("PanelSetting").Elements().Select(e => e.Name.LocalName),
            Is.EqualTo(new[] { "Notice", "System", "Screen", "Require", "Subject" }));
    }

    [Test]
    public void MessengerEmoticonSendsAUsableEmoticonWithTheWindowAndTarget()
    {
        var (c, h, _, _, _) = ChatWindow();
        var sent = new List<Ms2.Net.Packets.ChatStickerRequest>();
        c.Browser.SendSticker = sent.Add;
        c.Browser.IsEmoticonUsable = id => id == 7;
        Assert.That(c.OnCallback("eventChatEmoticonPush", new object[] { 7.0 }), Is.False, "vfunc_28 returns 0 after it");
        c.OnCallback("eventChatEmoticonPush", new object[] { 8.0 });
        c.OnCallback("eventChatEmoticonFavorite", new object[] { 7.0, true });
        c.OnCallback("eventChatEmoticonFavorite", new object[] { 7.0, false });
        c.OnCallback("eventChatEmoticonFavorite", new object[] { 8.0, true });
        int type = (int)Rt.ToNumber(h.Get(h.Child("m_subject"), "selectedIndex")) >= 0 ? sent[0].ChatType : -1;
        Assert.That(sent.Select(s => (s.Mode, s.EmoticonId)), Is.EqualTo(new[] { ((byte)3, 7), ((byte)5, 7), ((byte)6, 7) }));
        Assert.That(sent[0].Window, Is.EqualTo(c.Name));
        Assert.That(type, Is.EqualTo(MessengerController.Social), "the default selected target");
        int toggled = 0;
        c.Browser.ToggleEmoticonDialog = () => toggled++;
        c.OnCallback("eventChatEmoticonOpen", Array.Empty<object>());
        Assert.That(toggled, Is.EqualTo(1));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerWhisperTargetStartsAWhisperToAnotherPlayer()
    {
        var (c, h, m, _, _) = ChatWindow();
        var input = (AsObject)h.Child("m_textInput");
        Assert.That(c.OnCallback("eventWhisperTarget", new object[] { "Me" }), Is.True);
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.Empty, "not to the local player");
        c.OnCallback("eventWhisperTarget", new object[] { "Bob" });
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo(m.Tables.Common(0xaa5) + " Bob "));
        h.Set(input, "text", "");
        Assert.That(c.GetUserData("whisper", new object[] { "Ann" }), Is.Null);
        Assert.That(Rt.ToString(h.Get(input, "text")), Is.EqualTo(m.Tables.Common(0xaa5) + " Ann "));
        foreach (var n in new[] { "eventChangeText", "eventShowWhisperList", "eventHideWhisperList" })
            Assert.That(c.OnCallback(n, new object[] { "x" }), Is.True, n);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerHyperLinkLeftRunsThePartyLinkOfTheBrowser()
    {
        var (c, h, _, _, _) = ChatWindow();
        Assert.That(c.GetUserData("hyperLinkLeft", new object[] { "inviteParty,Bob" }), Is.Null, "no party links: nothing");
        var seen = new List<string>();
        c.Browser.PartyLinks = new PartyLinks { Send = p => seen.Add(((PartyInviteRequest)p).Name) };
        Assert.That(c.GetUserData("hyperLinkLeft", new object[] { "inviteParty,Bob" }), Is.Null);
        Assert.That(seen, Is.EqualTo(new[] { "Bob" }));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerShowsAnItemDropNoticeAsASystemLineWithTheItemLink()
    {
        var m = Model();
        var log = new MessengerBrowser { Model = m };
        string fmt = m.Tables.Common(0x342);
        Assert.That(fmt, Is.Not.Empty);
        log.AddItemDropNotice("Dropper", 15000001, 3, 7);
        string link = log.ItemLink(15000001, 3, 7);
        Assert.That(log.Logs.Last().Kind, Is.EqualTo(TalkKind.SystemNotice));
        Assert.That(log.Logs.Last().Msg, Does.Contain("Dropper").And.Contain(link));
        Assert.That(log.Logs.Last().Msg, Does.Contain("event:itemTooltip,7,3,7,15000001,3"));
    }

    [Test]
    public void MessengerShowsTheLinesLoggedBeforeItLoaded()
    {
        var m = Model();
        var log = new MessengerBrowser { Model = m };
        string welcome = m.Tables.Common("s_msg_chatting_welcome");
        log.AddSystemLine(welcome, TalkKind.SystemNotice);
        var c = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true, log);
        var h = Load(c, m);
        log.Process();
        var output = (AsObject)h.Child("m_textOutput");
        Assert.That(Rt.ToString(h.Get(output, "htmlText")), Does.Contain(welcome));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void MessengerLogKeepsFiveHundredLines()
    {
        var log = new MessengerBrowser();
        for (int i = 0; i < 501; i++) log.AddLog("<P>" + i + "</P>", TalkKind.SystemNotice);
        Assert.That(log.Logs.Count, Is.EqualTo(MessengerBrowser.MaxLogs));
        Assert.That(log.Logs.First().Msg, Is.EqualTo("<P>1</P>"));
        Assert.That(log.Pending.Count, Is.EqualTo(501));
        log.Process();
        Assert.That(log.Pending, Is.Empty);
        log.AddSystemLine("", TalkKind.SystemNotice);
        Assert.That(log.Pending, Is.Empty);
        log.Clear();
        Assert.That(log.Logs, Is.Empty);
        // UIMessenger::ReplaceFontSize: only size='12' .. size='18'
        Assert.That(MessengerBrowser.ReplaceFontSize(14, "size='12' size='18' size='19' size='17'"),
            Is.EqualTo("size='14' size='14' size='19' size='14'"));
    }

    [Test]
    public void GroupMessengerBrowserInsertButtonOnly()
    {
        var m = Model();
        var c = new GroupMessengerBrowserController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // m_container, added last by the constructor, holds only the insert button. The slot is private to the class, and
        // GFx::Value::GetMember looks a name up in the public namespace (AS3ValueObjectInterface::GetMember,
        // sfr_as3_18.cpp:7551), so it is taken from the display list
        var container = (AsContainer)((AsContainer)h.Entity).Children[3];
        Assert.That(container.Children.Count, Is.EqualTo(1));
        double y = Rt.ToNumber(h.Get(h.Entity, "y")), ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "x")), Is.EqualTo(0));
        Assert.That(y, Is.EqualTo(1080 - (int)ht - 300));
    }

    [Test]
    public void NoticeDialogShowRequestsTheNewsOnlyWithAUrlAndOpenUrlKeepsTheAddress()
    {
        var m = Model();
        var c = new NoticeDialogController { NoticeUrl = "http://n" };
        var sent = new List<(bool, int)>();
        c.NewsRequested += (f, id) => sent.Add((f, id));
        var h = Load(c, m);
        Assert.That(sent, Is.Empty);
        c.ToggleShowHide();                                   // Show: NavigateToNoticeUrl (uinoticedialog.cpp:275)
        Assert.That(sent, Is.EqualTo(new[] { (true, 2) }));
        Assert.That(c.PendingUrl, Is.EqualTo("http://n"));
        c.OpenUrl("http://x");                                // vfunc_89
        Assert.That(c.Url, Is.EqualTo("http://x"));
        Assert.That(c.PendingUrl, Is.Empty);
        var c2 = new NoticeDialogController();                // NoticeDialogUrl empty: nothing is sent
        var none = 0;
        c2.NewsRequested += (_, _) => none++;
        var h2 = Load(c2, Model());
        c2.ToggleShowHide();
        Assert.That(none, Is.EqualTo(0));
    }

    [Test]
    public void SmartPushStripAddsAndRemovesButtonsAndReportsClicks()
    {
        var m = Model();
        var c = new SmartPushStripController();
        Assert.That(c.AddPush(new SmartPushView { Id = 1 }), Is.False, "uismartpush.cpp:122: no entity yet, nothing is added");
        Assert.That(c.RemovePush(1), Is.False);
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.Ready, Is.True);
        int Count() => (int)Rt.ToNumber(h.Get(h.Entity, "numChildren"));
        Assert.That(Count(), Is.EqualTo(0));
        Assert.That(c.AddPush(new SmartPushView { Id = 5, Text = "Auto", ActiveMovie = "UISmartPush_autofish" }), Is.True);
        Assert.That(c.AddPush(new SmartPushView { Id = 6, Text = "Ride", IsToggle = true, Active = true }), Is.True);
        for (int i = 0; i < 10; i++) h.Player.Tick();
        Assert.That(Count(), Is.EqualTo(2));
        Assert.That(c.RemovePush(5), Is.True);
        Assert.That(Count(), Is.EqualTo(1));
        // vfunc_43 (uismartpush.cpp:100): bottom left, 0x1b4 across, FUN_1418dd0d0 + 0x5a above the bottom; FUN_1418dd0d0 is
        // (bar - 2) + the adventure bar's (feature 0xbd) + the margin 193 (uibuiltindialog.cpp:53)
        int inset = 21 - 2 + (m.IsFeatureEnabled(0xbd) ? 21 - 2 : 0) + 193;
        Assert.That(c.BottomInset, Is.EqualTo(inset));
        double ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        c.Layout(1920, 1080);
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "x")), Is.EqualTo(0x1b4));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo(1080 - (int)ht - (inset + 0x5a)));
        var clicked = new List<int>();
        c.PushClicked += clicked.Add;
        Assert.That(c.OnCallback("onSmartPush", new object[] { 6 }), Is.True);       // vfunc_28 (:136)
        Assert.That(c.OnCallback("onOther", new object[] { 6 }), Is.False);
        Assert.That(clicked, Is.EqualTo(new[] { 6 }));
    }

    [Test]
    public void NoticeTabClosedLeftMiddleAndToggles()
    {
        var m = Model();
        var c = new NoticeDialogController();
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        // OnLoadComplete: gotoAndStop("hideComplete"); vfunc_43: ASDisplayObject::vfunc_2(3, 0, 0)
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("hideComplete"));
        double ht = Rt.ToNumber(h.Get(h.Entity, "height"));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "x")), Is.EqualTo(0));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo((int)((1080 - (int)ht) * 0.5f)));
        var show = h.Child("m_showButton", h.Child("m_mainWindow"));
        Assert.That(show, Is.Not.Null);
        c.ToggleShowHide();                 // the show button's click: Show
        for (int i = 0; i < 30; i++) h.Player.Tick();
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("show"));
        Assert.That(Rt.ToBoolean(h.Get(show, "selected")), Is.True);
        Assert.That(c.BrowserVisible, Is.True);
        c.ToggleShowHide();                 // and again: Hide
        for (int i = 0; i < 30; i++) h.Player.Tick();
        // gotoAndPlay("hide") plays on to the frame40 stop, in hideComplete
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("hideComplete"));
        Assert.That(Rt.ToBoolean(h.Get(show, "selected")), Is.False);
        Assert.That(c.BrowserVisible, Is.False);
    }

    [Test]
    public void ASystemNoticeShownBeforeTheMovieIsReplayedAsTheRedWarning()
    {
        // ShowMessage without the movie parks the type and text (m_70, m_78); OnLoadComplete replays them by
        // ShowMessage(text, type, 0), and type 1 is the movie's appendWarningMessage: the line in m_gameWarnMessage's step1
        var m = Model();
        var c = new SystemTextController();
        c.ShowMessage("You do not have the necessary weapon equipped.", SystemTextController.Warning, 0);
        Assert.That(c.Parked, Is.EqualTo((1, "You do not have the necessary weapon equipped.")));
        var h = Load(c, m);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.Parked, Is.EqualTo((0, "")));
        var warn = h.Child("m_gameWarnMessage");
        Assert.That(Rt.ToNumber(h.Get(warn, "currentFrame")), Is.GreaterThan(1));
        // onWarningTimer: step1's text field gets the new line (step2 the previous, none here)
        Assert.That(Text(h, "textField", h.Child("step1", warn) as AsDisplayObject), Is.EqualTo("You do not have the necessary weapon equipped."));
        Assert.That(Text(h, "textField", h.Child("step2", warn) as AsDisplayObject), Is.Empty);
        // the same line again while it is queued or shown is dropped by the movie (appendWarningMessage's queue check)
        c.ShowMessage("You do not have the necessary weapon equipped.", SystemTextController.Warning, 0);
        Assert.That(h.RT.Errors, Is.Empty);
        // vfunc_43 without the upper HP bar: top centre, 100 down (the entity's width getter is 1920)
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "x")), Is.EqualTo(0));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo(100));
    }

    [Test]
    public void TheSystemLinesSitBelowTheUpperHpBar()
    {
        // vfunc_43: the top is the upper HP bar root's y + ceil(height) + 10
        var m = Model();
        var bar = new UpperHpBarController();
        var hb = Load(bar, m);
        var c = new SystemTextController { UpperHpBar = bar };
        var h = Load(c, m);
        double y = Rt.ToNumber(hb.Get(hb.Entity, "y")), ht = Rt.ToNumber(hb.Get(hb.Entity, "height"));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "y")), Is.EqualTo((int)((float)y + (int)Math.Ceiling(ht) + 10f)));
    }

    [Test]
    public void TheAiWarningExpiresAndTheEmergencyLinePlaysItsSound()
    {
        var m = Model();
        uint now = 1000;
        var c = new SystemTextController { TickMs = () => now };
        var sounds = new List<string>();
        c.SystemSound += sounds.Add;
        var h = Load(c, m);
        // ShowTimedMessage: only type 0xc has a method; it expires at now + duration (m_88)
        c.ShowTimedMessage("ai", SystemTextController.Warning, 500);
        Assert.That(c.Timed, Is.Empty);
        c.ShowTimedMessage("ai", SystemTextController.AiWarning, 500);
        Assert.That(c.Timed[SystemTextController.AiWarning], Is.EqualTo(1500u));
        var ai = h.Child("m_aiWarningMessage");
        Assert.That(Rt.ToNumber(h.Get(ai, "currentFrame")), Is.GreaterThan(1));
        now = 1400; c.Process();
        Assert.That(c.Timed.ContainsKey(SystemTextController.AiWarning), Is.True);
        now = 1500; c.Process();         // vfunc_15: expired (it->second <= now), onAiWarningMessage, erased
        Assert.That(c.Timed, Is.Empty);
        Assert.That(Rt.ToNumber(h.Get(ai, "currentFrame")), Is.EqualTo(1));
        // type 9: appendEmergencyMessage(text, option) and PlaySystemSound("emergency_notice")
        c.ShowMessage("emergency", SystemTextController.Emergency, 0);
        Assert.That(sounds, Is.EqualTo(new[] { "emergency_notice" }));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    static List<string> AllTexts(AsDisplayObject o)
    {
        var l = new List<string>();
        void Walk(AsDisplayObject d)
        {
            if (d is AsTextField t && !string.IsNullOrEmpty(t.Text)) l.Add($"{t.InstanceName}={t.Text}");
            if (d is AsContainer c) foreach (var k in c.Children) Walk(k);
        }
        Walk(o);
        return l;
    }

    static IEnumerable<AsDisplayObject> Tree(AsDisplayObject o)
    {
        yield return o;
        if (o is AsContainer c) foreach (var ch in c.Children) foreach (var x in Tree(ch)) yield return x;
    }

    [Test]
    public void TheOwnPictureAtLoadIsDrawn()
    {
        // BuildPlayerData: createPlayer with the downloaded picture's path; the list's build draws it.
        var m = Model();
        m.Field.Name = "Tria";
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false), CharacterId = 77, PlayerImage = "./Custom/Download/Profile/x_1_2.png" };
        var h = Load(c, m);
        var p = h.Player;
        for (int i = 0; i < 10; i++) p.Tick();
        var r = Tree(p.Root).Single(o => o.Class?.Name == "minimap_renderer_players");
        TestContext.WriteLine($"dirty '{r.GetPublic("m_dirtyProfile")}' loaded '{h.Get(r.GetPublic("m_profile"), "imgpath")}'");
        Assert.That(Rt.ToString(h.Get(r.GetPublic("m_profile"), "imgpath")), Does.EndWith("x_1_2.png"));
    }

    [Test]
    public void APlayerCirclePictureChangeWaitsForTheNextStageInvalidate()
    {
        // vfunc_ugc_1 (@0x141a383b0): changePlayer; ProfileRenderer.profile keeps the path and draws it on Event.RENDER,
        // which the runtime sends only after a stage.invalidate() (MovieRoot::AdvanceFrame, sfr_as3_16z.cpp:607-611).
        var m = Model();
        m.Field.Name = "Tria";
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false), CharacterId = 77 };
        var h = Load(c, m);
        var p = h.Player;
        for (int i = 0; i < 5; i++) p.Tick();
        var r = Tree(p.Root).Single(o => o.Class?.Name == "minimap_renderer_players");
        c.OnProfileImage(77, "profile/test.png");
        Assert.That(r.GetPublic("m_dirtyProfile"), Is.EqualTo("profile/test.png"));
        for (int i = 0; i < 10; i++) p.Tick();
        Assert.That(r.GetPublic("m_dirtyProfile"), Is.EqualTo("profile/test.png"), "nothing invalidated the stage");
        // A hover is not one: the renderer has no "over" frame, and Button.setState invalidates only for a labelled frame.
        var at = r.LocalToGlobal(4, 4);
        p.MouseMove(at.X, at.Y);
        p.Tick();
        Assert.That(r.GetPublic("m_dirtyProfile"), Is.EqualTo("profile/test.png"));
        // Any other component's invalidate in the movie is (a list rebuild when a player comes or goes: TileCacheList).
        p.Call(p.Stage, "invalidate", System.Array.Empty<object>());
        p.Tick();
        Assert.That(r.GetPublic("m_dirtyProfile"), Is.EqualTo(""), "draw ran");
        Assert.That(p.RenderInvalidated, Is.False);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    sealed class Caster : ICastingEventListener
    {
        public int Finished, Cancelled;
        public void OnCastingFinished() => Finished++;
        public void OnCastingCancelled() => Cancelled++;
    }

    [Test]
    public void ACastShowsTheGaugeAndFinishesWhenItsTimerExpires()
    {
        // StartCastingText (FUN_1418e15d0): the timer starts, the text fields are bound and the movie's startCasting runs with
        // the param's default labels ("show", "yellow"); vfunc_15 finishes the cast once the timer is past the duration
        long now = 1000;
        var c = new CastingBarController { NowMs = () => now };
        var h = Load(c, Model());
        Assert.That(h.RT.Errors, Is.Empty);
        var caster = new Caster();
        c.StartCastingText(caster, "Cast", 3000, true, true, "Done");
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(c.IsCasting, Is.True);
        Assert.That(c.Listener, Is.SameAs(caster));
        Assert.That(Rt.ToBoolean(h.Get(h.Entity, "visible")), Is.True);
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("show"));
        Assert.That(Rt.ToString(h.Get(h.Child("m_text1"), "text")), Is.EqualTo("Cast"));
        Assert.That(Rt.ToString(h.Get(h.Child("m_text2"), "text")), Is.EqualTo("Cast"));
        c.SetCastTime("1.5");               // the bound time lines
        Assert.That(Rt.ToString(h.Get(h.Child("m_timetext1"), "text")), Is.EqualTo("1.5"));
        now += 2999;
        c.Process();
        Assert.That(caster.Finished, Is.Zero);
        now += 1;
        c.Process();                        // expired: FinishCasting(end) stops the gauge with the end text
        Assert.That(caster.Finished, Is.EqualTo(1));
        Assert.That(caster.Cancelled, Is.Zero);
        Assert.That(c.IsCasting, Is.False);
        Assert.That(c.Listener, Is.Null);
        Assert.That(Rt.ToString(h.Get(h.Child("m_text1"), "text")), Is.EqualTo("Done"));
        Assert.That(Rt.ToString(h.Get(h.Entity, "currentLabel")), Is.EqualTo("hide"));
        c.Process();                        // the timer was reset: no second call
        Assert.That(caster.Finished, Is.EqualTo(1));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void ANewCastCancelsTheRunningOneAndTheMovieCompletesAZeroLengthCast()
    {
        long now = 0;
        var c = new CastingBarController { NowMs = () => now };
        var h = Load(c, Model());
        var a = new Caster();
        var b = new Caster();
        c.StartCastingText(a, "A", 5000, false, true, "");
        c.StartCasting(b, new CastingParam { Text = "B", Duration = 5000 });
        Assert.That(a.Cancelled, Is.EqualTo(1));
        Assert.That(a.Finished, Is.Zero);
        Assert.That(c.Listener, Is.SameAs(b));
        Assert.That(Rt.ToString(h.Get(h.Child("m_text1"), "text")), Is.EqualTo("B"));
        // the movie's OnCastingComplete (vfunc_28) finishes the cast
        Assert.That(c.OnCallback("OnCastingComplete", Array.Empty<object>()), Is.True);
        Assert.That(b.Finished, Is.EqualTo(1));
        Assert.That(c.IsCasting, Is.False);
        // a cast without a duration: the movie's startCasting calls onCastingComplete at once
        var z = new Caster();
        c.StartCastingText(z, "Z", 0, false, true, "");
        Assert.That(z.Finished, Is.EqualTo(1));
        Assert.That(c.Listener, Is.Null);
        // vfunc_3 (leaving the game scene) drops a running cast without calling its caster
        var d = new Caster();
        c.StartCastingText(d, "D", 5000, false, true, "");
        c.Hide();
        Assert.That(d.Finished + d.Cancelled, Is.Zero);
        Assert.That(c.Listener, Is.Null);
        Assert.That(h.RT.Errors, Is.Empty);
    }
    /// <summary>
    /// UIActionTooltip (0x4e5c) on its movie: the refresh shows the current action's key and caption (the entity's show),
    /// the frame hides it while the action is busy (gathering in StateInteraction 0x19), without a target it sits at the
    /// stage centre plus OffsetActionTooltipX/Y, with one at the projected point kept inside the stage less its size.
    /// </summary>
    [Test]
    public void ActionTooltipShowsTheCaptionHidesWhileBusyAndPlacesItself()
    {
        var m = Model();
        int state = 0;
        string caption = "Gather Herb x1, Ore x2";
        Ms2.Formats.Model.Vec3? target = null;
        var c = new ActionTooltipController
        {
            HasPlayer = () => true,
            Current = () => 4,
            Caption = () => caption,
            OwnState = () => state,
            Target = () => target,
            Project = p => ((float)p.X, (float)p.Y),
        };
        var h = Load(c, m);
        Assert.That(c.NeedsRefresh(), Is.True, "nothing shown yet");
        c.Refresh();
        for (int i = 0; i < 25; i++) h.Player.Tick();
        Assert.That(c.ShownAction, Is.EqualTo(4));
        Assert.That(c.ShownKind, Is.EqualTo("key"));
        Assert.That(c.ShownKeyText, Is.Not.Empty, "the action key's name");
        Assert.That(c.ShownKeyText, Is.EqualTo(m.Keys.GetKeyName(m.Keys.FindKeyByActionAndPriority(ActionTooltipController.ActionKeyCode, 0))));
        Assert.That(Text(h, "actionName"), Is.EqualTo(caption));
        Assert.That(c.Place, Is.EqualTo((1920 / 2f + 50, 1080 / 2f - 30)), "no target: the centre plus the offsets");
        Assert.That(c.NeedsRefresh(), Is.False);

        caption = "Gather Ore x1";
        Assert.That(c.NeedsRefresh(), Is.True, "the caption changed");
        c.Refresh();
        Assert.That(Text(h, "actionName"), Is.EqualTo(caption));

        state = 0x19;      // StateInteraction: gathering runs
        Assert.That(c.Busy(), Is.True);
        c.Process();
        Assert.That(c.ShownAction, Is.EqualTo(-1), "hidden while gathering");
        state = 0;
        c.Process();
        Assert.That(c.ShownAction, Is.EqualTo(4), "back when it ends");

        target = new Ms2.Formats.Model.Vec3(5000, -40, 0);
        c.Refresh();
        double w = Math.Ceiling(Rt.ToNumber(h.Get(h.Entity, "width")));
        Assert.That(c.Place.X, Is.EqualTo((float)(1920 - w)), "kept inside the stage");
        Assert.That(c.Place.Y, Is.EqualTo(0f));
        target = new Ms2.Formats.Model.Vec3(300, 200, 0);
        c.Refresh();
        Assert.That(c.Place, Is.EqualTo((300f, 200f)));

        caption = "";
        c.Refresh();
        Assert.That(c.ShownAction, Is.EqualTo(-1), "an empty caption hides it");
        Assert.That(new ActionTooltipController { HasPlayer = () => false }.Busy(), Is.True, "no player: busy");
    }
    /// <summary>TalkAction::vfunc_6: the npc's actionName (string table 0xe, keyed in 8 digits) or common 0x62d, then common 0x649
    /// with the npc's name; "" without an npc. Table 0xf (mapNamePortalTooltip) finds a 7-digit map id by its padded key.</summary>
    [Test]
    public void TalkCaptionIsTheActionNameOrTheTalkTextWithTheNpcName()
    {
        var m = Model();
        var t = m.Tables;
        Assert.That(t.StringTableText(0xe, 11000004), Is.EqualTo("Trade"));
        Assert.That(t.StringTableText(0xf, 3000002), Is.EqualTo("Twisted Pocket Realm"), "the padded key \"03000002\"");
        string name = ClientFormat.Format(t.Common(0x649), "Mia");
        Assert.That(name, Is.Not.Empty);
        Assert.That(ActionCaption.Talk(t, 11000004, "Mia"), Is.EqualTo("Trade" + name));
        Assert.That(ActionCaption.Talk(t, 11001898, "Mia"), Is.EqualTo(t.Common(0x62d) + name), "no action name: common 0x62d");
        Assert.That(ActionCaption.Talk(t, 0, "Mia"), Is.EqualTo(""));
    }
    /// <summary>PortalAction::vfunc_6: "" without the entity; the action text without a target; else the target's portal
    /// tooltip name (table 0xf) or map name (3) in common 0x633, with " &lt;common 0x99d&gt;" for a field that cannot be
    /// entered, common 0x632 for a target missing from the field table. LiftCubeAction::vfunc_6: GetActionCaption(0x63a, the
    /// lift item's name).</summary>
    [Test]
    public void PortalAndLiftCaptionsFollowTheirVfunc6()
    {
        var t = Model().Tables;
        var open = new Ms2.Formats.World.FieldData();
        var admin = new Ms2.Formats.World.FieldData { EnterAdminLevel = 1 };
        Assert.That(ActionCaption.Portal(t, false, 0, 2000001, _ => open), Is.EqualTo(""));
        Assert.That(ActionCaption.Portal(t, true, 8, 0, _ => open), Is.EqualTo(t.Common("s_action_portal_DungeonLobby")));
        Assert.That(ActionCaption.Portal(t, true, 0, 0, _ => open), Is.EqualTo(t.Common(0x632)));
        Assert.That(ActionCaption.Portal(t, true, 0, 3000002, _ => open), Is.EqualTo(ClientFormat.Format(t.Common(0x633), "Twisted Pocket Realm")));
        Assert.That(ActionCaption.Portal(t, true, 0, 2000001, _ => open), Is.EqualTo(ClientFormat.Format(t.Common(0x633), t.MapName(2000001))),
            "no portal tooltip name: the map name");
        Assert.That(ActionCaption.Portal(t, true, 0, 2000001, _ => admin),
            Is.EqualTo(ClientFormat.Format(t.Common(0x633), t.MapName(2000001) + " <" + t.Common(0x99d) + ">")));
        Assert.That(ActionCaption.Portal(t, true, 0, 2000001, _ => null), Is.EqualTo(t.Common(0x632)), "not in the field table");

        string ore = t.StringTableText(1, 30000001);
        Assert.That(ActionCaption.Lift(t, true, 30000001), Is.EqualTo(ActionCaption.Of(t.Common(0x63a), ore)));
        Assert.That(ActionCaption.Lift(t, false, 30000001), Is.EqualTo(""));
    }
}

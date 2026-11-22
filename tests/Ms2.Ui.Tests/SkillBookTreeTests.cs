using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The skill window (uiskillbooktreedialog_jobchange01) run by its own ActionScript with UISkillBookTreeDialog's C++
/// side, for a level 1 archer as in the capture (needs MS2_DATA).</summary>
public class SkillBookTreeTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static (UiHost, SkillBookTreeController, int Requests) Load()
    {
        var model = new UiModel(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = true };
        model.Player.Level = 1;
        var t = model.Tables.Skills;
        var jobs = new JobSystem(t);
        // a new archer's book: the skills learnt with the rank learnt at level 1
        jobs.Read(1, 50, t.Jobs.GetJobData(50).Skills.Select(r => new BookSkill((int)r.SkillId, 1, false, r.IsLearn, false)));
        var c = new SkillBookTreeController { JobChange01 = model.IsFeatureEnabled(0xca), Jobs = jobs };
        c.SkillTooltip = (d, id, lv) => new AsTooltip { ["tooltip"] = "export_common_tooltip_kindsSkill", ["skillID"] = id, ["level"] = lv };
        int requests = 0;
        c.RequestTree += () => requests++;
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 40_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, requests);
    }

    /// <summary>The quick slot bar (MainPlayerInfoController) on the same model, with an empty server key table.</summary>
    static (UiHost, MainPlayerInfoController, List<Ms2.Net.Wire.IOutPacket> Sent, ServerKeyTable Keys) QuickSlotBar(UiModel m)
    {
        var t = new ServerKeyTable();
        t.Apply(new ByteReader(ServerKeyTable.Write(0, false, Array.Empty<KeyBinding>(), 0,
            new IReadOnlyList<(int, QuickSlotEntry)>[] { Array.Empty<(int, QuickSlotEntry)>() })));
        m.Keys = new KeyTable(Packs(), "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var bar = new MainPlayerInfoController();
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        bar.Send = sent.Add;
        var (rt, p) = src.Open(bar.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, bar, m);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        return (h, bar, sent, t);
    }

    static SkillBookTreePacket NoTabs() => SkillBookTreePacket.Read(new ByteReader(SkillBookTreePacket.WriteTabs(1, 0, Array.Empty<SkillTreeTab>())));

    [Test]
    public void TheWindowWaitsForTheTabsThenShowsTheRankOneTree()
    {
        var (h, c, requests) = Load();
        Assert.That(c.Movie, Is.EqualTo("uiskillbooktreedialog_jobchange01"));
        Assert.That(requests, Is.EqualTo(1), "RequestSkillBookTree on load");
        Assert.That(Rt.ToBoolean(h.Get(h.Entity, "visible")), Is.False, "hidden until the tab packet");

        c.OnSkillBookTree(NoTabs());
        Assert.That(Rt.ToBoolean(h.Get(h.Entity, "visible")), Is.True);
        Assert.That(c.Tabs.Count, Is.EqualTo(1));
        Assert.That(c.Tabs[0].Name, Is.EqualTo("Build 1"));

        var tree = h.Child("m_treeSkills");
        int n = (int)Rt.ToNumber(h.Get(tree, "numChildren"));
        var skills = Enumerable.Range(0, n).Select(i => h.Invoke(tree, "getChildAt", i)).ToList();
        var byId = skills.ToDictionary(s => (uint)Rt.ToNumber(h.Get(s, "m_skillID")));
        TestContext.WriteLine(string.Join(", ", byId.Keys));
        // the rank 1 tree of job.xml group 50: 17 skills (the capture's 3 + 1 + 4 + 3 + 3 + 3)
        Assert.That(n, Is.EqualTo(17));
        string Level(uint id) => Rt.ToString(h.Get(h.Get(byId[id], "m_currentLevel"), "textField") is AsObject tf ? h.Get(tf, "htmlText") : null);
        TestContext.WriteLine($"{Level(10500001)} {Level(10500111)} {Level(10500041)}");
        Assert.That(Level(10500001), Does.Contain("1/10"));
        Assert.That(Level(10500111), Does.Contain("1/1"));
        Assert.That(Level(10500041), Does.Contain("0/10"));
        // placement: x * 180, y * 128 (setSkill)
        Assert.That((Rt.ToNumber(h.Get(byId[10500151], "x")), Rt.ToNumber(h.Get(byId[10500021], "y"))), Is.EqualTo((540.0, 256.0)));
        // the locked skill's slot is disabled, the learnt one not
        var slot41 = h.Get(byId[10500041], "m_skillSlot");
        Assert.That(Rt.ToBoolean(h.Get(slot41, "m_disabled")), Is.True);
        Assert.That(Rt.ToBoolean(h.Get(h.Get(byId[10500001], "m_skillSlot"), "m_disabled")), Is.False);

        // 0 points at level 1
        Assert.That(Rt.ToString(h.Get(h.Get(h.Child("m_point"), "textField"), "text")), Is.EqualTo("0"));
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_applySkill"), "enabled")), Is.False);
        Assert.That(Rt.ToBoolean(h.Get(h.Child("m_autoDistribute"), "enabled")), Is.False);

        // the slot binding of Arrow Stream
        var names = c.GetBagSlotBindings(SkillBookTreeController.BagName).ToList();
        Assert.That(names.Count, Is.GreaterThanOrEqualTo(17));
        var bound = names.Select(nm => (nm, h.ToAs(c.GetBinding(nm)))).ToList();
        Assert.That(bound.Any(b => b.Item2 is AsObject o && Rt.ToString(h.Get(o, "iconPath")).EndsWith("10500001.png")), Is.True);

        // the tooltip request reaches the skill tooltip at the shown level
        var tip = c.GetUserData("getTooltipFromSkillID", new object[] { 10500001u });
        Assert.That((tip["skillID"], tip["level"]), Is.EqualTo(((object)10500001u, (object)1)));
        Assert.That(c.GetUserData("getTooltipFromPrevLevel", new object[] { 10500001u }), Is.Null);
        Assert.That(c.GetUserData("getTooltipFromNextLevel", new object[] { 10500001u })["tooltipAlign"], Is.EqualTo(7));
    }

    [Test]
    public void ThePointTooltipCountsTheLevelAndSourcePoints()
    {
        var (h, c, _) = Load();
        h.Model.Player.Level = 20;
        c.Points.Apply(SkillPointPacket.Read(new ByteReader(SkillPointPacket.Write(0, new (int, IEnumerable<(int, int)>)[] { (2, new[] { (1, 2) }) }, 0))));
        c.OnSkillBookTree(NoTabs());
        Assert.That(Rt.ToString(h.Get(h.Get(h.Child("m_point"), "textField"), "text")), Is.EqualTo("13"));
        var t = c.PointTooltip();
        TestContext.WriteLine(t["text"]);
        Assert.That(t["tooltip"], Is.EqualTo("export_common_tooltip_tooltipText"));
        Assert.That((string)t["text"], Does.Contain("13"));
    }
    [Test]
    public void TheOpenWindowFollowsLevelPointsAndJobEvents()
    {
        var (h, c, _) = Load();
        c.OnSkillBookTree(NoTabs());
        string Point() => Rt.ToString(h.Get(h.Get(h.Child("m_point"), "textField"), "text"));
        Assert.That(Point(), Is.EqualTo("0"));
        // vfunc_oa0_9: level 20 gives 11 rank 1 points
        h.Model.Player.Level = 20;
        c.OnLevelChanged(20);
        Assert.That((Point(), c.Simulator.GetRemainPoints(1)), Is.EqualTo(("11", 11)));
        // vfunc_oa0_28: 2 more rank 1 points from a source
        c.Points.Apply(SkillPointPacket.Read(new ByteReader(SkillPointPacket.Write(0, new (int, IEnumerable<(int, int)>)[] { (2, new[] { (1, 2) }) }, 0))));
        c.OnSkillPointsChanged();
        Assert.That(Point(), Is.EqualTo("13"));
        // vfunc_oa0_7: a job change closes the window
        c.OnJobChanged();
        for (int i = 0; i < 60 && !c.IsClosed; i++) h.Player.Tick();
        Assert.That((c.IsClosed, c.Simulator), Is.EqualTo((true, (SkillPointSimulator)null)));
    }
    [Test]
    public void ABookSkillDroppedOnAQuickSlotIsSentAndKept()
    {
        // NotifyDragSlot::SlotDragEnd from a BookSlot to a SkillSlot: SkillSlot::vfunc_18 (skillslot.cpp:507) reads the book
        // slot's skill (BookSlot::FUN_140a366a0, bookslot.cpp:54) and puts it on the slot through FUN_14092f690, KeyTable mode 3
        var (bh, book, _) = Load();
        book.OnSkillBookTree(NoTabs());
        var m = bh.Model;
        var names = book.GetBagSlotBindings(SkillBookTreeController.BagName).ToList();
        Assert.That(book.IsDragSlot(names[0]), Is.True);
        Assert.That(book.IsDragSlot("m_treeSkills"), Is.False);
        Assert.That(book.BookSkillId("m_treeSkills"), Is.EqualTo(0u));
        string from = names.First(n => m.Tables.Skills.GetSkillData(book.BookSkillId(n)) is { IsPassive: false });
        int skill = (int)book.BookSkillId(from);
        var (h, bar, sent, t) = QuickSlotBar(m);

        bar.SlotDroppedOn(MainPlayerInfoController.SlotBag + ".22", from, book);     // a key bound slot: nothing
        Assert.That(sent, Is.Empty);
        bar.SlotDroppedOn(MainPlayerInfoController.SlotBag + ".3", from, book);
        Assert.That(sent.Select(s => s.Op), Is.EqualTo(new[] { Ms2.Net.Outbound.KeyTable }));
        Assert.That(sent[0], Is.EqualTo(new KeyTableSetQuickSlotRequest(0, skill, 0, 0, 3)));
        Assert.That(bar.ShownEntry(3), Is.EqualTo(new QuickSlotEntry(skill, 0, 0)));
        Assert.That(t.Entry(0, 3).IsEmpty, Is.True, "a book drop writes only the slot (skillslot.cpp, the BookSlot branch)");
        var slot3 = (AsObject)h.ToAs(bar.GetBinding(MainPlayerInfoController.SlotBag + ".3"));
        Assert.That(Rt.ToString(h.Get(slot3, "iconPath")), Is.EqualTo(m.Tables.Skills.GetSkillData((uint)skill).Icon));
    }

    /// <summary>The cooldown events reach the quick slot's movie: UIMainPlayerInfo vfunc_oa0_25 / 26 give each SkillSlot
    /// vfunc_23 / 24, which call NotifyCooldownStart / NotifyCooldownEnd (invenslot.cpp:493, :541) and the slot's AS onCooldownStart /
    /// onCooldownEnd, then queue the gray and recharge refresh.</summary>
    [Test]
    public void TheCooldownEventsRunTheSlotsOnCooldownStartAndEnd()
    {
        var (bh, book, _) = Load();
        book.OnSkillBookTree(NoTabs());
        var m = bh.Model;
        var names = book.GetBagSlotBindings(SkillBookTreeController.BagName);
        string from = names.First(n => m.Tables.Skills.GetSkillData(book.BookSkillId(n)) is { IsPassive: false, Condition.UseCoolDownShowUI: true });
        uint skill = book.BookSkillId(from);
        var (h, bar, _, _) = QuickSlotBar(m);
        float left = 3f;
        bar.SlotWorld = () => new SlotWorld
        {
            Tables = m.Tables.Skills, ItemData = m.Tables.Items.Get, Level = 99, HasPc = true, IsRiding = true,   // riding: the level 1 row (invenslot.cpp:505)
            CooldownRemain = (_, _) => left,
        };
        bar.SlotDroppedOn(MainPlayerInfoController.SlotBag + ".3", from, book);
        var calls = new List<string>();
        void Count(string n) { if (n.Contains("onCooldown")) calls.Add(n); }
        Interp.OnCall += Count;
        try
        {
            ulong key = m.Tables.Skills.GetSkillData(skill).CooldownKey;
            bar.OnCooldownStarted(key);
            Assert.That(calls.Count(c => c.EndsWith("onCooldownStart", StringComparison.Ordinal)), Is.EqualTo(1), string.Join(",", calls));
            bar.OnCooldownStarted(key + (3ul << 40));
            Assert.That(calls.Count, Is.EqualTo(1), "another key: no slot takes it");
            left = 0;
            bar.OnCooldownEnded(key);
            Assert.That(calls.Count(c => c.EndsWith("onCooldownEnd", StringComparison.Ordinal)), Is.EqualTo(1), string.Join(",", calls));
        }
        finally { Interp.OnCall -= Count; }
    }

    [Test]
    public void ABookSkillDroppedWithNoKeyTablePageShowsOnItsSlotUntilTheServerPagesComeIn()
    {
        // A new character's key table (mode 0, useDefault 1) has no page. SkillSlot::vfunc_18 (skillslot.cpp:551) still sends
        // mode 3 for the slot labelled 2 (index 9) and sets the slot's own skill, which its vfunc_8 shows. The server's mode 7
        // posts NotifyShortkeyPage only for a page in range (SetQuickSlotPage, keytable.cpp:2547), and UpdateQuickSlots
        // (uimainplayerinfo.cpp:1209) gives each slot its page entry: kept when the page holds it, emptied when not.
        var (bh, book, _) = Load();
        book.OnSkillBookTree(NoTabs());
        var m = bh.Model;
        string from = book.GetBagSlotBindings(SkillBookTreeController.BagName).First(n => m.Tables.Skills.GetSkillData(book.BookSkillId(n)) is { IsPassive: false });
        int skill = (int)book.BookSkillId(from);
        var t = new ServerKeyTable();
        t.Apply(new ByteReader(ServerKeyTable.Write(0, true, null, 0, null)));
        m.Keys = new KeyTable(Packs(), "en"); m.ServerKeys = t; m.Keys.Apply(t);
        var bar = new MainPlayerInfoController();
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        bar.Send = sent.Add;
        var (rt, p) = src.Open(bar.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, bar, m);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        string s9 = MainPlayerInfoController.SlotBag + ".9";
        string Icon() => Rt.ToString(h.Get((AsObject)h.ToAs(bar.GetBinding(s9)), "iconPath"));
        Assert.That((t.PageCount, Icon()), Is.EqualTo((0, "")));

        bar.SlotDroppedOn(s9, from, book);
        Assert.That(sent, Is.EqualTo(new[] { new KeyTableSetQuickSlotRequest(0, skill, 0, 0, 9) }));
        string icon = m.Tables.Skills.GetSkillData((uint)skill).Icon;
        Assert.That(icon, Is.Not.Empty);
        Assert.That(Icon(), Is.EqualTo(icon));

        void Server(byte[] packet) { t.Apply(new ByteReader(packet)); bar.OnKeyTable(); }
        Server(ServerKeyTable.Write(0, true, null, 0, null));
        Assert.That(Icon(), Is.EqualTo(icon), "a default-keys load takes no page");
        Server(ServerKeyTable.Write(7, false, null, 0, Array.Empty<IReadOnlyList<(int, QuickSlotEntry)>>()));
        Assert.That(Icon(), Is.EqualTo(icon), "no page in range: no notify");
        Server(ServerKeyTable.Write(7, false, null, 0, new IReadOnlyList<(int, QuickSlotEntry)>[] { new[] { (9, new QuickSlotEntry(skill, 0, 0)) } }));
        Assert.That(Icon(), Is.EqualTo(icon));
        Server(ServerKeyTable.Write(7, false, null, 0, new IReadOnlyList<(int, QuickSlotEntry)>[] { Array.Empty<(int, QuickSlotEntry)>() }));
        Assert.That(Icon(), Is.EqualTo(""));
    }
    /// <summary>A press, move and release on a learnt skill's slot (the user's drag out of the book). SkillSlot.handleMousePress
    /// re-dispatches a bubbling MOUSE_DOWN on its own m_slot, which bubbles back to the skill slot's MOUSE_DOWN listeners and
    /// so runs handleMousePress again: the movie recurses until the VM refuses the 129th call frame (VM::AddFrame,
    /// as3_callframe.h: Error #1023), and the listener that threw is output and ignored by the dispatch
    /// (EventDispatcher::DispatchSingleEvent, sfr_as3_09.cpp), so the press returns and the drag still starts.</summary>
    [Test]
    public void DraggingALearntSkillOutRecursesToTheFrameLimitAndStillStartsTheDrag()
    {
        var (h, c, _) = Load();
        c.OnSkillBookTree(NoTabs());
        var p = h.Player;
        var tree = h.Child("m_treeSkills");
        int n = (int)Rt.ToNumber(h.Get(tree, "numChildren"));
        var skill = Enumerable.Range(0, n).Select(i => h.Invoke(tree, "getChildAt", i)).Single(s => (uint)Rt.ToNumber(h.Get(s, "m_skillID")) == 10500001);
        var slot = (AsDisplayObject)h.Get(h.Get(skill, "m_skillSlot"), "m_slot");
        var b = slot.GetBounds(null);
        double x = b.X + b.W / 2, y = b.Y + b.H / 2;
        int before = h.RT.Errors.Count;
        int presses = 0;
        void Count(string m) { if (m.Contains("SkillSlot") && m.EndsWith("handleMousePress", StringComparison.Ordinal)) presses++; }
        Interp.OnCall += Count;
        try
        {
            p.MouseMove(x, y);
            p.MouseButton(true);
        }
        finally { Interp.OnCall -= Count; }
        var errors = h.RT.Errors.Skip(before).ToList();
        foreach (var e in errors) TestContext.WriteLine("  press error: " + e);
        TestContext.WriteLine($"SkillSlot.handleMousePress calls: {presses}");
        Assert.That(errors.Count(e => e.Contains("#1023")), Is.EqualTo(1), "one stack overflow error, output and ignored");
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(presses, Is.InRange(120, 128), "the recursion stops at the 128 frame limit");

        // the skill slot whose MOUSE_DOWN listener threw is marked (Dispatch: Mask_ExecutionAborted on the bubble phase's
        // object) and its own timeline no longer advances: the press's state tween stays on the label's first frame
        var skillSlot = (AsMovieClip)h.Get(skill, "m_skillSlot");
        Assert.That(skillSlot.ExecutionAborted, Is.True, "the skill slot's listener threw");
        Assert.That(slot.ExecutionAborted, Is.False, "the press's own target ran its listeners");
        for (int i = 0; i < 6; i++)
        {
            p.Tick();
            TestContext.WriteLine($"tick {i}: skill slot frame {skillSlot.Frame + 1} label {Rt.ToString(h.Get(skillSlot, "currentLabel"))}");
        }
        string label = Rt.ToString(h.Get(skillSlot, "currentLabel"));
        Assert.That(Rt.ToNumber(h.Get(skillSlot, "currentFrame")), Is.EqualTo(LabelFrame(h, skillSlot, label)),
            "the skill slot stays on the first frame of the state it went to");

        // SkillSlot.handleMouseDown armed the stage's MOUSE_MOVE: a move off the slot dispatches DRAG_START on m_slot
        // (SkillSlot.handleMouseMove), which bubbles to the stage, where the host hears it (UiStage.OnDragStart)
        var drags = new List<object>();
        p.Call(p.Stage, "addEventListener", new object[] { "dragStart",
            new AsPlainFunction(h.RT, new NativeMethod("dragStart", (_, a) => { drags.Add(h.Get(a[0], "dragTarget")); return Rt.Undef; })) });
        p.MouseMove(b.X + b.W + 20, y);
        Assert.That(drags, Is.Not.Empty.And.All.SameAs(slot), "the drag starts from the skill's slot");
        p.MouseMove(b.X + b.W + 300, y);
        p.MouseButton(false);
        Assert.That(h.RT.Errors.Skip(before + 1), Is.Empty);
    }

    /// <summary>The frame (1-based) of <paramref name="label"/> on <paramref name="mc"/>'s timeline (currentLabels).</summary>
    static double LabelFrame(UiHost h, AsObject mc, string label)
    {
        var labels = h.Get(mc, "currentLabels");
        int n = (int)Rt.ToNumber(h.Get(labels, "length"));
        for (int i = 0; i < n; i++)
        {
            var l = h.Get(labels, i.ToString());
            if (Rt.ToString(h.Get(l, "name")) == label) return Rt.ToNumber(h.Get(l, "frame"));
        }
        return double.NaN;
    }

    /// <summary>
    /// The user's drag of a learnt skill out of the book onto a quick slot, with both movies on one stage. The client starts
    /// the drag manager once, on main.gfx's stage (Main.addedToStage: DragManager.init(stage)), and every dialog uses it,
    /// so DRAG_START from the book's slot puts the skill's icon on the drag canvas and drags it (handleStartDragEvent); the
    /// release over the bar finds the bar's slot (handleEndDragEvent), and Main's drag end sends the drop to the bar:
    /// KeyTable mode 3.
    /// </summary>
    [Test]
    public void ABookSkillDraggedOntoAQuickSlotShowsItsIconAndIsSent()
    {
        var (bh, book, _) = Load();
        book.OnSkillBookTree(NoTabs());
        var (h, bar, sent, keys) = QuickSlotBar(bh.Model);
        var stage = new UiStage();
        (double X, double Y) mouse = (0, 0);
        stage.Add(h, () => mouse, () => 0);
        stage.Add(bh, () => mouse, () => 1);
        var p = bh.Player;
        void Move(double x, double y) { mouse = (x, y); p.MouseMove(x, y); }
        // the centred window covers the bar's skill slots: moved up out of the way first, as by its title bar
        bh.Set(bh.Entity, "y", Rt.ToNumber(bh.Get(bh.Entity, "y")) - 300);

        var tree = bh.Child("m_treeSkills");
        int n = (int)Rt.ToNumber(bh.Get(tree, "numChildren"));
        var skill = Enumerable.Range(0, n).Select(i => bh.Invoke(tree, "getChildAt", i)).Single(s => (uint)Rt.ToNumber(bh.Get(s, "m_skillID")) == 10500001);
        var slot = (AsDisplayObject)bh.Get(bh.Get(skill, "m_skillSlot"), "m_slot");
        var b = slot.GetBounds(null);
        double x = b.X + b.W / 2, y = b.Y + b.H / 2;
        var target = FindBound(h, h.Player.Stage, MainPlayerInfoController.SlotBag + ".3");
        Assert.That(target, Is.Not.Null, "the bar's quick slot 3");
        var tb = target.GetBounds(null);
        Assert.That(p.GetTopMostEntity(tb.X + tb.W / 2, tb.Y + tb.H / 2, true, null), Is.Null, "slot 3 is clear of the book");
        int before = bh.RT.Errors.Count;

        var sprites = new List<AsDisplayObject>();
        p.Call(p.Stage, "addEventListener", new object[] { "dragStart",
            new AsPlainFunction(bh.RT, new NativeMethod("dragStart", (_, a) => { sprites.Add(bh.Get(a[0], "dragSprite") as AsDisplayObject); return Rt.Undef; })) });
        Move(x, y);
        p.MouseButton(true);
        Move(x + 30, y + 10);
        var dm = bh.RT.GetClass("scaleform.clik.managers.DragManager");
        Assert.That(Rt.ToBoolean(bh.Invoke(dm, "inDrag")), Is.True, "DragManager is in a drag");
        Assert.That(sprites, Has.Count.EqualTo(1).And.All.Not.Null, "one DRAG_START with the skill's icon");
        var icon = sprites[0];
        Assert.That(p.DrawnOnTop, Is.SameAs(icon), "the dragged icon, drawn over the other movies");
        var up = icon;
        while (up != null && up != p.Stage)
        {
            Assert.That(up.visible, Is.True);
            Assert.That(up, Is.Not.SameAs(slot), "moved out of the slot onto the drag canvas");
            up = up.Parent;
        }
        Assert.That(up, Is.SameAs(p.Stage), "the icon is on the stage");

        Move(tb.X + tb.W / 2, tb.Y + tb.H / 2);
        p.MouseButton(false);
        Assert.That(Rt.ToBoolean(bh.Invoke(dm, "inDrag")), Is.False, "dropped");
        Assert.That(p.DrawnOnTop, Is.Null);
        Assert.That(sent, Is.EqualTo(new[] { new KeyTableSetQuickSlotRequest(0, 10500001, 0, 0, 3) }));
        // the BookSlot branch of SkillSlot::vfunc_18 (skillslot.cpp:547-557) sets the slot's own entry once the send is made and
        // never writes the key table page: the page fills only from the server's mode 7
        Assert.That(bar.ShownEntry(3), Is.EqualTo(new QuickSlotEntry(10500001, 0, 0)));
        Assert.That(keys.Entry(0, 3).IsEmpty, Is.True, "a book drop writes only the slot");
        var slot3 = (AsObject)h.ToAs(bar.GetBinding(MainPlayerInfoController.SlotBag + ".3"));
        Assert.That(Rt.ToString(h.Get(slot3, "iconPath")), Is.EqualTo(bh.Model.Tables.Skills.GetSkillData(10500001).Icon));
        Assert.That(bh.RT.Errors.Skip(before).Count(e => !e.Contains("#1023")), Is.Zero, string.Join("; ", bh.RT.Errors.Skip(before)));
    }

    /// <summary>The display object under <paramref name="root"/> whose binding is <paramref name="binding"/>.</summary>
    static AsDisplayObject FindBound(UiHost h, AsDisplayObject root, string binding)
    {
        object v = null;
        try { v = h.RT.GetProperty(root, Name.Public("binding")); } catch (AsThrow) { }
        if (v is string s && s == binding) return root;
        if (root is AsContainer c)
            foreach (var d in c.Children.ToList())
                if (FindBound(h, d, binding) is { } f) return f;
        return null;
    }
}

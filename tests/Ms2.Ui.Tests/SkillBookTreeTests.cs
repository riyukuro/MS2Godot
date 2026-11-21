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

        bar.SlotDroppedOn(MainPlayerInfoController.SlotBag + ".22", from, book);     // a key bound slot: nothing
        Assert.That(sent, Is.Empty);
        bar.SlotDroppedOn(MainPlayerInfoController.SlotBag + ".3", from, book);
        Assert.That(sent.Select(s => s.Op), Is.EqualTo(new[] { Ms2.Net.Outbound.KeyTable }));
        Assert.That(sent[0], Is.EqualTo(new KeyTableSetQuickSlotRequest(0, skill, 0, 0, 3)));
        Assert.That(t.Entry(0, 3), Is.EqualTo(new QuickSlotEntry(skill, 0, 0)));
        var slot3 = (AsObject)h.ToAs(bar.GetBinding(MainPlayerInfoController.SlotBag + ".3"));
        Assert.That(Rt.ToString(h.Get(slot3, "iconPath")), Is.EqualTo(m.Tables.Skills.GetSkillData((uint)skill).Icon));
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
}

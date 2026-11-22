using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The compact skill control window (uiskillcompactcontrol) run by its own ActionScript with
/// UISkillCompactControlDialog's C++ side, the 0xD1 / 0x7B packets and CSkillCompactControlTable (the movie tests need
/// MS2_DATA).</summary>
public class SkillCompactControlTests
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

    sealed class Rig
    {
        public UiHost H;
        public SkillCompactControlController C;
        public UiModel Model;
        public int Requests;
        public readonly List<IReadOnlyList<SkillMacroTab>> Saved = new(), Configs = new();
        public readonly List<bool> Capture = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly List<KeyBinding> Bound = new();
    }

    /// <summary>A rank 1 archer (job group 50) who learnt the rank's level 1 skills, an empty key table, the window loaded.</summary>
    static Rig Load()
    {
        var w = new Rig();
        w.Model = new UiModel(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f), HasLocalPlayer = true };
        w.Model.Keys = new KeyTable(Packs(), "en");
        w.Model.Keys.KeyBindingChanged += w.Bound.Add;
        var t = w.Model.Tables.Skills;
        var jobs = new JobSystem(t);
        jobs.Read(1, 50, t.Jobs.GetJobData(50).Skills.Select(r => new BookSkill((int)r.SkillId, 1, false, r.IsLearn, false)));
        w.C = new SkillCompactControlController { Jobs = jobs, ShowBox = w.Boxes.Add };
        w.C.SkillTooltip = (id, lv) => new AsTooltip { ["skillID"] = id, ["level"] = lv };
        w.C.RequestTabs += () => w.Requests++;
        w.C.Save += w.Saved.Add;
        w.C.ConfigsChanged += w.Configs.Add;
        w.C.HotKeyCapture += w.Capture.Add;
        var (rt, p) = src.Open(w.C.Movie, r => r.Budget = 40_000_000);
        p.StageW = 1280; p.StageH = 720;
        w.H = new UiHost(p, w.C, w.Model);
        Assert.That(w.H.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return w;
    }

    static SkillMacroPacket Tabs(params SkillMacroTab[] tabs) => SkillMacroPacket.Read(new ByteReader(SkillMacroPacket.Write(0, tabs)));

    static List<AsObject> Rows(UiHost h)
    {
        var dp = h.Get(h.Child("m_skillList"), "dataProvider");
        int n = (int)Rt.ToNumber(h.Get(dp, "length"));
        return Enumerable.Range(0, n).Select(i => (AsObject)h.Get(dp, i.ToString())).ToList();
    }

    static List<string> TabLabels(UiHost h)
    {
        var dp = h.Get(h.Child("m_buttonBar"), "dataProvider");
        int n = (int)Rt.ToNumber(h.Get(dp, "length"));
        return Enumerable.Range(0, n).Select(i => Rt.ToString(h.Get((AsObject)h.Get(dp, i.ToString()), "label"))).ToList();
    }

    // ---- packets ----------------------------------------------------------------------------------------------------

    [Test]
    public void TheMacroPacketReadsBothListModesAndRefusesATail()
    {
        var tab = new SkillMacroTab("a", 0xaae62, new[] { 3, -1, 2 });
        var p = SkillMacroPacket.Read(new ByteReader(SkillMacroPacket.Write(2, new[] { tab })));
        Assert.That(p.Mode, Is.EqualTo(2));
        Assert.That((p.Tabs[0].Name, p.Tabs[0].KeyAction), Is.EqualTo(("a", 0xaae62L)));
        Assert.That(p.Tabs[0].Skills, Is.EqualTo(new[] { 2, 3, -1 }), "a std::set of unsigned ids");
        var tail = SkillMacroPacket.Write(0, new[] { tab }).Append((byte)0).ToArray();
        Assert.That(() => SkillMacroPacket.Read(new ByteReader(tail)), Throws.Exception);
        Assert.That(SkillMacroPacket.Read(new ByteReader(new byte[] { 5, 1, 2 })).Tabs, Is.Empty, "another mode: left unread");
    }

    [Test]
    public void TheRequestAndSaveCarryTheirModes()
    {
        var w = new ByteWriter();
        new SkillMacroRequest().Write(w);
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 0 }));
        var s = new ByteWriter();
        new SkillMacroSave(new[] { new SkillMacroTab("b", 0, new[] { 7 }) }).Write(s);
        var r = new ByteReader(s.Buffer.AsSpan(0, s.Length).ToArray());
        Assert.That(r.ReadByte(), Is.EqualTo(1));
        Assert.That(r.ReadInt(), Is.EqualTo(1));
        var t = SkillMacroTab.Read(r);
        Assert.That((t.Name, t.KeyAction, t.Skills.Single()), Is.EqualTo(("b", 0L, 7)));
    }

    // ---- table ------------------------------------------------------------------------------------------------------

    [Test]
    public void TheTableKeepsAvailableRowsPerJobAndReadsNothingWhenOff()
    {
        var x = XDocument.Parse("<ms2><job code=\"50\"><skill id=\"5\" repeatCount=\"2\"/><skill id=\"6\" feature=\"X\"/></job>"
            + "<job code=\"60\" baseSP=\"70\"><skill id=\"9\"/></job></ms2>");
        var t = new SkillCompactControlTable(x, e => (string)e.Attribute("feature") == null, true);
        Assert.That(t.GetJobSkills(50).Select(s => s.SkillId), Is.EqualTo(new[] { 5u }));
        Assert.That(t.GetSkill(5).RepeatCount, Is.EqualTo(2));
        Assert.That(t.GetSkill(6), Is.Null);
        Assert.That((t.GetBaseSP(50), t.GetBaseSP(60), t.GetBaseSP(1)), Is.EqualTo((50L, 70L, 50L)));
        Assert.That(t.GetJobSkills(1), Is.Null);
        var off = new SkillCompactControlTable(x, _ => true, false);
        Assert.That(off.GetJobSkills(50), Is.Null);
    }

    [Test]
    public void FixedKeysAreNotRebindable()
    {
        foreach (int k in new[] { 1, 0x1c, 0x82, 0x83, 0xa3, 0xa6 }) Assert.That(KeyTable.IsKeyRebindable(k), Is.False, k.ToString());
        foreach (int k in new[] { 0x10, 0x1e, 0xa2, 0xa7 }) Assert.That(KeyTable.IsKeyRebindable(k), Is.True, k.ToString());
    }

    // ---- the window ---------------------------------------------------------------------------------------------------

    [Test]
    public void NoSavedTabsGiveOneTabOfEveryJobSkillLearntFirst()
    {
        var w = Load();
        Assert.That(w.Requests, Is.EqualTo(1), "SkillMacro mode 0 on load");
        Assert.That(w.C.OnCallback("onSelectAll", Array.Empty<object>()), Is.False);
        Assert.That(w.Configs, Is.Empty, "nothing taken before the tabs arrive");

        w.C.OnSkillMacro(Tabs());
        Assert.That(w.C.Loaded, Is.True);
        Assert.That(w.C.Tabs.Count, Is.EqualTo(1));
        Assert.That(w.C.Tabs[0].Name, Is.EqualTo(MessageBoxData.Common(0xc39)));
        Assert.That(TabLabels(w.H), Is.EqualTo(new[] { MessageBoxData.Common(0xc39) }));

        var table = w.Model.Tables.SkillCompactControl.GetJobSkills(50);
        Assert.That(table, Is.Not.Null.And.Not.Empty);
        var rows = Rows(w.H);
        TestContext.WriteLine(string.Join(", ", rows.Select(r => $"{w.H.Get(r, "skillID")}:{w.H.Get(r, "disable")}")));
        Assert.That(rows, Is.Not.Empty);
        var disable = rows.Select(r => (int)Rt.ToNumber(w.H.Get(r, "disable"))).ToList();
        Assert.That(disable, Is.Ordered, "learnt (0) rows before the rest (1)");
        var ids = rows.Select(r => (uint)Rt.ToNumber(w.H.Get(r, "skillID"))).ToList();
        Assert.That(ids.Where((_, i) => disable[i] == 0), Is.Ordered);
        Assert.That(ids.Where((_, i) => disable[i] == 1), Is.Ordered);
        Assert.That(ids.All(id => w.C.Tabs[0].Skills.Contains(id)), Is.True, "a new tab ticks every job skill");
        Assert.That(rows.All(r => (int)Rt.ToNumber(w.H.Get(r, "selected")) == 1), Is.True);
        Assert.That(Rt.ToString(w.H.Get(rows[0], "iconPath")), Is.EqualTo(w.Model.Tables.Skills.GetSkillData(ids[0]).Icon));
    }

    [Test]
    public void SavedTabsAreShownAndTheCheckboxEditsTheCurrentOne()
    {
        var w = Load();
        uint first = w.Model.Tables.SkillCompactControl.GetJobSkills(50)[0].SkillId;
        w.C.OnSkillMacro(Tabs(new SkillMacroTab("one", 0xaae61, new[] { (int)first }), new SkillMacroTab("two", 0, Array.Empty<int>())));
        Assert.That(TabLabels(w.H), Is.EqualTo(new[] { "one", "two" }));
        Assert.That(w.C.CurrentTab, Is.EqualTo("one"));
        var sel = Rows(w.H).ToDictionary(r => (uint)Rt.ToNumber(w.H.Get(r, "skillID")), r => (int)Rt.ToNumber(w.H.Get(r, "selected")));
        Assert.That(sel.Count(kv => kv.Value == 1), Is.EqualTo(sel.ContainsKey(first) ? 1 : 0));

        w.C.OnCallback("onClickCompactControlCheckbox", new object[] { (int)first, false });
        Assert.That(w.C.FindTab("ONE").Skills, Is.Empty, "FindTab ignores case");
        Assert.That(w.Configs.Last().Single(t => t.Name == "one").Skills, Is.Empty);
        w.C.OnCallback("onChangeTab", new object[] { "two" });
        w.C.OnCallback("onClickCompactControlCheckbox", new object[] { (int)first, true });
        Assert.That(w.C.FindTab("two").Skills, Is.EqualTo(new[] { first }));

        // the tab key actions: a held one is kept, a free tab takes the first unused
        Assert.That(w.C.GetTabKeyId("one"), Is.EqualTo(0xaae61));
        Assert.That(w.C.GetTabKeyId("two"), Is.EqualTo(0xaae62));
        Assert.That(w.C.GetTabKeyId("none"), Is.EqualTo(0));

        w.C.OnCallback("onSelectAll", Array.Empty<object>());
        Assert.That(w.C.FindTab("two").Skills, Is.EqualTo(w.C.GetJobSkills().OrderBy(x => x)));
        w.C.OnCallback("onRemoveTab", new object[] { "two" });
        Assert.That(w.C.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "one" }));

        w.C.Close();
        Assert.That(w.Capture.Last(), Is.False);
        var saved = w.Saved.Single();
        Assert.That(saved.Select(t => (t.Name, t.KeyAction)), Is.EqualTo(new[] { ("one", 0xaae61L) }));
        Assert.That(w.C.Loaded, Is.False);
    }

    [Test]
    public void AddAndRenameRefuseAUsedNameAndStopAtThreeTabs()
    {
        var w = Load();
        w.C.OnSkillMacro(Tabs(new SkillMacroTab("one", 0, Array.Empty<int>())));
        w.C.OnCallback("onAddTab", Array.Empty<object>());
        var add = w.Boxes.Single();
        Assert.That(add.Type, Is.EqualTo(10));
        add.ClickedTag = "0,";
        Assert.That(add.Callback(0, add), Is.False, "no name keeps the box");
        add.ClickedTag = "0,ONE";
        Assert.That(add.Callback(0, add), Is.False, "a used name keeps the box");
        Assert.That(add.Callback(1, add), Is.True, "cancel closes it");
        add.ClickedTag = "0,two";
        Assert.That(add.Callback(0, add), Is.True);
        Assert.That(w.C.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "one", "two" }));
        Assert.That(w.C.CurrentTab, Is.EqualTo("two"));
        Assert.That(TabLabels(w.H), Is.EqualTo(new[] { "one", "two" }));
        add.ClickedTag = "0,three";
        Assert.That(add.Callback(0, add), Is.True);
        w.Boxes.Clear();
        w.C.OnCallback("onAddTab", Array.Empty<object>());
        Assert.That(w.Boxes, Is.Empty, "three tabs: no box");
        add.ClickedTag = "0,four";
        Assert.That(add.Callback(0, add), Is.False);
        Assert.That(w.C.Tabs.Count, Is.EqualTo(3));

        w.C.OnCallback("onRenameTab", new object[] { "two" });
        var ren = w.Boxes.Single();
        ren.ClickedTag = "0,three";
        Assert.That(ren.Callback(0, ren), Is.False);
        ren.ClickedTag = "0,zwei";
        Assert.That(ren.Callback(0, ren), Is.True);
        Assert.That(w.C.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "one", "zwei", "three" }));
        Assert.That(TabLabels(w.H), Does.Contain("zwei"));
    }

    [Test]
    public void TheHotKeyFieldBindsAFreeKeyToTheTabAction()
    {
        var w = Load();
        w.C.OnSkillMacro(Tabs(new SkillMacroTab("one", 0, Array.Empty<int>())));
        Assert.That(w.C.CapturingKey, Is.False);
        Assert.That(w.C.OnKey(0x10), Is.False, "no capture before the field takes focus");
        w.C.OnCallback("onHotKeyFocusIn", Array.Empty<object>());
        Assert.That(w.Capture.Last(), Is.True);
        Assert.That(w.C.CapturingKey, Is.True);

        Assert.That(w.C.OnKey(1), Is.True, "a fixed key");
        Assert.That(w.Boxes.Single().Msg, Is.EqualTo(MessageBoxData.Common(0x84c)));
        Assert.That(w.C.CapturingKey, Is.False, "the edit is dropped");

        w.C.OnCallback("onHotKeyFocusIn", Array.Empty<object>());
        int free = Enumerable.Range(0x02, 0x80).First(k => KeyTable.IsKeyRebindable(k) && w.Model.Keys.GetKeyAction(k) == 0);
        Assert.That(w.C.OnKey(free), Is.True);
        Assert.That(w.Model.Keys.GetKeyAction(free), Is.EqualTo(0xaae61));
        Assert.That(w.C.Tabs[0].KeyAction, Is.EqualTo(0xaae61));
        Assert.That(w.Bound.Any(b => b.Key == free), Is.True, "synced to the server");
        Assert.That(w.C.CapturingKey, Is.False);
        Assert.That(Rt.ToString(w.H.Get(w.H.Child("m_hotKey"), "label")), Is.EqualTo(w.Model.Keys.GetKeyName(free)));

        // reset: the tab's skills cleared and its key unbound
        w.C.OnCallback("onResetHotKey", Array.Empty<object>());
        Assert.That(w.Model.Keys.GetKeyAction(free), Is.EqualTo(0));
        Assert.That(w.C.Tabs[0].KeyAction, Is.EqualTo(0));
        Assert.That(w.C.Tabs[0].Skills, Is.Empty);
    }

    [Test]
    public void TheTooltipIsTheSkillAtItsBookLevel()
    {
        var w = Load();
        uint id = w.Model.Tables.SkillCompactControl.GetJobSkills(50)[0].SkillId;
        var tip = w.C.GetTooltip(new object[] { "compactSkill", id });
        Assert.That(tip["skillID"], Is.EqualTo(id));
        Assert.That(tip["level"], Is.EqualTo(w.C.Jobs.FindSkill(id)?.Level ?? 1));
        Assert.That(w.C.GetTooltip(new object[] { "other", id }), Is.Null);
    }

    [Test]
    public void AKeyBoundToTheOpenActionOpensTheWindowAndNoDefaultKeyDoes()
    {
        // keytable.cpp:881: action 400031 toggles 0x4ef6; defaultkey.xml binds no key to it
        var keys = new KeyTable(Packs(), "en");
        Assert.That(keys.KeysOfAction(SkillCompactControlController.OpenKeyAction), Is.Empty);
        int free = Enumerable.Range(0x02, 0x80).First(k => KeyTable.IsKeyRebindable(k) && keys.GetKeyAction(k) == 0);
        Assert.That(SkillCompactControlController.OpensWithKey(keys, free), Is.False);
        Assert.That(keys.SetKeyBindingAndSync(free, 0, SkillCompactControlController.OpenKeyAction, 0, 0), Is.True);
        Assert.That(SkillCompactControlController.OpensWithKey(keys, free), Is.True);
        Assert.That(SkillCompactControlController.OpensWithKey(keys, free + 1), Is.False);
    }
}

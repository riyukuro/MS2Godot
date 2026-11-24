using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The guide (CGuideManager, client/ms2/guideentity_1b.cpp): the table load (needs MS2_DATA), an event's state
/// machine on a small table, the occurrence index, the limits and the GuideRecord packets.</summary>
public class GuideTests
{
    sealed class Pane : IGuideEventPane
    {
        public bool Loaded => true;
        public void ShowTutorial(int entityId, int textId, bool first) { }
        public void HideTutorial(int entityId) { }
        public bool TutorialShowing => false;
        public void ShowSummary(int entityId, int textId, int durationMs = 0) { }
        public void HideSummary(int entityId) { }
        public bool SummaryShowing => false;
        public void AddArrow(GuideArrow arrow) { }
        public void ReleaseArrow(int entityId) { }
        public void QueueOrShowTextTip(int entityId, int textId) { }
        public void AdvanceTextTip(int entityId) { }
        public int LastClosedTip => 0;
    }

    internal sealed class World : GuideWorld
    {
        readonly Pane pane = new();
        public uint Time = 1000;
        public int Mode = 1;
        public readonly List<string> Log = new();
        public override uint Now => Time;
        public override IGuideEventPane Pane => pane;
        public override int GuideMode => Mode;
        public override void GuideLog(string text) => Log.Add(text);
        public bool KeyHeld;
        public override bool IsKeyDown(int key) => KeyHeld;
    }

    static GuideManager Make(World world, string xml)
    {
        var root = XElement.Parse(xml);
        var table = new GuideTable();
        table.Files.Add(new GuideTableFile("table/test.xml", root.Elements("event").ToList()));
        var g = new GuideManager(world);
        g.Load(table);
        return g;
    }

    const string Table = """
        <ms2>
          <event id="10" saveId="5" saveStep="1">
            <open><condition type="trigger" trigger="ShopOpen" value="7" /></open>
            <close><condition type="timeout" value="1000" /></close>
            <openAction>
              <action type="limit" limit="PlayerMove" />
              <action type="limitDialog" dialogName="QuestDialog" />
            </openAction>
          </event>
          <event id="20" saveId="5" saveStep="2">
            <open><condition type="guideClear" id="10" /></open>
            <close><condition type="false" /></close>
          </event>
          <event id="30" saveId="3" saveStep="1">
            <open><condition type="false" /></open>
          </event>
        </ms2>
        """;

    [Test]
    public void EventRunsOnItsTriggerThenClosesAndOpensTheNext()
    {
        var w = new World();
        var g = Make(w, Table);
        Assert.That(g.LoadErrors, Is.Empty);
        Assert.That(g.Entities.Select(e => e.Id), Is.EqualTo(new[] { 10, 20, 30 }));
        Assert.That(g.IndexedOccurrences, Is.EqualTo(new[] { (int)GuideOccurrence.GuideClear }));
        Assert.That(g.FindEntity(20).TriggerConditions, Has.Count.EqualTo(1));
        Assert.That(g.FindEntity(20).OpenConditions, Is.Empty);

        // The first update: 20's trigger condition fails, so it leaves the queue; 10 and 30 wait on open conditions.
        w.Time = 1300; g.Tick();
        Assert.That(g.Queued.Select(e => e.Id), Is.EqualTo(new[] { 10, 30 }));
        Assert.That(g.FindEntity(10).State, Is.EqualTo(0));

        // A trigger of another value does not open 10; the triggers last one update.
        g.AddTrigger(GuideTrigger.ShopOpen, 8);
        w.Time = 1600; g.Tick();
        Assert.That(g.FindEntity(10).State, Is.EqualTo(0));
        Assert.That(g.CheckTrigger(GuideTrigger.ShopOpen, null, null), Is.False);

        g.AddTrigger(GuideTrigger.ShopOpen, 7);
        Assert.That(g.CheckTrigger(GuideTrigger.ShopOpen, 7, null), Is.True);
        Assert.That(g.CheckTrigger(GuideTrigger.ShopOpen, 7, 1), Is.False);
        w.Time = 1900; g.Tick();
        var e10 = g.FindEntity(10);
        Assert.That(e10.State, Is.EqualTo(2));                   // the actions are done at once
        Assert.That(g.IsRestricted(GuideLimit.PlayerMove), Is.True);
        Assert.That(g.IsSkillRestricted(GuideNames.ActionMoveUp), Is.True);
        Assert.That(g.IsSkillRestricted(GuideNames.ActionTotal), Is.False);
        Assert.That(g.IsDialogLimited("QuestDialog"), Is.True);

        // The timeout counts from its first test (1900): not closed at 2800, closed at 2900.
        w.Time = 2200; g.Tick();
        w.Time = 2800; g.Tick();
        Assert.That(e10.State, Is.EqualTo(2));
        Assert.That(g.StateChanged, Is.False);
        w.Time = 3100; g.Tick();
        Assert.That(e10.State, Is.EqualTo(0));
        Assert.That(g.IsCleared(10), Is.True);
        Assert.That(g.IsRestricted(GuideLimit.PlayerMove), Is.False);
        Assert.That(g.IsDialogLimited("QuestDialog"), Is.False);
        Assert.That(g.StateChanged, Is.True);
        Assert.That(g.SavedSteps, Is.EqualTo(new[] { new KeyValuePair<int, int>(5, 1) }));

        // The GuideClear occurrence of 10 queues 20, which opens; 10 is cleared and leaves the queue.
        w.Time = 3400; g.Tick();
        Assert.That(g.FindEntity(20).State, Is.EqualTo(2));
        Assert.That(g.Queued.Select(e => e.Id), Is.EqualTo(new[] { 30, 20 }));
        Assert.That(g.IsCleared(20), Is.False);

        var record = g.SendStates();
        Assert.That(record.Steps, Is.EqualTo(new[] { (5, 1) }));
        Assert.That(g.StateChanged, Is.False);
    }

    [Test]
    public void GuideOffRunsOnlyForcedEvents()
    {
        var w = new World { Mode = 0 };
        var g = Make(w, """
            <ms2>
              <event id="1"><close><condition type="false" /></close></event>
              <event id="2" isForce="1"><close><condition type="false" /></close></event>
            </ms2>
            """);
        w.Time = 1300; g.Tick();
        Assert.That(g.FindEntity(1).State, Is.EqualTo(0));
        Assert.That(g.FindEntity(2).State, Is.EqualTo(2));
    }

    [Test]
    public void LoadErrorsLeaveTheEventOut()
    {
        var w = new World();
        var g = Make(w, """
            <ms2>
              <event id="1" />
              <event id="1" />
              <event id="2"><open><condition type="noSuchCondition" /></open></event>
              <event id="3"><openAction><action type="noSuchAction" /></openAction></event>
              <event id="4"><open><condition type="trigger" trigger="noSuchTrigger" /></open></event>
            </ms2>
            """);
        Assert.That(g.Entities.Select(e => e.Id), Is.EqualTo(new[] { 1 }));
        Assert.That(g.LoadErrors.Select(s => s.Substring(0, s.IndexOf(']') + 1)),
            Is.EqualTo(new[] { "Guide Error: [ID:1]", "Guide Error: [ID:2]", "Guide Error: [ID:3]", "Guide Error: [ID:4]" }));
        w.Time = 1300; g.Tick();
        Assert.That(g.LoadErrors, Is.Empty);
        Assert.That(w.Log, Has.Count.EqualTo(4));
    }

    [Test]
    public void ProgressKeepsTheHighestStepInUnsignedSaveIdOrder()
    {
        var w = new World();
        var g = Make(w, Table);
        int high = unchecked((int)0x80000001);
        g.ReceiveStates(new GuideRecordIn(new[] { (high, 4), (9, 1), (0, 7), (3, 1) }, 0x05));
        Assert.That(g.RecordBits, Is.EqualTo(0x05));
        Assert.That(g.StateChanged, Is.False);
        Assert.That(g.SavedSteps.Select(p => p.Key), Is.EqualTo(new[] { 3, 9, high }));
        Assert.That(g.IsCleared(30), Is.True);                   // save id 3 reached step 1
        Assert.That(g.IsCleared(10), Is.False);
        Assert.That(g.IsCleared(99), Is.False);                  // no such event

        // FinishById saves 10's step; 20 (same save id, step 2) is not cleared by it.
        g.FinishById(10);
        Assert.That(g.IsCleared(10), Is.True);
        Assert.That(g.IsCleared(20), Is.False);
        Assert.That(g.SendStates().Steps, Is.EqualTo(new[] { (3, 1), (5, 1), (9, 1), (high, 4) }));

        // Save id 1 clears every event with a save id.
        g.SetInitialState();
        Assert.That(g.StateChanged, Is.True);
        Assert.That(g.IsCleared(20), Is.True);

        g.ClearStates();
        Assert.That(g.SavedSteps, Is.Empty);
        Assert.That(g.IsCleared(10), Is.False);
    }

    [Test]
    public void LimitsCountTheEventsHoldingThem()
    {
        var g = new GuideManager(new World());
        g.RemoveLimit(GuideLimit.PlayerControl);                 // at 0 it stays 0
        g.AddLimit(GuideLimit.PlayerControl);
        g.AddLimit(GuideLimit.PlayerControl);
        g.RemoveLimit(GuideLimit.PlayerControl);
        Assert.That(g.IsRestricted(GuideLimit.PlayerControl), Is.True);
        Assert.That(g.IsSkillRestricted(GuideNames.ActionTotal), Is.True);
        g.RemoveLimit(GuideLimit.PlayerControl);
        Assert.That(g.IsRestricted(GuideLimit.PlayerControl), Is.False);

        g.AddLimit(GuideLimit.CubeKeyUp);
        Assert.That(g.IsActionRestricted(0x11), Is.True);
        Assert.That(g.IsActionRestricted(0x1f), Is.False);
        Assert.That(g.IsActionRestricted(1), Is.False);
        g.AddLimit(GuideLimit.CubeKeyAll);
        Assert.That(g.IsActionRestricted(1), Is.True);

        g.RemoveDialogLimit("QuestDialog");
        g.AddDialogLimit("QuestDialog");
        Assert.That(g.IsDialogLimited("QuestDialog"), Is.True);
        Assert.That(g.IsDialogLimited("questdialog"), Is.False);  // a case-sensitive CRBMap<CStringA>
        g.RemoveDialogLimit("QuestDialog");
        Assert.That(g.IsDialogLimited("QuestDialog"), Is.False);
    }

    [Test]
    public void GuideRecordPackets()
    {
        var w = new ByteWriter();
        new GuideRecordOut(new[] { (3, 1), (5, 2) }).Write(w);
        var bytes = w.Buffer.AsSpan(0, w.Length).ToArray();
        Assert.That(bytes, Is.EqualTo(new byte[] { 2, 0, 0, 0, 3, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0, 2, 0, 0, 0 }));

        var inb = new ByteWriter();
        inb.WriteInt(3);
        inb.WriteInt(3); inb.WriteInt(1);
        inb.WriteInt(0); inb.WriteInt(9);                        // save id 0 is skipped
        inb.WriteInt(5); inb.WriteInt(2);
        inb.WriteByte(0x81);
        var rec = GuideRecordIn.Read(new ByteReader(inb.Buffer.AsSpan(0, inb.Length).ToArray()));
        Assert.That(rec.Steps, Is.EqualTo(new[] { (3, 1), (5, 2) }));
        Assert.That(rec.Bits, Is.EqualTo(0x81));
    }

    // ---- the tables (MS2_DATA) ------------------------------------------------------------------------------------------

    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(d);
    }

    static int CountIn(GuideTable t, string name) =>
        t.Files.Single(f => string.Equals(f.Path, GuideTable.PathOf(name), StringComparison.OrdinalIgnoreCase)).Events.Count;

    [Test]
    public void TablesLoadEveryImportedEvent()
    {
        var t = GuideTable.Load(Packs());
        Assert.That(t.BadFiles, Is.Empty);
        // 787 <event>s in the files; the feature filter keeps 750 of them on this data (no exact count asserted:
        // the counts are the data's, ours is that every file's events load)
        Assert.That(t.Events.Count(), Is.GreaterThan(0));
        Assert.That(CountIn(t, "guide_common.xml"), Is.GreaterThan(0));
        Assert.That(CountIn(t, "guide_mapleguide.xml"), Is.GreaterThan(0));
        Assert.That(CountIn(t, "guide_tutorial_100.xml"), Is.GreaterThan(0));
        Assert.That(t.Files.Any(f => f.Path.Contains("guide_sample", StringComparison.OrdinalIgnoreCase)), Is.False);

        int Sections(string name) => t.Events.Count(e => e.Element(name) != null);
        Assert.That(Sections("open"), Is.GreaterThan(0));
        Assert.That(Sections("close"), Is.GreaterThan(0));
        Assert.That(Sections("openAction"), Is.GreaterThan(0));
        Assert.That(Sections("cancel"), Is.GreaterThan(0));
    }

    [Test]
    public void ManagerLoadsTheTablesWithoutErrors()
    {
        var t = GuideTable.Load(Packs());
        var g = new GuideManager(new World());
        g.Load(t);
        Assert.That(g.LoadErrors, Is.Empty);
        Assert.That(g.EntityCount, Is.EqualTo(t.Events.Count()), "every event is an entity");
        Assert.That(g.Queued.Count(), Is.EqualTo(g.EntityCount), "all queued at the start");
        Assert.That(g.FindEntity(100), Is.Not.Null);
        Assert.That(g.FindEntity(100).Force, Is.EqualTo(1));
    }

    sealed class DialogWorld : GuideWorld
    {
        sealed class Dlg : GuideDialog { public override bool Hidden => false; public override int SubIndex => 3; }
        readonly Pane pane = new();
        public uint Time = 1000;
        public bool BagOpen;
        public override uint Now => Time;
        public override IGuideEventPane Pane => pane;
        public override int GuideMode => 1;
        public override void GuideLog(string text) { }
        public override GuideDialog FindDialog(string name) => BagOpen && name == "InventoryDialog" ? new Dlg() : null;
    }

    /// <summary>CGuideConditionDialog's constructor (@0x1413a60c0) sets isOpen 1 and subIndex -1: the tutorial's "open the
    /// bag" close (dialogName only) is met by an open bag of any sub index and holds while it is shut.</summary>
    [Test]
    public void DialogConditionWithoutAttributesMeansOpenAnySubIndex()
    {
        var w = new DialogWorld();
        var g = new GuideManager(w);
        var table = new GuideTable();
        table.Files.Add(new GuideTableFile("table/test.xml", XElement.Parse("""
            <ms2>
              <event id="170" saveId="110" saveStep="70">
                <open><condition type="trigger" trigger="ShopOpen" value="7" /></open>
                <close><condition type="dialog" dialogName="InventoryDialog" /></close>
                <openAction><action type="limit" limit="ItemPutOn" /></openAction>
              </event>
            </ms2>
            """).Elements("event").ToList()));
        g.Load(table);
        Assert.That(g.LoadErrors, Is.Empty);
        w.Time = 1300; g.Tick();
        g.AddTrigger(GuideTrigger.ShopOpen, 7);
        w.Time = 1600; g.Tick();
        Assert.That(g.FindEntity(170).State, Is.EqualTo(2));
        Assert.That(g.IsRestricted(GuideLimit.ItemPutOn), Is.True);
        w.Time = 1900; g.Tick();
        Assert.That(g.FindEntity(170).State, Is.EqualTo(2), "the bag is shut");
        w.BagOpen = true;
        g.QueueOccurrence(GuideOccurrence.Dialog, 0);
        w.Time = 2200; g.Tick();
        w.Time = 2500; g.Tick();
        Assert.That(g.FindEntity(170).State, Is.EqualTo(0), "the open bag closes it");
        Assert.That(g.IsRestricted(GuideLimit.ItemPutOn), Is.False);
    }

    sealed class ClosedWorld : GuideWorld
    {
        sealed class Dlg : GuideDialog { readonly string n; public Dlg(string n) { this.n = n; } public override string TypeName => n; public override bool Hidden => false; }
        readonly Pane pane = new();
        public uint Time = 1000;
        public readonly HashSet<string> Open = new();
        public override uint Now => Time;
        public override IGuideEventPane Pane => pane;
        public override int GuideMode => 1;
        public override void GuideLog(string text) { }
        public override GuideDialog FindDialog(string name) =>
            GuideDialogLookup.Resolve(name, n => Open.Contains(n) ? new Dlg(n) : null);
    }

    /// <summary>The "close all windows" step (event 250 of the tutorial) closes on dialogs with isOpen 0. The client's registry
    /// answers a closed dialog; the port frees it, so a dialog that was open and was closed must still meet isOpen 0.</summary>
    [Test]
    public void ClosedDialogMeetsIsOpenZero()
    {
        var w = new ClosedWorld();
        var g = new GuideManager(w);
        var table = new GuideTable();
        table.Files.Add(new GuideTableFile("table/test.xml", XElement.Parse("""
            <ms2>
              <event id="250" saveId="110" saveStep="71">
                <open><condition type="trigger" trigger="ShopOpen" value="7" /></open>
                <close>
                  <condition type="dialog" dialogName="MyInfoDialog" dialogID="10001" isOpen="0" />
                  <condition type="dialog" dialogName="InventoryDialog" isOpen="0" />
                </close>
              </event>
            </ms2>
            """).Elements("event").ToList()));
        g.Load(table);
        Assert.That(g.LoadErrors, Is.Empty);
        w.Time = 1300; g.Tick();
        g.AddTrigger(GuideTrigger.ShopOpen, 7);
        w.Open.Add("MyInfoDialog"); w.Open.Add("InventoryDialog");
        w.Time = 1600; g.Tick();
        w.Time = 1900; g.Tick();
        Assert.That(g.FindEntity(250).State, Is.EqualTo(2));
        w.Open.Clear();     // both closed and freed
        g.QueueOccurrence(GuideOccurrence.Dialog, 0);
        w.Time = 2200; g.Tick();
        w.Time = 2500; g.Tick();
        Assert.That(g.FindEntity(250).State, Is.EqualTo(0), "closing the windows closes it");
    }

    /// <summary>The closed-dialog answer is one kept stand-in per name: a guide tick's lookup allocates nothing.</summary>
    [Test]
    public void ClosedDialogLookupAllocatesNothing()
    {
        Func<string, GuideDialog> none = _ => null;
        var first = GuideDialogLookup.Resolve("InventoryDialog", none);
        Assert.That(first, Is.Not.Null);
        Assert.That(first.Hidden, Is.True);
        Assert.That(GuideDialogLookup.Resolve("NotARegisteredDialog", none), Is.Null);
        // the least of up to 5 windows (Alloc.Least): the JIT's pass through a loop may allocate once
        long bytes = Alloc.Least(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                GuideDialogLookup.Resolve("InventoryDialog", none);
                GuideDialogLookup.Resolve("NotARegisteredDialog", none);
            }
        });
        Assert.That(bytes, Is.EqualTo(0));
        Assert.That(GuideDialogLookup.Resolve("inventorydialog", none), Is.SameAs(first));
    }

    /// <summary>Event 250 with neither window opened in this process (a restart on the saved step): the client's registry holds
    /// both from the start, closed, so the step is met.</summary>
    [Test]
    public void NeverOpenedRegisteredDialogsMeetIsOpenZero()
    {
        var w = new ClosedWorld();
        var g = new GuideManager(w);
        var table = new GuideTable();
        table.Files.Add(new GuideTableFile("table/test.xml", XElement.Parse("""
            <ms2>
              <event id="250" saveId="110" saveStep="71">
                <open><condition type="trigger" trigger="ShopOpen" value="7" /></open>
                <close>
                  <condition type="dialog" dialogName="MyInfoDialog" dialogID="10001" isOpen="0" />
                  <condition type="dialog" dialogName="InventoryDialog" isOpen="0" />
                </close>
              </event>
            </ms2>
            """).Elements("event").ToList()));
        g.Load(table);
        w.Time = 1300; g.Tick();
        g.AddTrigger(GuideTrigger.ShopOpen, 7);
        w.Time = 1600; g.Tick();
        w.Time = 1900; g.Tick();
        g.QueueOccurrence(GuideOccurrence.Dialog, 0);
        w.Time = 2200; g.Tick();
        w.Time = 2500; g.Tick();
        Assert.That(g.FindEntity(250).State, Is.EqualTo(0));
    }

    /// <summary>The registry's names are each class's GetTypeName result; objects registered without RegisterDialog are no dialogs.</summary>
    [Test]
    public void RegisteredDialogNamesAreTheTypeNameResults()
    {
        foreach (var name in new[] { "InventoryDialog", "MyInfoDialog", "AdditionalEffectControlDialog", "HomeMenuDialog", "DynamicAction",
                     "SkillCompactControl", "GuideMovieDialog", "CashAttendGiftDialog", "SkillBookTreeDialog", "SkillBookTreeDialog_jobchange01",
                     "TriggerTimerDialog", "WebBrowser", "TencentUserReportDialog" })
            Assert.That(RegisteredDialogs.Contains(name), Is.True, name);
        foreach (var name in new[] { "AdditionalControlDialog", "PhotoStudioMenuDialog", "PCInfoDialog", "IGuideMovieDialog", "GuideEvent", "ExpBar", "ActionTooltip", "" })
            Assert.That(RegisteredDialogs.Contains(name), Is.False, name);
        Assert.That(GuideDialogLookup.Resolve("GuideEvent", _ => null), Is.Null);
    }

    sealed class SlotWorld : GuideWorld
    {
        public bool Slotted;
        public override int FindQuickSlotBySkill(int skillId) => Slotted ? 4 : -1;
    }

    /// <summary>CGuideConditionEquipSkill's constructor (@0x1413a8420) sets isEquip 1: without the attribute the condition is
    /// met by a slotted skill, as with isEquip="1", and not by an empty quick bar.</summary>
    [Test]
    public void EquipSkillConditionWithoutAttributeMeansSlotted()
    {
        var w = new SlotWorld();
        var g = new GuideManager(w);
        var bare = new EquipSkillCondition(); bare.Load(XElement.Parse("<condition type=\"equipSkill\" value=\"5\" />"));
        var explicitOne = new EquipSkillCondition(); explicitOne.Load(XElement.Parse("<condition type=\"equipSkill\" value=\"5\" isEquip=\"1\" />"));
        w.Slotted = true;
        Assert.That(bare.Check(g), Is.EqualTo(explicitOne.Check(g)));
        int met = bare.Check(g);
        w.Slotted = false;
        Assert.That(bare.Check(g), Is.Not.EqualTo(met), "an empty quick bar does not meet it");
    }

    sealed class FieldWorld : GuideWorld
    {
        readonly Pane pane = new();
        public uint Time = 1000;
        public bool Entered, Bound;
        public readonly List<Ms2.Net.Wire.IOutPacket> Sent = new();
        public override uint Now => Time;
        public override IGuideEventPane Pane => pane;
        public override int GuideMode => 1;
        public override int SceneId => Entered ? 0x620000c : 0;
        public override bool HasPlayer => Bound;
        public override int FieldId => 52000065;
        public override void Send(Ms2.Net.Wire.IOutPacket packet) => Sent.Add(packet);
    }

    /// <summary>The guide ticks from the own pc's update (MyPC::vfunc_53, mypc.cpp:2976). The tutorial field's server
    /// trigger event (guide_tutorial_100's event 251 shape: the field and a cleared event) opens only once the own pc is in
    /// the entered field. The recordings send it after ResponseFieldEnter; an earlier send reaches a field whose trigger
    /// has not made its guide widget yet.</summary>
    [Test]
    public void ServerTriggerEventWaitsForTheOwnPcInTheEnteredField()
    {
        var w = new FieldWorld();
        var g = new GuideManager(w);
        var table = new GuideTable();
        table.Files.Add(new GuideTableFile("table/test.xml", XElement.Parse("""
            <ms2>
              <event id="250" saveId="110" saveStep="150"><open><condition type="false" /></open></event>
              <event id="251" isForce="1" isNoSave="1">
                <open>
                  <condition type="fieldId" value="52000065" />
                  <condition type="anyOne"><condition type="guideClear" id="250" /></condition>
                </open>
                <close />
                <openAction><action type="ServerTriggerEvent" eventID="251" /></openAction>
              </event>
            </ms2>
            """).Elements("event").ToList()));
        g.Load(table);
        Assert.That(g.LoadErrors, Is.Empty);
        g.SetInitialState();
        g.QueueOccurrence(GuideOccurrence.FieldId, 52000065);

        // the field loads: no own pc, then a bound pc before the field entry is answered
        for (int i = 0; i < 4; i++) { w.Time += 300; g.OwnPcTick(); }
        w.Bound = true;
        for (int i = 0; i < 4; i++) { w.Time += 300; g.OwnPcTick(); }
        Assert.That(w.Sent, Is.Empty);

        // entered with the own pc: one send of the event id
        w.Entered = true;
        for (int i = 0; i < 4; i++) { w.Time += 300; g.OwnPcTick(); }
        Assert.That(w.Sent, Has.Count.EqualTo(1));
        Assert.That(w.Sent[0], Is.TypeOf<GuideServerTriggerEvent>());
        Assert.That(((GuideServerTriggerEvent)w.Sent[0]).EventId, Is.EqualTo(251));
    }
}

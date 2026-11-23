using System.Numerics;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The tutorial field's guide chain on the data's own tables (needs MS2_DATA): a fresh record (ReceiveStates with no steps,
/// CGuideManager @0x14138b280) in field 52000065, walked by a player who does what each open event's close conditions ask
/// (steps into the marked cube, opens and closes the named dialogs, equips the tutorial weapon, waits out the timeouts). The
/// chain reaches the tutorial item request and then the server trigger event 251 (the crystal's spawn signal), the two
/// sends the server waits on in the recordings.
/// </summary>
public class GuideChainWalkTests
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

    sealed class OpenDialog(string name) : GuideDialog
    {
        public override string TypeName => name;
        public override bool Hidden => false;
    }

    sealed class TutorialWorld(int job) : GuideWorld
    {
        readonly Pane pane = new();
        public uint Time = 1000;
        public Vector3 Position = new(-675, 525, 625);
        public readonly Dictionary<string, OpenDialog> Open = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Worn = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<Ms2.Net.Wire.IOutPacket> Sent = new();
        public override uint Now => Time;
        public override IGuideEventPane Pane => pane;
        public override int SceneId => 0x620000c;
        public override bool HasPlayer => true;
        public override Vector3 PlayerPosition => Position;
        public override int FieldId => 52000065;
        public override int JobGroup => job;
        public override GuideDialog FindDialog(string name) => GuideDialogLookup.Resolve(name, n => Open.GetValueOrDefault(n));
        public override GuideEquipSlot? Equip(string slotName, int slotType) =>
            slotType == 0 && Worn.TryGetValue(slotName, out int id) ? new GuideEquipSlot(id, true) : null;
        public override void Send(Ms2.Net.Wire.IOutPacket packet) => Sent.Add(packet);
    }

    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(d);
    }

    /// <summary>What the player does for one close condition: false when nothing the player does meets it (a job, a
    /// cleared event, a server trigger, a negation), true when it was done or only time meets it.</summary>
    static bool Satisfy(TutorialWorld w, GuideManager g, GuideCondition c)
    {
        if (c.Not) return false;
        switch (c)
        {
            case PositionCondition p when p.Cubes.Count > 0:
                w.Position = p.Cubes[0].Pos;
                return true;
            case DialogCondition d:
                if (d.IsOpen == 1) w.Open[d.DialogName] = new OpenDialog(d.DialogName);
                else w.Open.Remove(d.DialogName);
                g.QueueOccurrence(GuideOccurrence.Dialog, 0);
                return true;
            case EquipItemCondition e when e.ItemId != 0 && e.SlotType == 0:
                w.Worn[e.SlotName] = e.ItemId;
                return true;
            case JobCodeCondition j:
                return j.Value == w.JobGroup;
            case AnyOneCondition any:
                foreach (var child in any.Children)
                    if (Satisfy(w, g, child)) return true;
                return false;
            case AllOfCondition all:
                foreach (var child in all.Children)
                    if (!Satisfy(w, g, child)) return false;
                return true;
            case TimeoutCondition:
                return true;
            default:
                return false;
        }
    }

    // the job groups of the five weapon events' close conditions: the one-hand pick (210), knight (220), priest (230),
    // thief (240), and a group none of 220-240 names
    [TestCase(10)]
    [TestCase(40)]
    [TestCase(70)]
    [TestCase(30)]
    [TestCase(50)]
    public void FreshRecordWalksToTheServerTriggerEvent(int job)
    {
        var w = new TutorialWorld(job);
        var g = new GuideManager(w);
        g.Load(GuideTable.Load(Packs()));
        g.ReceiveStates(new GuideRecordIn(Array.Empty<(int, int)>(), 0));
        g.QueueOccurrence(GuideOccurrence.FieldId, 52000065);

        var opened = new List<int>();
        int tutorialItemAt = -1, triggerAt = -1;
        for (int step = 0; step < 4000 && triggerAt < 0; step++)
        {
            w.Time += 300;
            g.OwnPcTick();
            foreach (var e in g.Entities)
            {
                if (e.State != 2) continue;
                if (!opened.Contains(e.Id)) opened.Add(e.Id);
                foreach (var c in e.CloseConditions) Satisfy(w, g, c);
            }
            for (int i = 0; i < w.Sent.Count; i++)
            {
                if (w.Sent[i] is RequestTutorialItem && tutorialItemAt < 0) tutorialItemAt = step;
                if (w.Sent[i] is GuideServerTriggerEvent { EventId: 251 }) triggerAt = step;
            }
        }
        TestContext.Out.WriteLine($"job {job}: opened {string.Join(" ", opened)}; item at {tutorialItemAt}, 251 at {triggerAt}");
        Assert.That(tutorialItemAt, Is.GreaterThanOrEqualTo(0), "the tutorial item request was sent");
        Assert.That(triggerAt, Is.GreaterThan(tutorialItemAt), "the server trigger event 251 follows it");
        Assert.That(w.Sent.OfType<GuideServerTriggerEvent>().Count(), Is.EqualTo(1), "251 is sent once");
    }
}

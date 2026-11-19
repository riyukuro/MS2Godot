using Ms2.Net.Packets;
using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The guide's listeners and the checks the controllers make through <see cref="GuideManager.Current"/>
/// (guideentity_1b.cpp listeners, UIDialog::vfunc_73, Unk_Inventory::RequestEquipItem / RequestUnequipItem).</summary>
[NonParallelizable]
public class GuideHookTests
{
    sealed class World : GuideWorld
    {
        public int Scene;
        public bool CutScene;
        public override int SceneId => Scene;
        public override bool CutScenePlaying => CutScene;
        public override int IconCodeOf(int itemId) => itemId == 20000001 ? 7 : 0;
    }

    static GuideManager Make(World w)
    {
        var g = new GuideManager(w);
        g.Load(new GuideTable());
        return g;
    }

    [TearDown]
    public void ClearCurrent() => GuideManager.Current = null;

    [Test]
    public void JobChangeRaisesOnlyInTheGameScene()
    {
        var w = new World();
        var g = Make(w);
        g.OnJobChanged(2);
        Assert.That(g.PendingOccurrences, Is.Empty);
        Assert.That(g.CheckTrigger(GuideTrigger.Job, null, null), Is.False);

        w.Scene = 0x620000c;
        g.OnJobChanged(1);                                  // sub job 1: the occurrence, no trigger
        Assert.That(g.PendingOccurrences, Is.EqualTo(new[] { (int)GuideOccurrence.JobCode }));
        Assert.That(g.CheckTrigger(GuideTrigger.Job, null, null), Is.False);
        g.OnJobChanged(2);
        Assert.That(g.CheckTrigger(GuideTrigger.Job, 0, 0), Is.True);
    }

    [Test]
    public void ItemAddRaisesItsTriggersAndOccurrences()
    {
        var w = new World();
        var g = Make(w);
        g.OnItemAdded(20000001, valid: false);
        Assert.That(g.PendingOccurrences, Is.Empty);
        Assert.That(g.CheckTrigger(GuideTrigger.AddItem, null, null), Is.False);

        g.OnItemAdded(20000001, valid: true);
        Assert.That(g.CheckTrigger(GuideTrigger.AddItem, 20000001, null), Is.True);
        Assert.That(g.CheckTrigger(GuideTrigger.AddItemByIconType, 7, null), Is.True);
        Assert.That(g.PendingOccurrences, Is.EquivalentTo(new[] { (int)GuideOccurrence.DesignItem, (int)GuideOccurrence.CountItem }));

        g.ProcessOccurrences();
        Assert.That(g.PendingOccurrences, Is.Empty);
        g.OnItemChanged(true);
        Assert.That(g.PendingOccurrences, Is.EqualTo(new[] { (int)GuideOccurrence.CountItem }));
    }

    [Test]
    public void NoTriggerWhileACutScenePlays()
    {
        var w = new World { CutScene = true };
        var g = Make(w);
        g.OnBuyHome();
        g.OnUseSkill(10200001);
        Assert.That(g.CheckTrigger(GuideTrigger.BuyHome, null, null), Is.False);
        Assert.That(g.CheckTrigger(GuideTrigger.UseSkill, null, null), Is.False);
        w.CutScene = false;
        g.OnUseSkill(10200001);
        Assert.That(g.CheckTrigger(GuideTrigger.UseSkill, 10200001, null), Is.True);
    }

    [Test]
    public void DialogOpenIsRefusedWhileTheGuideHoldsIt()
    {
        Assert.That(GuideManager.RefusesDialogOpen("InventoryDialog"), Is.False);    // no guide
        var g = Make(new World());
        GuideManager.Current = g;
        Assert.That(GuideManager.RefusesDialogOpen("InventoryDialog"), Is.False);
        g.AddDialogLimit("InventoryDialog");
        Assert.That(GuideManager.RefusesDialogOpen("InventoryDialog"), Is.True);
        Assert.That(GuideManager.RefusesDialogOpen("WorldMapDialog"), Is.False);
        g.RemoveDialogLimit("InventoryDialog");
        Assert.That(GuideManager.RefusesDialogOpen("InventoryDialog"), Is.False);
    }

    [Test]
    public void ASummonPortalRaisesOnlyWhenTheFieldHoldsIt()
    {
        // OnQuest mode 0x12 (questmanager_2.cpp:1125-1141): the trigger is raised only after the target portal is
        // found in the field (WorldService::FUN_141bc2800, :1129-1130) — a packet whose target the field does not
        // hold raises nothing.
        var w = new GuideTests.World();
        var g = new GuideManager(w);
        g.Load(new GuideTable());
        var book = new QuestBook();
        GuideQuests.OnQuest(g, new QuestMessage.SummonPortal(91000010, 50001, 60000), book, knownBefore: false, targetPortal: id => id == 50001);
        Assert.That(g.CheckTrigger(GuideTrigger.QuestSummonPortal, 0, null), Is.True, "the field holds the target");
        w.Time += 300; g.Tick();      // the triggers last one update
        GuideQuests.OnQuest(g, new QuestMessage.SummonPortal(91000010, 50002, 60000), book, knownBefore: false, targetPortal: id => id == 50001);
        Assert.That(g.CheckTrigger(GuideTrigger.QuestSummonPortal, null, null), Is.False, "a target the field does not hold raises nothing");
    }

    [Test]
    public void AnExpiredPacketQueuesOnceHoweverManyIdsItNames()
    {
        // OnQuest modes 6 and 7 (questmanager_2.cpp:1058-1074): RemoveQuest runs per known id and each call queues
        // occurrences 7 and 8 (:865-866), which QueueOccurrence collapses into its sets when the sub value is 0
        // (std::set m_setPlain, cguidemanager.h:112, guideentity_1b.cpp:793-798) — so one raise per kind is every
        // call's, however many ids the packet holds.
        var w = new GuideTests.World();
        var g = new GuideManager(w);
        g.Load(new GuideTable());
        var book = new QuestBook();
        book.Apply(new QuestMessage.Start(11, 0, false, Array.Empty<long>()));
        book.Apply(new QuestMessage.Start(12, 0, false, Array.Empty<long>()));
        Assert.That(GuideQuests.KnownBefore(book, new QuestMessage.Expired(new[] { 11, 12, 13 })), Is.True, "a known id among them");
        Assert.That(GuideQuests.KnownBefore(book, new QuestMessage.Abandon(99)), Is.False, "an unknown id alone is no quest of the book");
        GuideQuests.OnQuest(g, new QuestMessage.Expired(new[] { 11, 12 }), book, knownBefore: true, targetPortal: _ => false);
        GuideQuests.OnQuest(g, new QuestMessage.Abandon(11), book, knownBefore: true, targetPortal: _ => false);
        Assert.That(g.PendingOccurrences,
            Is.EqualTo(new[] { (int)GuideOccurrence.QuestProgress, (int)GuideOccurrence.QuestComplete }), "the calls collapse into one per kind");
        // the collapse itself: the same sub-0 kind raised twice is one pending kind
        g.QueueOccurrence(GuideOccurrence.QuestProgress, 0);
        Assert.That(g.PendingOccurrences.Count(id => id == (int)GuideOccurrence.QuestProgress), Is.EqualTo(1));
        // a packet no quest of the book backs raises nothing (RemoveQuest's early return, :863-864)
        GuideQuests.OnQuest(g, new QuestMessage.Abandon(99), book, knownBefore: false, targetPortal: _ => false);
        Assert.That(g.PendingOccurrences, Has.Count.EqualTo(2));
    }
}

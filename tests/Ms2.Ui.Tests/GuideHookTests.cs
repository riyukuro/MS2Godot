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
}

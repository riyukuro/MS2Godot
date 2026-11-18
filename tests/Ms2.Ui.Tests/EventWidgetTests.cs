using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The event widgets' C++ side (UIEventWidgetDialog, uileveluppackagewidgetdialog.cpp): when each opens at field entry
/// (vfunc_84) and the data it sends (vfunc_85). The level-up package cases read the KR data (need MS2_DATA).
/// </summary>
public class EventWidgetTests
{
    static PackSet packs;
    static QuestTable quests;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
        }
        return packs;
    }

    static QuestTable Quests() => quests ??= QuestTable.Load(Packs(), new[] { "en" }, "KR", f => LobbyFeatures.IsEnabled(Packs(), f));

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };

    sealed class Rewarded : IQuestState
    {
        readonly Dictionary<int, QuestRecord> map = new();
        public Rewarded(params int[] rewarded) { foreach (var id in rewarded) map[id] = new QuestRecord(id, 2, 0, 0, 0, true, false); }
        public IReadOnlyList<QuestRecord> Records => map.Values.ToList();
        public QuestRecord Get(int questId) => map.TryGetValue(questId, out var r) ? r : null;
        public bool CanStart(int questId) => false;
        public int PlayerLevel => 0;
        public long CharacterId => 0;
        public bool NpcFieldOpen(int npcId, int fieldId) => false;
    }

    static LevelUpPackageWidgetController LevelUp(UiModel model, int level, int jobGroup, params int[] rewarded)
    {
        model.HasLocalPlayer = true;
        model.Player.Level = level;
        model.Player.JobGroup = jobGroup;
        return new LevelUpPackageWidgetController(model) { Table = Quests(), State = new Rewarded(rewarded) };
    }

    [Test]
    public void BurningWidgetStaysClosedWithNoEventContexts()
    {
        var model = new UiModel(null) { HasLocalPlayer = true };
        var w = new BurningEventWidgetController(model);
        Assert.That(w.ShouldOpen(), Is.False);
        Assert.That(w.Data(), Is.Null);
        Assert.That((w.Movie, w.TypeName), Is.EqualTo(("uieventwidgetdialog", "BurningEventWidget")));
    }

    [Test]
    public void LevelUpWidgetStaysClosedWithoutALocalPlayer()
    {
        var model = NewModel();
        var w = LevelUp(model, 1, 10);
        model.HasLocalPlayer = false;
        Assert.That(w.ShouldOpen(), Is.False);
        Assert.That(w.Data(), Is.Null);
        Assert.That(model.ItemSlots, Is.Empty);
    }

    [Test]
    public void LevelUpQuestIsTheFirstLevelNotReachedOrNotRewarded()
    {
        // KR: the level guide quests 70000001 (level 10), 70000002 (15), 70000003 (20) ...; 70000015 (level 5) is a
        // QuestCN quest, which the KR features leave out
        var model = NewModel();
        var w = LevelUp(model, 1, 10);
        Assert.That((w.FindQuest(out int low, out int high), low, high), Is.EqualTo((70000001, 0, 10)));
        // reached and not rewarded: still the first
        w = LevelUp(model, 17, 10);
        Assert.That((w.FindQuest(out low, out high), low, high), Is.EqualTo((70000001, 0, 10)));
        // the rewarded ones are passed over up to the first not reached
        w = LevelUp(model, 17, 10, 70000001, 70000002);
        Assert.That((w.FindQuest(out low, out high), low, high), Is.EqualTo((70000003, 15, 20)));
        Assert.That(w.ShouldOpen(), Is.True);
    }

    [Test]
    public void LevelUpWidgetClosesWhenEveryPackageIsRewarded()
    {
        var model = NewModel();
        var all = Quests().CollectType3QuestsByGuideType(1).Select(q => q.Id).ToArray();
        Assert.That(all.Length, Is.EqualTo(23));       // 24 in QuestData.xml, less the QuestCN one
        var w = LevelUp(model, 99, 10, all);
        Assert.That(w.ShouldOpen(), Is.False);
        Assert.That(w.Data(), Is.Null);
    }

    [Test]
    public void LevelUpDataIsTheJobsRewardAndTheLevelProgress()
    {
        var model = NewModel();
        var w = LevelUp(model, 12, 10, 70000001);
        var d = w.Data();
        Assert.That(d, Is.Not.Null);
        Assert.That(w.QuestId, Is.EqualTo(70000002));
        Assert.That(d["title"], Is.EqualTo("$s_levelup_package_widget"));
        Assert.That((d["max"], d["current"], d["text"]), Is.EqualTo((5, 2, "12/15")));
        // 70000002's last row with an item a knight (10) is recommended: its KR row, 12200297 rank 4 count 1
        var slot = model.ItemSlots[(string)d["binding"]];
        Assert.That(d["binding"], Is.EqualTo("LevelUpPackageWidget_1"));
        Assert.That((slot.Item.ItemId, slot.Item.Count), Is.EqualTo((12200297, 1u)));
        // a second build rebinds: the binder is cleared first
        d = w.Data();
        Assert.That(d["binding"], Is.EqualTo("LevelUpPackageWidget_2"));
        Assert.That(model.ItemSlots.Keys, Is.EqualTo(new[] { "LevelUpPackageWidget_2" }));
    }

    [Test]
    public void LevelUpRewardIsTheRowForThePlayersJobGroup()
    {
        // 70000001's KR rows: one per job group; 13200167 (recommended for 1 and 10, rank 4, count 1) is a knight's and a
        // beginner's, 13100171 (70, count 2, clamped to its slot maximum 1) an assassin's
        foreach (var (job, item, count) in new[] { (10, 13200167, 1u), (1, 13200167, 1u), (70, 13100171, 1u) })
        {
            var model = NewModel();
            var d = LevelUp(model, 3, job).Data();
            var slot = model.ItemSlots[(string)d["binding"]];
            Assert.That((slot.Item.ItemId, slot.Item.Count), Is.EqualTo((item, count)), $"job group {job}");
            Assert.That((d["max"], d["current"], d["text"]), Is.EqualTo((10, 3, "3/10")));
        }
    }
}

using Ms2.Formats.World;
using Ms2.Quest;
using NUnit.Framework;
using QuestRecord = Ms2.Net.Packets.QuestRecord;
using Status = Ms2.Formats.World.QuestMaskTable.QuestStatus;

namespace Ms2.Ui.Tests;

/// <summary>
/// An interact object whose quest mask runs from its quest taken to the same quest taken (the main quest's 10001229 for
/// 50001670, "50001670,50001670" states "1,1") is usable while the quest is taken and not yet complete, and closes at the
/// complete stage (FUN_140764480 mode 1 gives 1 for complete). Its quest's condition has no "value", so the client's goal is
/// the constructor's 1 (conditionutil.cpp:287); with a goal of 0 the quest read complete as soon as it was taken, the mask
/// was closed, and LiveInteractObjects.CheckQuestMask refused the action key (nothing sent, the complete npc stayed on its
/// in-progress page).
/// </summary>
[TestFixture]
public class InteractQuestMaskGoalTests
{
    static readonly Dictionary<string, (QuestTable Quests, Dictionary<int, InteractObjectRow> Objects)> loaded = new();

    static (QuestTable Quests, Dictionary<int, InteractObjectRow> Objects) Load(string env, string locale)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        if (loaded.TryGetValue(env, out var d)) return d;
        var packs = TestData.Packs(dir);
        var strings = new GameStrings(packs, "en");
        var rows = new Dictionary<int, InteractObjectRow>();
        foreach (var entry in new[] { "table/interactobject.xml", "table/interactobject_mastery.xml" })
            if (packs.Exists("Xml", entry)) InteractObjectTable.Parse(packs.Read("Xml", entry), rows);
        return loaded[env] = (QuestTable.Load(packs, strings.Languages, locale, _ => true), rows);
    }

    static QuestRecord Taken(QuestData d, long count) => new() { Id = d.Id, State = 1, Progress = d.Conditions.Select(_ => count).ToArray() };

    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void TheMainQuestObjectIsUsableWhileItsQuestIsTaken(string env, string locale)
    {
        var (quests, objects) = Load(env, locale);
        var row = objects.GetValueOrDefault(10001229);
        var d = quests.Quest(50001670);
        if (row == null || d == null) Assert.Ignore($"{env}: no interact 10001229 or quest 50001670");
        Assert.That(d.Conditions.Select(c => (c.Type, c.Goal)), Is.EqualTo(new[] { (d.Conditions[0].Type, 1L) }), "one condition, goal 1");
        QuestRecord rec = null;
        bool Usable() => QuestMaskTable.Check(row.MaskUse, id => id == d.Id ? QuestMaskStatus.Of(rec, quests.Quest) : Status.Absent);
        Assert.That(Usable(), Is.False, "not taken");
        rec = Taken(d, 0);
        Assert.That((QuestMaskStatus.Of(rec, quests.Quest), Usable()), Is.EqualTo((Status.Active, true)), "taken, the object not used yet");
        rec = Taken(d, 1);
        Assert.That((QuestMaskStatus.Of(rec, quests.Quest), Usable()), Is.EqualTo((Status.Completed, false)), "used: complete, the mask closes");
    }

    /// <summary>
    /// Every interact row of the table with a range from one quest taken to the same quest taken: with only that quest taken
    /// and every count 0, the row is usable: the object is the step that moves the quest, so the quest is not complete yet.
    /// </summary>
    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void EveryTakenToTakenObjectIsUsableWhileItsQuestIsShort(string env, string locale)
    {
        var (quests, objects) = Load(env, locale);
        var fails = new List<string>();
        int rows = 0;
        foreach (var (id, row) in objects)
        {
            if (!row.MaskUse.IsSatisfied || row.MaskUse.Enable == 0) continue;
            foreach (var r in row.MaskUse.Ranges)
            {
                if (r.StartQuest != r.EndQuest || r.StartState != 1 || r.EndState != 1) continue;
                if (quests.Quest(r.StartQuest) is not { } d || d.Conditions.Count == 0) continue;
                rows++;
                var rec = Taken(d, 0);
                if (!QuestMaskTable.Check(row.MaskUse, q => q == d.Id ? QuestMaskStatus.Of(rec, quests.Quest) : Status.Absent))
                    fails.Add($"{id}: quest {d.Id}");
                break;
            }
        }
        TestContext.Out.WriteLine($"{env}: {rows} interact rows with a taken-to-taken range, {fails.Count} closed while the quest is short");
        Assert.That(rows, Is.GreaterThan(0));
        Assert.That(fails, Is.Empty);
    }
}

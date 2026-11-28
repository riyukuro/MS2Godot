using Ms2.Formats.World;
using Ms2.Quest;
using NUnit.Framework;
using QuestRecord = Ms2.Net.Packets.QuestRecord;

namespace Ms2.Ui.Tests;

/// <summary>
/// The quest mask of live npcs (CNpc::UpdateQuestVisibility @0x141094e60): an npc the field's questmasknpc table has a mask
/// for is hidden (hide reason 5) while the mask fails, so the talk key and the click pass it by, and shown when the quest
/// standing moves into its range. Before the port every live npc stayed shown and the talk reached the server, which drops it
/// ("quest mask hides it").
/// </summary>
[TestFixture]
public class NpcQuestVisibilityTests
{
    [Test]
    public void HideReasonsShowTheActorOnlyWhenNoneIsLeft()
    {
        int reasons = 0;
        Assert.That(NpcQuestVisibility.SetHideReason(ref reasons, NpcQuestVisibility.HideReasonQuest, true), Is.True);
        Assert.That(NpcQuestVisibility.SetHideReason(ref reasons, NpcQuestVisibility.HideReasonQuest, true), Is.False, "the bit already set: nothing to apply");
        Assert.That(NpcQuestVisibility.SetHideReason(ref reasons, 10, true), Is.True);
        Assert.That(NpcQuestVisibility.SetHideReason(ref reasons, NpcQuestVisibility.HideReasonQuest, false), Is.True);
        Assert.That(reasons, Is.Not.Zero, "another reason keeps the actor hidden");
        Assert.That(NpcQuestVisibility.SetHideReason(ref reasons, 10, false), Is.True);
        Assert.That(reasons, Is.Zero);
        Assert.That(NpcQuestVisibility.SetHideReason(ref reasons, 10, false), Is.False);
    }

    [Test]
    public void NoTableShowsEveryNpc()
    {
        var v = new NpcQuestVisibility(null, 52010027, null);
        Assert.That((v.HasMask(11003469), v.Shows(11003469)), Is.EqualTo((false, true)));
    }

    static readonly Dictionary<string, (QuestTable Quests, QuestMaskTable Masks)> loaded = new();

    static (QuestTable Quests, QuestMaskTable Masks) Load(string env, string locale)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} is not set");
        if (loaded.TryGetValue(env, out var d)) return d;
        var packs = TestData.Packs(dir);
        var strings = new GameStrings(packs, "en");
        return loaded[env] = (QuestTable.Load(packs, strings.Languages, locale, _ => true), new QuestMaskTable(packs));
    }

    /// <summary>
    /// The epic's zealot leader (11003469 on 52010027, enableInMask 0, a range 10003099 taken .. 10003101 taken): hidden while
    /// 10003101 is taken with its kill short of the goal, shown once every goal is met (the complete stage closes the range)
    /// and after the reward; an npc of the field without a table row is never masked.
    /// </summary>
    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void TheZealotLeaderShowsOnlyOnceItsQuestGoalsAreMet(string env, string locale)
    {
        var (quests, masks) = Load(env, locale);
        var mask = masks.Npc(52010027, 11003469);
        if (mask == null) Assert.Ignore($"{env}: no npc mask row for 11003469 on 52010027");
        var d = quests.Quest(10003101);
        Assume.That(d, Is.Not.Null, "quest 10003101 in the data");
        Assume.That(d.Conditions, Is.Not.Empty);
        var recs = new Dictionary<int, QuestRecord>
        {
            [10003099] = new() { Id = 10003099, State = 2 },
            [10003100] = new() { Id = 10003100, State = 2 },
        };
        QuestMaskTable.QuestStatus S(int id) => QuestMaskStatus.Of(recs.GetValueOrDefault(id), quests.Quest);
        var v = new NpcQuestVisibility(masks, 52010027, S);
        Assert.That(v.HasMask(11003469), Is.True);

        recs[10003101] = new() { Id = 10003101, State = 1, Progress = new long[d.Conditions.Count] };
        Assert.That(v.Shows(11003469), Is.False, "10003101 taken, its goals not met: the mask hides the npc");
        recs[10003101] = new() { Id = 10003101, State = 1, Progress = d.Conditions.Select(c => c.Goal).ToArray() };
        Assert.That(v.Shows(11003469), Is.True, "every goal met: past the range");
        recs[10003101] = new() { Id = 10003101, State = 2 };
        Assert.That(v.Shows(11003469), Is.True, "rewarded");
        Assert.That((v.HasMask(11000529), v.Shows(11000529)), Is.EqualTo((false, true)), "a row of another field (02000208) does not mask here");
    }

    /// <summary>
    /// Every npc mask row of the table: the npc counts as masked only in its own field (HasNpcMaskHere looks up the current
    /// field), and the answer is the table's own check.
    /// </summary>
    [TestCase("MS2_DATA", "KR")]
    [TestCase("MS2_GMS2_DATA", "NA")]
    public void EveryNpcRowIsMaskedInItsOwnFieldOnly(string env, string locale)
    {
        var (_, masks) = Load(env, locale);
        var rows = masks.NpcRows.ToList();
        Assert.That(rows, Is.Not.Empty);
        var fields = rows.Select(r => r.Key.Field).ToHashSet();
        var fails = new List<string>();
        foreach (var (key, mask) in rows)
        {
            var v = new NpcQuestVisibility(masks, key.Field, QuestMaskTable.NoQuests);
            if (!v.HasMask(key.Key)) fails.Add($"{key.Field}/{key.Key}: not masked in its field");
            if (v.Shows(key.Key) != QuestMaskTable.Check(mask, QuestMaskTable.NoQuests)) fails.Add($"{key.Field}/{key.Key}: answer");
            int elsewhere = fields.FirstOrDefault(f => f != key.Field && masks.Npc(f, key.Key) == null);
            if (elsewhere != 0 && new NpcQuestVisibility(masks, elsewhere, QuestMaskTable.NoQuests).HasMask(key.Key))
                fails.Add($"{key.Field}/{key.Key}: masked in {elsewhere}");
        }
        TestContext.Out.WriteLine($"{env}: {rows.Count} npc mask rows over {fields.Count} fields");
        Assert.That(fails, Is.Empty);
    }
}

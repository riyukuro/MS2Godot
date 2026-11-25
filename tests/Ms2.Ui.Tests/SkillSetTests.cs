using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Ui.Skills;
using Ms2Godot.Movement;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The skill set table and the own skill system's set list (CSkillSet / CSkillSetTable, commonfiles/skillset.cpp; CSkillSystem
/// AddSkillSet / RemoveSkillSet, skillslot.cpp:2582-2621), and every riding row's skillSetID of a data set.
/// </summary>
public class SkillSetTests
{
    static SkillSetTable Table(string rows, Func<XElement, bool> available = null) =>
        SkillSetTable.Load(XDocument.Parse("<ms2>" + rows + "</ms2>"), available ?? (_ => true));

    [Test]
    public void TheTableReadsTheListsAndTheDefaults()
    {
        var t = Table("""
            <skillSet id="10" skillIDs="99910002, 99910001" skillLevels="1, 3" />
            <skillSet id="20" version="5" slotLock="0" itemIDs="0,0,20000001,0" />
            <skillSet id="30" feature="Off" skillIDs="1" skillLevels="1" />
            <skillSet id="40" skillIDs="7" skillLevels="2" learnSkillIDs="8" learnSkillLevels="4" />
            <skillSet id="10" skillIDs="5" skillLevels="1" />
            """, e => (string)e.Attribute("feature") != "Off");
        Assert.That(t.Get(30), Is.Null, "an element whose feature is off is no row");
        Assert.That(t.Get(10).SkillIds, Is.EqualTo(new[] { 5 }), "a later row of the same id replaces the first (SetAt)");

        var s20 = t.Get(20);
        Assert.That((s20.Version, s20.SlotLock), Is.EqualTo((5, false)));
        Assert.That(s20.ItemIds, Is.EqualTo(new[] { 0, 0, 20000001, 0 }), "a 0 item keeps its place");
        Assert.That(s20.KeySettingName, Is.EqualTo("SkillSet20_5"));

        var s40 = t.Get(40);
        Assert.That((s40.Version, s40.SlotLock), Is.EqualTo((1, true)), "the ctor's version 1 and slotLock 1");
        Assert.That(s40.HasSkill(7), Is.True);
        Assert.That(s40.HasSkill(8), Is.True, "a learn skill is in the set too");
        Assert.That(s40.HasSkill(7, 2), Is.True);
        Assert.That(s40.HasSkill(7, 3), Is.False);
        Assert.That(s40.HasSkill(9), Is.False);
        Assert.That((s40.GetSkillLevel(7), s40.GetSkillLevel(8), s40.GetSkillLevel(9)), Is.EqualTo((2, 4, 0)));

        var spaced = Table("""<skillSet id="1" skillIDs="99910002, 99910001" skillLevels="1, 1" />""").Get(1);
        Assert.That(spaced.SkillIds, Is.EqualTo(new[] { 99910002, 99910001 }), "atoi skips the space after the comma");
    }

    [Test]
    public void TheCurrentSetIsTheLastAddedAndAnOpenLayoutIsSaved()
    {
        var t = Table("""
            <skillSet id="1" skillIDs="11" skillLevels="1" />
            <skillSet id="2" version="3" slotLock="0" skillIDs="22" skillLevels="1" />
            """);
        var saved = new List<string>();
        var l = new SkillSetList { SaveKeySetting = saved.Add };

        l.Add(0, t);
        Assert.That(l.Current, Is.Null, "0 adds nothing");
        l.Add(1, t);
        Assert.That(l.Current.Id, Is.EqualTo(1));
        l.Add(2, t);
        Assert.That(l.Current.Id, Is.EqualTo(2));
        Assert.That(saved, Is.Empty, "a slotLock set is not saved");
        l.Add(1, t);
        Assert.That(saved, Is.EqualTo(new[] { "SkillSet2_3" }), "the open set's layout is saved before another comes in");
        Assert.That(l.Ids, Is.EqualTo(new[] { 1, 2, 1 }));

        l.Remove(2, t);
        Assert.That(saved, Has.Count.EqualTo(1), "only the current set of the id is saved");
        Assert.That(l.Ids, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(l.Current.Id, Is.EqualTo(1));
        l.Remove(1, t);
        Assert.That(l.Ids, Is.EqualTo(new[] { 1 }), "the first entry of the id goes");
        l.Remove(1, t);
        Assert.That(l.Current, Is.Null);
        l.Add(77, t);
        Assert.That(l.Current, Is.Null, "an id without a row leaves no current set");
        Assert.That(l.Ids, Is.EqualTo(new[] { 77 }));
    }

    [Test]
    public void ASetSkillIsLearntAtTheSetsLevel()
    {
        // JobSystemCli::vfunc_2 (jobsystemcli.cpp:91-95) / vfunc_1 (:63-67): the set's skill is learnt and has the set's level
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore("MS2_DATA is not set");
        var tables = new GameTables(TestData.Packs(data), "en");
        var set = tables.SkillSets.Rows.Values.Select(s => (Set: s, Skill: s.SkillIds.FirstOrDefault(id =>
            tables.Skills.GetSkillData((uint)id) is { } d && JobSystem.IsBookSkillKind(d)))).FirstOrDefault(x => x.Skill != 0);
        if (set.Set == null) Assert.Ignore("no set with a book kind skill");
        var jobs = new JobSystem(tables.Skills);
        uint id = (uint)set.Skill;
        Assert.That(jobs.IsLearned(id), Is.False, "outside the set the book does not have it");
        jobs.SkillSet = () => set.Set;
        Assert.That(jobs.IsLearned(id), Is.True);
        Assert.That(jobs.LevelData(id)?.Level, Is.EqualTo(set.Set.GetSkillLevel(set.Skill)));
    }

    /// <summary>
    /// Every riding row's skillSetID. A set the table has (its element there and its feature on) has level data for each
    /// skill at its level and item data for each item id other than 0, which the quick slots take when the mount is ridden
    /// (MyPC::vfunc_120, CKeyTable::ApplySkillSetToQuickSlots). Some rows name a set the table does not have: AddSkillSet
    /// still lists the id, GetSkillSet gives no row, so there is no current set and ApplySkillSet changes nothing.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRidingSkillSetIsInTheTable(string env)
    {
        var packs = TestData.Packs(RcGap.Data(env));
        var tables = new GameTables(packs, "en");
        var sets = tables.SkillSets;
        var ids = packs.List("Xml").Where(e => e.StartsWith("riding/") && e.Count(c => c == '/') == 1 && e.EndsWith(".xml"))
            .Select(e => int.TryParse(e[7..^4], out var id) ? id : -1).Where(id => id > 0).OrderBy(id => id).ToList();
        var rows = new List<(int Ride, int Set)>();
        var missing = new List<string>();
        var noRow = new List<string>();
        foreach (var id in ids)
        {
            if (PackXml.Parse(packs.Read("Xml", $"riding/{id:D8}.xml")).Root?.Element("riding")?.Element("basic") == null) continue;
            var d = RideData.Load(packs, id);
            if (d.SkillSetId == 0) continue;
            rows.Add((id, d.SkillSetId));
            if (sets.Get(d.SkillSetId) is not { } s)
            {
                noRow.Add($"{id}:{d.SkillSetId}");
                var list = new SkillSetList();
                list.Add(d.SkillSetId, sets);
                if (list.Current != null || list.Ids.Count != 1) missing.Add($"ride {id}: set {d.SkillSetId} without a row became current");
                continue;
            }
            foreach (var skill in s.SkillIds)
                if (tables.Skills.Skills.GetSkillLevelData((uint)skill, s.GetSkillLevel(skill)) == null)
                    missing.Add($"ride {id}: set {s.Id} skill {skill} level {s.GetSkillLevel(skill)}");
            foreach (var item in s.ItemIds)
                if (item != 0 && tables.Items.Get(item) == null) missing.Add($"ride {id}: set {s.Id} item {item}");
        }
        var on = rows.Where(r => sets.Get(r.Set) != null).ToList();
        TestContext.WriteLine($"{env}: {sets.Rows.Count} sets; {rows.Count} riding rows with a skill set " +
            $"({rows.Select(r => r.Set).Distinct().Count()} sets), {on.Count} with a row; without one: {string.Join(" ", noRow)}");
        foreach (var (ride, set) in rows)
            TestContext.WriteLine($"  {ride}: {set}{(sets.Get(set) is { } s ? $" skills {string.Join(",", s.SkillIds)} items {string.Join(",", s.ItemIds)} slotLock {(s.SlotLock ? 1 : 0)}" : " (no row)")}");
        foreach (var m in missing) TestContext.WriteLine("  MISSING " + m);
        Assert.That(rows, Is.Not.Empty);
        Assert.That(on, Is.Not.Empty, "some mount's set has a row");
        Assert.That(on.Count, Is.GreaterThan(noRow.Count), "most do");
        Assert.That(missing, Is.Empty);
    }
}

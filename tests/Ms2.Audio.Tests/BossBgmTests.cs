using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The field's boss music (WorldService m_900 / m_910, worldservice_1.cpp:4876-4930 and :9717-9731) as the boss npcs drive it
/// (CNpc::vfunc_10 :534, vfunc_53 :2354-2359, vfunc_125 :2150, vfunc_14 :613 in unit_14108e1e0_npcactorandvibrate.cpp): the
/// distance band of <see cref="BossBgmTracker.Update"/>, the npc rows' bossSoundDistance / bossSoundEndDistance, and every
/// field's boss cue and every boss npc on MS2_DATA and MS2_GMS2_DATA.
/// </summary>
public class BossBgmTests
{
    // field 2000004: <bgm id="59" param="5" boss="23" bossParam="5"/>
    static readonly FieldBgmSpec Field = new(BgmId: 59, BgmParam: 5, BgmValue: 0f, BossBgmId: 23, BossBgmParam: 5, BossBgmValue: 0f);
    const float Start = 2000f, End = 3000f;

    static SoundLibrary Library(string env = "MS2_DATA")
    {
        var data = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore($"{env} is not set");
        var dir = Path.Combine(data, "Sound");
        if (!File.Exists(Path.Combine(dir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {env}");
        return SoundLibrary.Shared(dir);
    }

    static (MusicController, BossBgmTracker) Playing()
    {
        var mc = new MusicController(Library().Fev);
        Assert.That(mc.PlayFieldBgm(Field), Is.True);
        return (mc, new BossBgmTracker(mc, Field));
    }

    [Test]
    public void TheBossDistanceIsMeasuredFromTheCameraPoint()
    {
        var player = new System.Numerics.Vector3(100, 200, 0);
        var eye = new System.Numerics.Vector3(100, -1300, 900);
        Assert.That(BossBgmTracker.MusicPoint(player, eye), Is.EqualTo(player), "the camera target (the own PC)");
        Assert.That(BossBgmTracker.MusicPoint(null, eye), Is.EqualTo(eye), "no camera target: the camera's eye");
        Assert.That(BossBgmTracker.MusicPoint(player, null), Is.EqualTo(System.Numerics.Vector3.Zero), "no camera: the origin");
    }

    [Test]
    public void ABossWithinItsSoundDistanceStartsTheBossMusic()
    {
        var (mc, t) = Playing();
        t.AddBoss(1);
        Assert.That(t.Update(1, 2500f, Start, End), Is.False, "between the two distances: nothing");
        Assert.That(t.Update(1, 3100f, Start, End), Is.False, "beyond the end distance: nothing to let go");
        Assert.That(mc.CurrentCueId, Is.EqualTo(59));
        Assert.That(t.Update(1, Start, Start, End), Is.True, "at the distance itself (m_unk_c4 <= m_fBossSoundDistance)");
        Assert.That((mc.CurrentCueId, t.IsBossBgmActive), Is.EqualTo((23, true)));
    }

    [Test]
    public void TheBossMusicStaysUntilTheEndDistance()
    {
        var (mc, t) = Playing();
        t.AddBoss(1);
        t.Update(1, 100f, Start, End);
        t.Update(1, 2999f, Start, End);
        Assert.That(t.Tick(), Is.False, "short of the end distance the boss stays in range");
        Assert.That(mc.CurrentCueId, Is.EqualTo(23));
        t.Update(1, End, Start, End);
        Assert.That(t.Tick(), Is.True, "at the end distance (m_unk_c4 >= m_fBossSoundEndDistance) the world update gives the field's music back");
        Assert.That((mc.CurrentCueId, t.IsBossBgmActive), Is.EqualTo((59, false)));
        Assert.That(t.Update(1, 1500f, Start, End), Is.True, "and it starts again when the player comes back");
    }

    [Test]
    public void AnUntrackedNpcNeverStartsIt()
    {
        var (mc, t) = Playing();
        Assert.That(t.Update(7, 0f, Start, End), Is.False, "StartCombatBgm finds no m_900 entry for an npc that is not a tracked boss");
        t.AddBoss(7);
        t.RemoveBoss(7);
        Assert.That(t.Update(7, 0f, Start, End), Is.False, "a dead or removed boss is no longer tracked");
        Assert.That(mc.CurrentCueId, Is.EqualTo(59));
    }

    [Test]
    public void TheDeathOfTheBossInRangeHandsBackOnceNoOtherIsInRange()
    {
        var (mc, t) = Playing();
        t.AddBoss(1); t.AddBoss(2);
        t.Update(1, 500f, Start, End);
        t.Update(2, 2500f, Start, End);
        Assert.That(t.RemoveBoss(1), Is.False, "another boss is still tracked: RemoveCombatBgmTracker keeps the boss music");
        Assert.That(mc.CurrentCueId, Is.EqualTo(23));
        Assert.That(t.Tick(), Is.True, "no tracked boss is in range: the next world update restores the field's music");
        Assert.That(mc.CurrentCueId, Is.EqualTo(59));
        t.Update(2, 500f, Start, End);
        Assert.That(t.RemoveBoss(2), Is.True, "the last boss's death restores it at once");
        Assert.That(mc.CurrentCueId, Is.EqualTo(59));
    }

    [Test]
    public void ASecondBossInRangeDoesNotHoldTheMusic()
    {
        // StartCombatBgm returns at m_910 before it marks the entry, so only the boss that started the music holds it
        var (mc, t) = Playing();
        t.AddBoss(1); t.AddBoss(2);
        t.Update(1, 500f, Start, End);
        t.Update(2, 500f, Start, End);
        t.Update(1, 3500f, Start, End);
        Assert.That(t.Tick(), Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(59));
    }

    [Test]
    public void TheRowsDistancesAndTheirDefaults()
    {
        static NpcRow Row(string attrs) => NpcTable.ParseRow(XElement.Parse($"<npc id=\"1\"><environment class=\"3\" {attrs}/></npc>"), Array.Empty<EffectDummy>());
        var none = Row("");
        Assert.That((none.BossSoundDistance, none.BossSoundEndDistance), Is.EqualTo((2000f, 3000f)), "the CNpcData ctor's defaults (npcdata.cpp:652-653)");
        var set = Row("bossSoundDistance=\"1500\" bossSoundEndDistance=\"4500.5\"");
        Assert.That((set.BossSoundDistance, set.BossSoundEndDistance), Is.EqualTo((1500f, 4500.5f)));
        Assert.That(set.IsBoss, Is.True);
    }

    /// <summary>
    /// Every field whose default environment has a boss cue: the cue plays (PlayMusic finds it and its first segment) on a field's
    /// music, so StartCombatBgm switches; every boss npc row's two distances, the start not above the end.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryFieldsBossCueAndEveryBoss(string env)
    {
        var lib = Library(env);
        var packs = AudioData.Packs(Environment.GetEnvironmentVariable(env));
        var root = FieldData.Document(packs)?.Root;
        Assert.That(root, Is.Not.Null);
        var problems = new List<string>();
        int fields = 0, withBoss = 0;
        var cues = new SortedSet<int>();
        foreach (var fd in root.Elements("fieldData"))
        {
            fields++;
            var spec = FieldBgmSpec.Parse(FieldData.DefaultEnvironment(fd)?.Element("bgm"));
            if (spec.BossBgmId == 0) continue;
            withBoss++; cues.Add(spec.BossBgmId);
            var mc = new MusicController(lib.Fev);
            mc.PlayFieldBgm(spec);
            var t = new BossBgmTracker(mc, spec);
            t.AddBoss(1);
            bool started = t.Update(1, 0f, Start, End);
            var seg = mc.CurrentSegment;
            if (!started || mc.CurrentCueId != spec.BossBgmId) problems.Add($"field {(string)fd.Attribute("id")}: boss cue {spec.BossBgmId} does not play");
            else if (seg == null || lib.ResolveSegment(seg) == null) problems.Add($"field {(string)fd.Attribute("id")}: boss cue {spec.BossBgmId} has no first segment clip");
        }
        var npcs = new NpcTable(packs);
        int bosses = 0, setStart = 0, setEnd = 0, same = 0;
        foreach (var r in npcs.Rows)
        {
            if (!r.IsBoss) continue;
            bosses++;
            if (r.BossSoundDistance != 2000f) setStart++;
            if (r.BossSoundEndDistance != 3000f) setEnd++;
            // equal distances are data (0: only at the boss's own point, 99999: anywhere in the field); the start check comes first
            if (r.BossSoundDistance == r.BossSoundEndDistance) same++;
            if (r.BossSoundDistance > r.BossSoundEndDistance) problems.Add($"npc {r.Id}: bossSoundDistance {r.BossSoundDistance} above bossSoundEndDistance {r.BossSoundEndDistance}");
        }
        TestContext.Out.WriteLine($"{env}: {fields} fields, {withBoss} with a boss cue (cues {string.Join(",", cues)}); {bosses} boss npcs, "
            + $"{setStart} with their own bossSoundDistance, {setEnd} with their own bossSoundEndDistance, {same} with the two equal; {problems.Count} problems");
        foreach (var p in problems.Take(40)) TestContext.Out.WriteLine("  " + p);
        Assert.That(withBoss, Is.GreaterThan(0));
        Assert.That(bosses, Is.GreaterThan(0));
        Assert.That(setStart, Is.GreaterThan(0), "the rows' own distances are read");
        Assert.That(problems, Is.Empty);
    }
}

using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A random part (CRandomEffectParam / CRandomEffect, randomeffectparam.cpp) makes one child per play: prob read as
/// XmlSplit + atoi (:86-87), the children the made params (:88-93), CRandomEffect::vfunc_3 @0x1416f48e0 (:143-178) picking
/// m_nRandomIndex mod count or by Unk_WeightedPicker (Build @0x1416f5250, Pick @0x1416f55b0) seeded with
/// CEffectService::GetTick through Unk_Random(int) @0x140768210 (questmasktable.cpp:400-407).
/// The reference pick below is written out from those lines: a sorted map of running total - 1 to the child, lower_bound
/// on the first number of the Tausworthe generator (FUN_1407684c0, questmasktable.cpp:451-461) mod the total.
/// </summary>
[NonParallelizable]
public class EffectRandomTests
{
    static List<EffectPart> Parts(string xml) => EffectTable.Parse(XDocument.Parse(xml));

    /// <summary>The client's pick over the made children's weights, or -1 when Build fails.</summary>
    static int ReferencePick(IReadOnlyList<int> weights, uint tick)
    {
        int seed = unchecked((int)tick);
        uint a = unchecked((uint)seed); seed = unchecked(seed * 0x343fd + 0x269ec3);
        uint b = unchecked((uint)seed); seed = unchecked(seed * 0x343fd + 0x269ec3);
        uint c = unchecked((uint)seed);
        a |= 0x100000; b |= 0x1000; c |= 0x10;
        a = (a & 0xfffffffe) << 12 ^ (a << 13 ^ a) >> 19;
        b = (b & 0xfffffff8) << 4 ^ (b << 2 ^ b) >> 25;
        c = (c & 0xfffffff0) << 17 ^ (c << 3 ^ c) >> 11;
        uint next = a ^ b ^ c;
        var map = new SortedDictionary<int, int>();
        int total = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            if (weights[i] == 0) continue;
            total += weights[i];
            map[total - 1] = i;
        }
        if (total <= 0) return -1;
        int key = (int)(next % (uint)total);
        foreach (var kv in map) if (kv.Key >= key) return kv.Value;
        return -1;
    }

    [Test]
    public void ProbIsSplitOnCommaAndSpaceWithAtoiPerToken()
    {
        Assert.That(EffectTable.Atoi("12"), Is.EqualTo(12));
        Assert.That(EffectTable.Atoi(" \t7x"), Is.EqualTo(7));
        Assert.That(EffectTable.Atoi("-3"), Is.EqualTo(-3));
        Assert.That(EffectTable.Atoi("+4"), Is.EqualTo(4));
        Assert.That(EffectTable.Atoi("abc"), Is.EqualTo(0));
        Assert.That(EffectTable.Atoi("1.9"), Is.EqualTo(1));
        Assert.That(EffectTable.Atoi(""), Is.EqualTo(0));
        Assert.That(EffectTable.Atoi("99999999999"), Is.EqualTo(int.MaxValue), "MSVC atoi saturates (strtol)");
        Assert.That(EffectTable.Weights("8,1, 1"), Is.EqualTo(new uint[] { 8, 1, 1 }));
        Assert.That(EffectTable.Weights("1,,2 "), Is.EqualTo(new uint[] { 1, 2 }), "Tokenize gives no empty token");
        Assert.That(EffectTable.Weights("a,2"), Is.EqualTo(new uint[] { 0, 2 }));
        Assert.That(EffectTable.Weights(null), Is.Empty);
        Assert.That(EffectTable.Weights(""), Is.Empty);
    }

    const string Xml = """
        <ms2>
          <effect type="nif" filePath="top.nif" duration="1"/>
          <effect type="random" prob="8, 1,1">
            <effect type="nif" filePath="a.nif" duration="1"/>
            <effect type="nif" filePath="b.nif" duration="1"><effect type="nif" filePath="inner.nif"/></effect>
            <effect type="soundEvent" group="g" event="e"/>
          </effect>
          <effect type="random">
            <effect type="random" prob="1,1">
              <effect type="nif" filePath="n1.nif"/>
              <effect type="nif" filePath="n2.nif"/>
            </effect>
          </effect>
        </ms2>
        """;

    [Test]
    public void ARandomPartsChildrenAreItsDirectEffectChildrenInTheFilesOrder()
    {
        var parts = Parts(Xml);
        Assert.That(parts.Select(p => p.FilePath ?? p.Type), Is.EqualTo(new[]
            { "top.nif", "random", "a.nif", "b.nif", "inner.nif", "soundEvent", "random", "random", "n1.nif", "n2.nif" }));
        Assert.That(parts[1].RandomChildren, Is.EqualTo(new[] { 2, 3, 5 }), "inner.nif sits in a nif part, not in the random part");
        Assert.That(parts[1].RandomWeights, Is.EqualTo(new uint[] { 8, 1, 1 }));
        Assert.That(parts[4].RandomParent, Is.EqualTo(-1));
        Assert.That(parts[6].RandomChildren, Is.EqualTo(new[] { 7 }));
        Assert.That(parts[6].RandomWeights, Is.Empty, "no prob: every weight 0");
        Assert.That(parts[7].RandomParent, Is.EqualTo(6));
        Assert.That(parts[7].RandomChildren, Is.EqualTo(new[] { 8, 9 }));
        Assert.That(parts[0].RandomChildren, Is.Null);
        Assert.That(parts[0].RandomParent, Is.EqualTo(-1));
        Assert.That(parts.Count(p => p.TopLevel), Is.EqualTo(3));
    }

    [Test]
    public void TheWeightedPickIsTheClientsOverEveryTick()
    {
        var parts = Parts(Xml);
        var sound = EffectRandom.SoundEventFound;
        EffectRandom.SoundEventFound = null;
        try
        {
            var counts = new int[3];
            for (uint t = 0; t < 30000; t++)
            {
                int want = ReferencePick(new[] { 8, 1, 1 }, t);
                int got = EffectRandom.Pick(parts, parts[1], 0, t);
                Assert.That(got, Is.EqualTo(parts[1].RandomChildren[want]), $"tick {t}");
                counts[want]++;
            }
            Assert.That(counts[0] / 30000.0, Is.EqualTo(0.8).Within(0.02));
            Assert.That(counts[1] / 30000.0, Is.EqualTo(0.1).Within(0.02));
            Assert.That(counts[2] / 30000.0, Is.EqualTo(0.1).Within(0.02));
            foreach (uint t in new uint[] { 0x80000000, 0xffffffff, 0xdeadbeef, 12345678 })
                Assert.That(EffectRandom.Pick(parts, parts[1], 0, t), Is.EqualTo(parts[1].RandomChildren[ReferencePick(new[] { 8, 1, 1 }, t)]));
        }
        finally { EffectRandom.SoundEventFound = sound; }
    }

    [Test]
    public void AWeight0IsNeverPickedAWeightPastTheListIs0AndAll0MakesNothing()
    {
        var parts = Parts("""
            <ms2>
              <effect type="random" prob="0, 3"><effect type="nif" filePath="a.nif"/><effect type="nif" filePath="b.nif"/><effect type="nif" filePath="c.nif"/></effect>
              <effect type="random" prob="0"><effect type="nif" filePath="d.nif"/></effect>
              <effect type="random"><effect type="nif" filePath="e.nif"/></effect>
            </ms2>
            """);
        for (uint t = 0; t < 2000; t++)
            Assert.That(EffectRandom.Pick(parts, parts[0], 0, t), Is.EqualTo(2), "only b.nif has a weight");
        Assert.That(EffectRandom.Pick(parts, parts[4], 0, 7), Is.EqualTo(-1));
        Assert.That(EffectRandom.Pick(parts, parts[6], 0, 7), Is.EqualTo(-1));
        var list = new List<EffectPart>();
        Assert.That(EffectRandom.PlayList(parts, 0, 7, list).Select(p => p.FilePath), Is.EqualTo(new[] { "b.nif" }));
    }

    [Test]
    public void ARandomIndexTakesTheChildAtItModTheCount()
    {
        var parts = Parts(Xml);
        for (uint r = 1; r < 20; r++)
            Assert.That(EffectRandom.Pick(parts, parts[1], r, 5), Is.EqualTo(parts[1].RandomChildren[r % 3]));
        Assert.That(EffectRandom.Pick(parts, parts[6], 9, 5), Is.EqualTo(7), "a random part with no prob still takes its index");
        var list = new List<EffectPart>();
        Assert.That(EffectRandom.PlayList(parts, 9, 5, list).Select(p => p.FilePath ?? p.Type),
            Is.EqualTo(new[] { "top.nif", "a.nif", "n2.nif" }), "9 % 3 = 0, then 9 % 1 = 0 and 9 % 2 = 1 in the nested part");
    }

    [Test]
    public void AChildTheClientDoesNotMakeIsNotCountedAndShiftsTheWeights()
    {
        var parts = Parts("""
            <ms2>
              <effect type="random" prob="5, 1, 3">
                <effect type="light" duration="0"/>
                <effect type="nosuchtype"/>
                <effect type="soundEvent" group="missing" event="e"/>
                <effect type="nif" filePath="a.nif"/>
                <effect type="light" loop="true"/>
                <effect type="status" duration="0.5"/>
              </effect>
            </ms2>
            """);
        var sound = EffectRandom.SoundEventFound;
        EffectRandom.SoundEventFound = p => p.SoundGroup != "missing";
        try
        {
            // made: a.nif (5), the looping light (1), the status with a duration (3)
            int[] made = { 4, 5, 6 };
            for (uint t = 0; t < 5000; t++)
                Assert.That(EffectRandom.Pick(parts, parts[0], 0, t), Is.EqualTo(made[ReferencePick(new[] { 5, 1, 3 }, t)]), $"tick {t}");
            for (uint r = 1; r < 10; r++)
                Assert.That(EffectRandom.Pick(parts, parts[0], r, 0), Is.EqualTo(made[r % 3]));
            EffectRandom.SoundEventFound = null;
            Assert.That(EffectRandom.ParamMade(parts, parts[3]), Is.True, "no FMOD lookup: the part is taken as made");
            Assert.That(EffectRandom.ParamMade(parts, parts[1]), Is.False, "light, no loop, duration 0 (CEffectParam::vfunc_2)");
            Assert.That(EffectRandom.ParamMade(parts, parts[2]), Is.False, "an unknown type has no param");
        }
        finally { EffectRandom.SoundEventFound = sound; }
    }

    [Test]
    public void ARandomPartWithNoMadeChildIsNotMade()
    {
        var parts = Parts("""
            <ms2>
              <effect type="random" prob="1"><effect type="random" prob="1"><effect type="fog"/></effect></effect>
              <effect type="random" prob="1"/>
            </ms2>
            """);
        Assert.That(EffectRandom.ParamMade(parts, parts[1]), Is.False);
        Assert.That(EffectRandom.ParamMade(parts, parts[0]), Is.False);
        Assert.That(EffectRandom.ParamMade(parts, parts[3]), Is.False);
        Assert.That(EffectRandom.PlayList(parts, 0, 1, new List<EffectPart>()), Is.Empty);
    }

    [Test]
    public void APlayHasEveryTopLevelPartAndOneChildPerRandomPart()
    {
        var parts = Parts(Xml);
        var list = new List<EffectPart>();
        for (uint t = 0; t < 3000; t++)
        {
            EffectRandom.PlayList(parts, 0, t, list);
            Assert.That(list.Count, Is.EqualTo(2), "top.nif and the first random part's child; the second has no prob");
            Assert.That(list[0].FilePath, Is.EqualTo("top.nif"));
            Assert.That(list[1].RandomParent, Is.EqualTo(1));
            Assert.That(list.Any(p => p.FilePath == "inner.nif"), Is.False);
        }
    }

    [Test]
    public void ARandomPartItsOwnersFiltersRefusePicksNothing()
    {
        var parts = Parts("""
            <ms2>
              <effect type="nif" filePath="all.nif"/>
              <effect type="random" prob="1" onlyCasterMyPC="true"><effect type="nif" filePath="mine.nif"/></effect>
              <effect type="random" prob="1" gender="1"><effect type="nif" filePath="female.nif"/></effect>
            </ms2>
            """);
        var list = new List<EffectPart>();
        Assert.That(EffectRandom.PlayList(parts, 0, 1, list, new EffectOwner(3, 0, 0, false)).Select(p => p.FilePath), Is.EqualTo(new[] { "all.nif" }));
        Assert.That(EffectRandom.PlayList(parts, 0, 1, list, new EffectOwner(EffectOwner.KindMyPc, 1, 0, false)).Select(p => p.FilePath),
            Is.EqualTo(new[] { "all.nif", "mine.nif", "female.nif" }));
        Assert.That(EffectRandom.PlayList(parts, 0, 1, list).Count, Is.EqualTo(3), "no owner: no filter");
    }

    [Test]
    public void ARandomPartsChildIsTestedOnTheRandomRowUnlessOffSkip()
    {
        var table = EffectSkipTable.Parse("""
            <ms2><option2><random><element caster="myPC" viewDistance="0"/><element caster="PC" viewDistance="1000"/></random></option2></ms2>
            """, 1);
        var parts = Parts("""
            <ms2>
              <effect type="random" prob="1"><effect type="nif" filePath="a.nif"/></effect>
              <effect type="random" prob="1" offSkip="true"><effect type="nif" filePath="b.nif"/></effect>
            </ms2>
            """);
        var eye = new Vector3(0, 0, 0);
        Assert.That(table.RandomOutOfView(parts[0], 3, new Vector3(1500, 0, 0), eye), Is.True);
        Assert.That(table.RandomOutOfView(parts[0], 3, new Vector3(500, 0, 0), eye), Is.False);
        Assert.That(table.RandomOutOfView(parts[0], 0, new Vector3(1500, 0, 0), eye), Is.False, "kind 0");
        Assert.That(table.RandomOutOfView(parts[0], 3, new Vector3(1500, 0, 0), null), Is.False, "no camera actor");
        Assert.That(table.RandomOutOfView(parts[2], 3, new Vector3(1500, 0, 0), eye), Is.False, "offSkip");
    }

    [Test]
    public void TheSeededGeneratorIsUnkRandomOfAnInt()
    {
        foreach (int seed in new[] { 0, 1, -1, 0x7fffffff, int.MinValue, 123456789 })
        {
            int s = seed;
            uint a = unchecked((uint)s); s = unchecked(s * 0x343fd + 0x269ec3);
            uint b = unchecked((uint)s); s = unchecked(s * 0x343fd + 0x269ec3);
            uint c = unchecked((uint)s);
            var want = new DropRandom(a, b, c);
            var got = DropRandom.Seeded(seed);
            for (int i = 0; i < 8; i++) Assert.That(got.Next(), Is.EqualTo(want.Next()));
        }
    }

    // ---- the play sites: every caller that plays an effect xml's parts goes through the play list, and a cast sets the tick

    [TestCase("net/CombatLayer.cs", "void Play(string xmlPath,", "EffectRandom.PlayList(")]
    [TestCase("scripts/world/RegionSkills.cs", "List<Node3D> Play(string xml, Xform at, bool keep, int handle,", "EffectRandom.PlayList(")]
    [TestCase("scripts/world/GroundEffects.cs", "public void Play(string effectFile, Vector3 at, float yawRadians)", "EffectRandom.PlayList(")]
    [TestCase("scripts/world/TaxiStations.cs", "void Play(string xml, Vector3 at, bool keep)", "EffectRandom.PlayList(")]
    [TestCase("net/EquipApplier.cs", "void IdleEffectsOf(Node3D model, Skeleton3D skel, EquipItem item,", "EffectRandom.PlayList(")]
    [TestCase("net/RemoteLayer.Casts.cs", "void OnSkillUse(SkillUse u)", "EffectRandom.Tick = unchecked((uint)u.Serial)")]
    [TestCase("net/ReplayField.cs", "void OnSkillUse(SkillUse u)", "EffectRandom.Tick = unchecked((uint)u.Serial)")]
    public void EveryEffectPlayPicksOneChildPerRandomPart(string file, string signature, string call)
    {
        var text = File.ReadAllText(Path.Combine(GameDir(), file)).Replace("\r\n", "\n");
        int at = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"game/{file}: no {signature}");
        int open = text.IndexOf('{', at), depth = 0, end = open;
        for (; end < text.Length; end++)
            if (text[end] == '{') depth++;
            else if (text[end] == '}' && --depth == 0) break;
        Assert.That(text[open..end], Does.Contain(call), $"game/{file}: {signature} has no {call}");
    }

    static string GameDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Ms2.Formats")) && File.Exists(Path.Combine(d.FullName, "game", "project.godot")))
                return Path.Combine(d.FullName, "game");
        Assert.Ignore("source tree not found");
        return null!;
    }
}

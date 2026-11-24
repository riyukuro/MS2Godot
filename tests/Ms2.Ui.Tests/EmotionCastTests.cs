using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The own emotion cast (StateSkill::FUN_1417eaa50, stateskill.cpp:1878-1913), PCBase::FUN_14166ed70 and
/// GetEmotionAnimation, StateEmotionIdle's bore timer, and the sweep of every emotion skill over the data.</summary>
public class EmotionCastTests
{
    static SkillData Row(string xml) =>
        SkillData.Load(XElement.Parse(xml), (e, child, key) => e.Elements(child).ToList());

    const string Sit = "<skill id=\"90200011\"><basic mainType=\"14\" paramStr=\"Sit\"><kinds offsetNameTag=\"-40\"/></basic><level value=\"1\"/></skill>";
    const string Couple = "<skill id=\"90300001\"><basic mainType=\"15\" paramStr=\"Hug\"/><level value=\"1\"/></skill>";

    [Test]
    public void TheSkillRowKeepsItsParamStrAndNameTagOffset()
    {
        // SkillData::Load (skilloverlapcompare.cpp:823, :844)
        var s = Row(Sit);
        Assert.That(s.ParamStr, Is.EqualTo("Sit"));
        Assert.That(s.OffsetNameTag, Is.EqualTo(-40));
        Assert.That(Row("<skill id=\"1\"><basic mainType=\"14\"/></skill>").ParamStr, Is.EqualTo(""));
        // the GMS2 per-skill file's shape: <kinds emotionType emotion> (INFERRED from the data, see SkillData.Load)
        var g = Row("<skill id=\"90200011\"><basic><kinds type=\"0\" subType=\"0\" emotionType=\"1\" emotion=\"hello\" offsetNameTag=\"5\"/></basic></skill>");
        Assert.That((g.MainType, g.ParamStr, g.OffsetNameTag, g.IsEmotion), Is.EqualTo((14, "hello", 5, true)));
        var c = Row("<skill id=\"90220001\"><basic><kinds type=\"0\" subType=\"0\" emotionType=\"2\" emotion=\"100001\"/></basic></skill>");
        Assert.That((c.MainType, c.ParamStr), Is.EqualTo((15, "")));
        Assert.That(Row("<skill id=\"2\"><basic><kinds type=\"0\" subType=\"0\"/></basic></skill>").MainType, Is.EqualTo(0));
    }

    [Test]
    public void AnEmotionCastIsNoModeZeroCast()
    {
        // FUN_1417ea570 (stateskill.cpp:1809-1810): IsEmotion goes to FUN_1417eaa50, which sends Emote mode 2 itself
        var s = Row(Sit);
        var r = new CastRecord { Skill = s, Level = s.GetLevelData(1), Tick = 1, CastId = 2 };
        Assert.That(SkillCast.Use(r, 6000, 55, false), Is.Null);
        var c = Row(Couple);
        Assert.That(SkillCast.Use(new CastRecord { Skill = c, Level = c.GetLevelData(1) }, 6000, 55, false), Is.Null);
    }

    [Test]
    public void TheEmotionEntersItsStateAndSendsModeTwo()
    {
        var s = Row(Sit);
        var o = EmotionCast.Use(s, jumping: false, onWater: false, stateId: 1, runningAnim: null);
        Assert.That(o.Handled && o.Enter, Is.True);
        Assert.That(o.Send, Is.EqualTo(new EmoteUseRequest(90200011, "Sit")));
        // from StateEmotionIdle (0xf) the same emotion starts again; from StateEmotion (0xb) with the same paramStr it does not,
        // but it is still sent (pcbase.cpp:3679-3683, stateskill.cpp:1904-1910)
        Assert.That(EmotionCast.Use(s, false, false, 0xf, null).Enter, Is.True);
        var kept = EmotionCast.Use(s, false, false, 0xb, "Sit");
        Assert.That(kept.Handled, Is.True);
        Assert.That(kept.Enter, Is.False);
        Assert.That(kept.Send, Is.Not.Null);
        Assert.That(EmotionCast.Use(s, false, false, 0xb, "Bow").Enter, Is.True);
    }

    [Test]
    public void TheEmotionIsRefusedJumpingOnWaterAndInTheBusyStates()
    {
        var s = Row(Sit);
        Assert.That(EmotionCast.Use(s, true, false, 6, null), Is.EqualTo(default(EmotionCast.Outcome)));
        Assert.That(EmotionCast.Use(s, false, true, 1, null), Is.EqualTo(default(EmotionCast.Outcome)));
        foreach (int st in new[] { 5, 8, 9, 0xd, 0x10, 0x1d, 0x1e })
            Assert.That(EmotionCast.Use(s, false, false, st, null).Handled, Is.False, $"state 0x{st:x}");
        foreach (int st in new[] { 1, 2, 4, 0xb, 0xf, 0x1b, 0x20 })
            Assert.That(EmotionCast.Use(s, false, false, st, null).Handled, Is.True, $"state 0x{st:x}");
        // main type 0xf (the couple emotion) is not this branch
        Assert.That(EmotionCast.Use(Row(Couple), false, false, 1, null).Handled, Is.False);
        // id 0: handled and entered, nothing sent (:1905)
        var zero = EmotionCast.Use(Row("<skill id=\"0\"><basic mainType=\"14\" paramStr=\"Sit\"/></skill>"), false, false, 1, null);
        Assert.That(zero.Handled && zero.Enter && zero.Send == null, Is.True);
    }

    [Test]
    public void TheIdleBoreTimerPicksOneOfTheBoreKinds()
    {
        var b = new EmotionIdleSub();
        b.Enter();
        Assert.That((b.Sub, b.Kind), Is.EqualTo((0x6f, 1)));
        var draws = new Queue<uint>();
        uint Rand() => draws.Dequeue();
        bool Has(int k) => k is 2 or 4;               // two bore kinds: the pick is 0x70 or 0x71
        b.Tick(0, 20000, 100, Rand, Has);             // starts the timer
        b.Tick(19999, 20000, 100, Rand, Has);
        Assert.That(b.Sub, Is.EqualTo(0x6f));
        draws.Enqueue(99); draws.Enqueue(3);          // the chance roll 99 (100 >= 99), the pick 3 % 2 = 1
        b.Tick(20000, 20000, 100, Rand, Has);
        Assert.That((b.Sub, b.Kind), Is.EqualTo((0x71, 3)), "the kind is 2 + the pick, whether or not it exists");
        b.EndOfSequence();
        Assert.That(b.Sub, Is.EqualTo(0x6f));
        b.Tick(20001, 20000, 100, Rand, Has);         // the timer starts again
        draws.Enqueue(50);                            // the chance roll 50 against 40: no pick
        b.Tick(40001, 20000, 40, Rand, Has);
        Assert.That(b.Sub, Is.EqualTo(0x6f));
        b.Tick(40002, 20000, 100, Rand, _ => false);
        draws.Enqueue(0);                             // no bore kind: no pick
        b.Tick(60002, 20000, 100, Rand, _ => false);
        Assert.That(b.Sub, Is.EqualTo(0x6f));
        Assert.That(draws, Is.Empty);
    }

    [Test]
    public void TheEmotionAnimationIsTheGroupsKind()
    {
        var g = EmotionGroupOf("<ms2><emotion name=\"Sit\"><anim name=\"Emotion_Sit_A\"/><idleAnim name=\"Emotion_Sit_Idle_A\"/>"
            + "<boreAnim name=\"Bore_A,Bore_B\"/></emotion><emotion name=\"Bow\"><anim name=\"Emotion_Bow_A\"/></emotion></ms2>");
        Assert.That(EmotionCast.Animation(g, "Sit", 0), Is.EqualTo("Emotion_Sit_A"));
        Assert.That(EmotionCast.Animation(g, "sit", 1), Is.EqualTo("Emotion_Sit_Idle_A"), "names in any case");
        Assert.That(EmotionCast.Animation(g, "Sit", 3), Is.EqualTo("Bore_B"));
        Assert.That(EmotionCast.Animation(g, "Sit", 4), Is.Null);
        Assert.That(EmotionCast.Animation(g, "Bow", 1), Is.Null, "no idleAnim: StateEmotion's end goes to StateIdle");
        Assert.That(EmotionCast.Animation(g, "Nope", 0), Is.Null);
        Assert.That(EmotionCast.Animation(null, "Sit", 0), Is.Null);
    }

    static EmotionGroup EmotionGroupOf(string xml)
    {
        var t = typeof(EmotionGroup).GetMethod("Read", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (EmotionGroup)t.Invoke(null, new object[] { null, XElement.Parse(xml) })!;
    }

    /// <summary>
    /// Every emotion skill (main type 0xe) of the data: its paramStr names an emotion in every face item's emotion group,
    /// whose anim (kind 0) the male and female bodies both have; its idleAnim and bore animations, when named, too. The GM
    /// faces' groups (an import of a GM_ common group) hold only part of the emotions: on them the rest plays no body motion
    /// (StateEmotion has no anim and goes to StateIdle), so they are listed, not failed; so is an emotion no face has.
    /// </summary>
    static void EveryEmotionResolvesAndPlays(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var tables = new GameTables(packs, "en");
        var rows = tables.Skills.Skills.Rows.Where(s => s.MainType == EmotionCast.MainTypeEmotion).OrderBy(s => s.Id).ToList();
        var table = new EmotionTable(packs, EmotionTable.Item);
        var faces = packs.List("Xml").Where(e => e.StartsWith("emotion/item/", StringComparison.OrdinalIgnoreCase))
            .Select(e => Path.GetFileNameWithoutExtension(e)).Where(n => uint.TryParse(n, out _)).Select(uint.Parse).OrderBy(i => i).ToList();
        var groups = faces.Select(f => (f, g: table.Group(f))).Where(x => x.g != null).ToList();
        var noParam = new List<string>();
        var missing = new List<string>();
        var gmFaces = new SortedDictionary<uint, (string Import, int Missing)>();
        var noSeq = new SortedSet<string>(StringComparer.Ordinal);
        int anims = 0, idles = 0, bores = 0;
        // the kf cycle (0 loop, 1 back and forth, 2 clamp) of each kind on the male body: a clamped anim ends StateEmotion
        // (EndOfSequence), a looped one keeps it
        string nx = NifXmlPath();
        var spec = nx == null ? null : new Ms2.Formats.Nif.NifSpec(nx);
        var cycles = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in rows)
        {
            if (s.ParamStr.Length == 0) { noParam.Add(s.Id.ToString()); continue; }
            foreach (var (f, g) in groups)
            {
                if (EmotionCast.Animation(g, s.ParamStr, 0) is not { } anim)
                {
                    string import = (string)PackXml.Parse(packs.Read("Xml", table.Entry(f))).Root.Element("import")?.Attribute("name") ?? "";
                    if (import.StartsWith("GM_", StringComparison.Ordinal)) gmFaces[f] = (import, gmFaces.GetValueOrDefault(f).Missing + 1);
                    else missing.Add($"{s.Id}:{s.ParamStr}@{f}");
                    continue;
                }
                for (int k = 0; k <= 4; k++)
                {
                    if (EmotionCast.Animation(g, s.ParamStr, k) is not { } name) continue;
                    foreach (var body in new[] { "male", "female" })
                        if (!BodySequences.Exists(packs, body, name)) noSeq.Add($"{body}/{name} ({s.ParamStr} kind {k})");
                }
            }
            var g0 = groups[0].g;
            if (EmotionCast.Animation(g0, s.ParamStr, 0) != null) anims++;
            if (EmotionCast.Animation(g0, s.ParamStr, 1) != null) idles++;
            if (EmotionCast.Animation(g0, s.ParamStr, 2) != null) bores++;
            if (spec != null)
                for (int k = 0; k <= 4; k++)
                    if (EmotionCast.Animation(g0, s.ParamStr, k) is { } name && BodySequences.Exists(packs, "male", name))
                    {
                        var kf = KfAnimation.Load(Ms2.Formats.Nif.NifFile.Load(spec, packs.Read(BodySequences.Archive, BodySequences.Entry("male", name))));
                        string key = $"kind {k} cycle {kf.Cycle}" + (k == 0 ? (EmotionCast.Animation(g0, s.ParamStr, 1) != null ? " with idleAnim" : " no idleAnim") : "");
                        cycles[key] = cycles.GetValueOrDefault(key) + 1;
                    }
        }
        TestContext.WriteLine($"{variable}: {rows.Count} emotion skills, {groups.Count} face groups; on face {groups[0].f}: {anims} with anim, "
            + $"{idles} with idleAnim, {bores} with bore; no paramStr: {(noParam.Count == 0 ? "none" : string.Join(", ", noParam))}; "
            + $"not in a face group: {(missing.Count == 0 ? "none" : string.Join(", ", missing.GroupBy(m => m[..m.IndexOf('@')]).Select(x => $"{x.Key} on {x.Count()} faces")))} ({missing.Count}); "
            + $"GM faces lacking some: {string.Join(", ", gmFaces.Select(x => $"{x.Key} {x.Value.Import} {x.Value.Missing}"))}; "
            + $"no body sequence: {(noSeq.Count == 0 ? "none" : string.Join(", ", noSeq))}; "
            + $"male kf cycles: {(spec == null ? "no nif.xml" : string.Join(", ", cycles.Select(x => $"{x.Key}: {x.Value}")))}");
        Assert.That(rows, Has.Count.GreaterThan(30));
        Assert.That(groups, Has.Count.GreaterThan(10));
        Assert.That(noParam, Is.Empty);
        // an emotion no face group has (GMS2's Nemi_Sit_*) enters StateEmotion with no anim and goes to StateIdle on every
        // face: listed; one that some faces have and other (not GM) faces lack fails
        int plainFaces = groups.Count - gmFaces.Count;
        var noFace = missing.GroupBy(m => m[..m.IndexOf('@')]).Where(x => x.Count() == plainFaces).Select(x => x.Key).ToList();
        TestContext.WriteLine($"{variable}: in no face group (no motion): {(noFace.Count == 0 ? "none" : string.Join(", ", noFace))}");
        Assert.That(missing.Where(m => !noFace.Contains(m[..m.IndexOf('@')])), Is.Empty);
        Assert.That(noSeq, Is.Empty);
    }

    /// <summary>The item ids of the data: KMS itemdata/NNN.xml rows, GMS2 item/a/bb/id.xml files.</summary>
    internal static IEnumerable<int> ItemIds(PackSet packs)
    {
        var rx = new System.Text.RegularExpressions.Regex("<item\\s+id=\"(\\d+)\"");
        foreach (var e in packs.List("Xml"))
        {
            if (e.StartsWith("itemdata/", StringComparison.OrdinalIgnoreCase))
            {
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(PackXml.Text(packs.Read("Xml", e))))
                    yield return int.Parse(m.Groups[1].Value);
            }
            else if (e.StartsWith("item/", StringComparison.OrdinalIgnoreCase) && int.TryParse(Path.GetFileNameWithoutExtension(e), out int id))
                yield return id;
        }
    }

    /// <summary>
    /// Every emotion item (type 24, FUN_14053ce40) of the data: its skill (the learn box's name, IsEmotionLearned's id) is an
    /// emotion skill row (main type 0xe or 0xf), or 0 (listed: the box names no skill and the learn is the server's to
    /// refuse). Items of other types whose skill is an emotion are counted by type: GMS2's emotion items are type 2, which
    /// the KMS client's double click casts as an item skill, not the learn box (no GMS2 client code to say otherwise).
    /// </summary>
    static void EveryEmotionItemTeachesAnEmotion(string variable, bool typed)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var tables = new GameTables(packs, "en");
        var all = ItemIds(packs).Distinct().Select(tables.Items.Get).Where(d => d != null).ToList();
        var items = all.Where(d => d.Type == EmotionList.EmotionItemType).ToList();
        var noSkill = items.Where(d => d.SkillId == 0).Select(d => d.Id).ToList();
        var bad = items.Where(d => d.SkillId != 0 && tables.Skills.GetSkillData((uint)d.SkillId) is not { IsEmotion: true })
            .Select(d => $"{d.Id}:{d.SkillId}").ToList();
        int couple = items.Count(d => tables.Skills.GetSkillData((uint)d.SkillId)?.MainType == EmotionCast.MainTypeCouple);
        var byType = all.Where(d => d.SkillId != 0 && tables.Skills.GetSkillData((uint)d.SkillId) is { IsEmotion: true })
            .GroupBy(d => d.Type).OrderBy(g => g.Key).Select(g => $"type {g.Key}: {g.Count()}");
        TestContext.WriteLine($"{variable}: {all.Count} items, {items.Count} emotion items (type 24; {couple} teach a couple emotion), "
            + $"first {items.FirstOrDefault()?.Id}; skill 0: {(noSkill.Count == 0 ? "none" : string.Join(", ", noSkill))}; "
            + $"not an emotion skill: {(bad.Count == 0 ? "none" : string.Join(", ", bad))}; items with an emotion skill by type: {string.Join(", ", byType)}");
        Assert.That(bad, Is.Empty);
        if (typed) Assert.That(items, Is.Not.Empty);
    }

    [Test]
    public void EveryKmsEmotionItemTeachesAnEmotion() => EveryEmotionItemTeachesAnEmotion("MS2_DATA", true);

    [Test]
    public void EveryGms2EmotionItemTeachesAnEmotion() => EveryEmotionItemTeachesAnEmotion("MS2_GMS2_DATA", false);

    static string NifXmlPath()
    {
        var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(nx))
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "MS2Godot.sln"))) { nx = Path.Combine(d.FullName, "external", "nifxml", "nif.xml"); break; }
        return !string.IsNullOrEmpty(nx) && File.Exists(nx) ? nx : null;
    }

    [Test]
    public void EveryKmsEmotionResolvesAndPlays() => EveryEmotionResolvesAndPlays("MS2_DATA");

    [Test]
    public void EveryGms2EmotionResolvesAndPlays() => EveryEmotionResolvesAndPlays("MS2_GMS2_DATA");
}

using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every quick slot skill of every starting job (rank 1, level 1, with its sub skills), on the KMS and the GMS2 data: the
/// additional effects the skill links to (KMS actionAdditional@additionalID and the like, GMS2 conditionSkill@skillID with
/// splash 0), and the skills those link on, are read through the table the game reads (GameTables.NewAdditionalTable: the
/// KMS additional/*.xml rows, the GMS2 additionaleffect/ files). Each linked effect level must exist, each of its motion
/// effects (start, keep, stop, invoke, the overlap keep rows) must name an effect xml the data has, and the port's
/// AdditionalBuffList must play every one of them through the effect service in an add, update, invoke, count and remove run.
/// </summary>
public class StartingSkillBuffEffectSweepTests
{
    static readonly int[] StartingJobs = { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 };

    sealed class Recorder : IBuffMotionEffects
    {
        public readonly HashSet<string> Played = new(StringComparer.Ordinal);
        int next = 1;
        public int Keep(string effect, int ownerId, int casterId) { Played.Add(effect); return next++; }
        public void Insert(string effect, int ownerId, int casterId) => Played.Add(effect);
        public void Stop(int id) { }
        public bool OwnerShown(int ownerId) => true;
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryLinkedBuffEffectResolvesAndIsPlayed(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        var packs = TestData.Packs(dir);      // the run-long pack set of the data folder
        var jobs = JobTable.Load(packs);
        var effects = new EffectTable(packs);
        var table = GameTables.NewAdditionalTable(packs);
        var skills = RawSkills(packs);
        var rows = RawAdditionals(packs);
        Assert.That(skills.Count, Is.GreaterThan(1000), "skills read");

        var links = new SortedSet<(uint Id, ushort Level)>();          // the linked effect levels
        var linkedFrom = new Dictionary<(uint, ushort), string>();
        int quickSlots = 0;
        foreach (int g in StartingJobs)
        {
            var jd = jobs.GetJobData(g);
            Assert.That(jd, Is.Not.Null, $"job {g}");
            var list = jd.Skills.Where(s => s.JobLevel <= 1 && s.QuickSlotPriority != 0x7fffffff).ToList();
            Assert.That(list, Is.Not.Empty, $"job {g} quick slot skills");
            foreach (var js in list)
            {
                quickSlots++;
                var seen = new HashSet<string>();
                void Link(uint id, ushort level, string via)
                {
                    if (links.Add((id, level))) linkedFrom[(id, level)] = via;
                }
                Walk(packs, skills, rows, (int)js.SkillId, 1, 0, seen, js.SkillId.ToString(), Link);
                foreach (var sub in js.SubSkillIds) Walk(packs, skills, rows, (int)sub, 1, 1, seen, $"{js.SkillId}>{sub}", Link);
            }
        }

        var missingLevels = new List<string>();
        var offByFeature = new List<string>();
        var missingXmls = new SortedSet<string>(StringComparer.Ordinal);
        var notPlayed = new List<string>();
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var xmls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int withEffects = 0;
        foreach (var (id, level) in links)
        {
            var lv = table.GetLevel(id, level);
            if (lv == null)
            {
                // an effect whose every level is of a feature that is off is not in the game's table (AdditionalData::Load
                // returns false): the link finds nothing in the client too
                string at = $"{id} level {level} (from {linkedFrom[(id, level)]})";
                if (rows.TryGetValue(id, out var row) && row.Elements("level").Any()
                    && row.Elements("level").All(l => !LobbyFeatures.IsEnabledByName(packs, (string)l.Attribute("feature"))))
                    offByFeature.Add(at);
                else missingLevels.Add(at);
                continue;
            }
            var named = new List<(string Kind, string Xml)>();
            void Name(string kind, string xml) { if (!string.IsNullOrEmpty(xml)) named.Add((kind, xml)); }
            Name("start", lv.StartEffect); Name("keep", lv.KeepEffect); Name("stop", lv.StopEffect); Name("invoke", lv.InvokeEffect);
            if (lv.OverlapKeep is { } o)
            {
                foreach (var r in o.Start) Name("overlap start", r.Effect);
                foreach (var r in o.Keep) Name("overlap keep", r.Effect);
                foreach (var r in o.End) Name("overlap end", r.Effect);
            }
            if (named.Count == 0) continue;
            withEffects++;
            foreach (var (kind, xml) in named)
            {
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                xmls.Add(xml);
                if (effects.Get(xml) == null) missingXmls.Add($"{xml} ({kind} of {id} level {level})");
            }
            var played = Run(lv, id, level);
            foreach (var (kind, xml) in named)
                if (!played.Contains(xml)) notPlayed.Add($"{id} level {level} {kind} {xml}");
        }

        TestContext.Progress.WriteLine($"{env}: {quickSlots} quick slot skills, {links.Count} linked effect levels, {withEffects} with motion effects, "
            + $"{xmls.Count} effect xmls ({string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}"))})");
        foreach (var m in offByFeature) TestContext.Progress.WriteLine($"{env}: linked effect with every level of a feature that is off: {m}");
        foreach (var m in missingLevels) TestContext.Progress.WriteLine($"{env}: linked effect level not in the table: {m}");
        foreach (var m in missingXmls) TestContext.Progress.WriteLine($"{env}: effect xml not in the data: {m}");

        // the Knight's Iron Defense (10100061): KMS links it with actionAdditional@additionalID, GMS2 with conditionSkill splash 0;
        // the KMS row has no Motion (no effects), the GMS2 file has start, keep and stop effects
        Assert.That(links.Contains((10100061u, (ushort)1)), "Iron Defense 10100061 links its effect 10100061");
        var defense = table.GetLevel(10100061, 1);
        Assert.That(defense, Is.Not.Null, "Iron Defense's effect level");
        TestContext.Progress.WriteLine($"{env}: 10100061 level 1 effects: start '{defense.StartEffect}' keep '{defense.KeepEffect}' stop '{defense.StopEffect}'");
        // the live proof's: Iron Shield's keep and the stun keep
        Assert.That(table.GetLevel(10100151, 1)?.KeepEffect, Is.EqualTo("CH/Skill/Knight/Eff_Knight_IronShield_Keep_01.xml"));
        Assert.That(table.GetLevel(10000031, 1)?.KeepEffect, Is.EqualTo("Additional/Stun/Eff_Additional_Stun_Keep.xml"));
        // Head Butt is the beginner job's; the archer's 10500051 puts the same stun on its target
        Assert.That(table.GetLevel(10500051, 1)?.KeepEffect, Is.EqualTo("Additional/Stun/Eff_Additional_Stun_Keep.xml"));

        Assert.That(quickSlots, Is.GreaterThan(100));
        Assert.That(withEffects, Is.GreaterThan(30));
        Assert.That(missingLevels, Is.Empty, "linked effect levels the table does not have");
        Assert.That(missingXmls, Is.Empty, "effect xmls the data does not have");
        Assert.That(notPlayed, Is.Empty, "effects the port did not play");
    }

    /// <summary>The level through AdditionalBuffList: an OnBuff add (start), a frame (keep), the invoke by key, each overlap count
    /// from 1 past the largest row (the overlap start, keep and end rows), then the remove (stop, the last end row).</summary>
    static HashSet<string> Run(AdditionalLevelData lv, uint id, ushort level)
    {
        var rec = new Recorder();
        var list = new AdditionalBuffList((i, l) => i == id ? lv : null) { Effects = rec };
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 1, 7, 0, new BuffState(0, 1000, id, level, 1, 1), 0));
        list.UpdateEffects();
        list.PlayInvokeEffect(1);
        if (lv.OverlapKeep is { } o)
        {
            uint max = 1;
            foreach (var r in o.Start.Concat(o.Keep).Concat(o.End)) max = Math.Max(max, r.Count);
            for (uint c = 2; c <= max + 1; c++)
            {
                list.Apply(new BuffPacket(BuffPacket.Modify, 5, 1, 7, BuffPacket.ModifyState, new BuffState(0, 1000, id, level, (int)c, 1), null));
                list.UpdateEffects();
            }
        }
        list.Apply(new BuffPacket(BuffPacket.Remove, 5, 1, 7, 0, null, null));
        return rec.Played;
    }

    /// <summary>The skill elements by id: KMS skilldata/*.xml &lt;skill&gt; rows, GMS2 skill/&lt;nn&gt;/&lt;id&gt;.xml files as rows
    /// (SkillTable.ToSkillRows); the first of an id kept.</summary>
    static Dictionary<int, XElement> RawSkills(PackSet packs)
    {
        var skills = new Dictionary<int, XElement>();
        foreach (var e in packs.List("Xml"))
        {
            string fileId = SkillTable.PerSkillId(e);
            if (fileId == null && !e.StartsWith("skilldata/", StringComparison.OrdinalIgnoreCase)) continue;
            XDocument x;
            if (fileId != null) { x = XDocument.Parse(PackXml.Text(packs.Read("Xml", e))); SkillTable.ToSkillRows(x); }
            else x = PackXml.Parse(packs.Read("Xml", e));
            foreach (var s in fileId != null ? new[] { x.Root } : x.Descendants("skill"))
                if (s != null && int.TryParse(fileId ?? (string)s.Attribute("id"), out int id)) skills.TryAdd(id, s);
        }
        return skills;
    }

    /// <summary>The raw effect rows by id for their skill links: the KMS additional/*.xml &lt;additional&gt; rows (a later row of an
    /// id replaces the earlier, as the client reads them), else the GMS2 additionaleffect/&lt;id&gt;.xml roots.</summary>
    static Dictionary<uint, XElement> RawAdditionals(PackSet packs)
    {
        var rows = new Dictionary<uint, XElement>();
        var kms = packs.List("Xml").Where(n => n.StartsWith("additional/", StringComparison.OrdinalIgnoreCase) && n.IndexOf('/', 11) < 0
            && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (kms.Count > 0)
        {
            foreach (var n in kms)
                foreach (var a in PackXml.Parse(packs.Read("Xml", n)).Descendants("additional"))
                    if (uint.TryParse((string)a.Attribute("id"), out uint id)) rows[id] = a;
            return rows;
        }
        foreach (var n in packs.List("Xml").Where(n => AdditionalTable.IsGms2File(n)))
            if (uint.TryParse(Path.GetFileNameWithoutExtension(n), out uint id))
                rows[id] = PackXml.Parse(packs.Read("Xml", n)).Root;
        return rows;
    }

    /// <summary>A skill level's links, 4 deep, the level picked among the available ones as the game picks them
    /// (FeatureElements.Collect, FUN_14028faa0: a level of a feature that is off is not there): GMS2 conditionSkill@skillID with splash 0 is an effect, any other skillID or
    /// splashActiveSkillID a skill; an attribute named *additional*ID lists effects (KMS actionAdditional@additionalID). An
    /// effect's own skillID attributes are skills.</summary>
    static void Walk(PackSet packs, Dictionary<int, XElement> skills, Dictionary<uint, XElement> rows, int id, int level, int depth, HashSet<string> seen,
        string via, Action<uint, ushort, string> link)
    {
        if (depth > 4 || !seen.Add($"s{id}.{level}") || !skills.TryGetValue(id, out var s)) return;
        var levels = FeatureElements.Collect(packs, s, "level", "value");
        var lv = levels.FirstOrDefault(l => (int?)l.Attribute("value") == level) ?? levels.FirstOrDefault();
        if (lv == null) return;
        foreach (var el in lv.Descendants())
            foreach (var a in el.Attributes())
            {
                string an = a.Name.LocalName, v = a.Value;
                int lvl = (int?)el.Attribute("level") ?? 1;
                if (an == "skillID" && el.Name.LocalName == "conditionSkill" && (string)el.Attribute("splash") == "0" && uint.TryParse(v, out uint bid) && bid != 0)
                    Effect(packs, skills, rows, bid, lvl, depth + 1, seen, $"{via}>b{bid}", link);
                else if ((an == "skillID" || an == "splashActiveSkillID") && int.TryParse(v, out int sid) && sid != 0)
                    Walk(packs, skills, rows, sid, lvl, depth + 1, seen, $"{via}>{sid}", link);
                if (an.Contains("additional", StringComparison.OrdinalIgnoreCase) && an.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
                    foreach (var t in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (uint.TryParse(t, out uint aid) && aid != 0) Effect(packs, skills, rows, aid, lvl, depth + 1, seen, $"{via}>b{aid}", link);
            }
    }

    static void Effect(PackSet packs, Dictionary<int, XElement> skills, Dictionary<uint, XElement> rows, uint id, int level, int depth, HashSet<string> seen,
        string via, Action<uint, ushort, string> link)
    {
        link(id, (ushort)level, via);
        if (depth > 4 || !seen.Add($"b{id}") || !rows.TryGetValue(id, out var row)) return;
        foreach (var el in row.Descendants())
            foreach (var a in el.Attributes())
                if (a.Name.LocalName == "skillID" && int.TryParse(a.Value, out int sid) && sid != 0)
                    Walk(packs, skills, rows, sid, (int?)el.Attribute("level") ?? 1, depth + 1, seen, $"{via}>{sid}", link);
    }
}

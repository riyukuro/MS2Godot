using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>One motion of a skill level: the animation sequence it plays, its effect file and what the attack points name.</summary>
public sealed class SkillMotion
{
    public string SequenceName;          // e.g. Attack_01_A; the sequence of the actor's kfm / the player's kf
    public string MotionEffect;          // effect xml path (Xml pack effect/...), may be null
    /// <summary>
    /// SkillMotionData::m_mapTagEffects ("strTagEffects", skillleveldata.cpp:1180-1188): a text key of the motion's sequence to
    /// the effect xml StateSkill::TextKeyEvent plays on the caster when the key comes (stateskill.cpp:795-834). Null without
    /// any. See <see cref="ParseTagEffects"/>.
    /// </summary>
    public Dictionary<string, string> TagEffects;

    /// <summary>
    /// The tag effect list as SkillMotionData::Load splits it: the attribute's "," tokens (CString::Tokenize, empty ones
    /// skipped, nothing trimmed), each split on "=" the same way; a token of exactly two parts maps its first to its second
    /// (CRBMap::SetAt, so a later key replaces an earlier one; keys compare case-sensitively). Null for no pairs.
    /// </summary>
    public static Dictionary<string, string> ParseTagEffects(string attr)
    {
        if (string.IsNullOrEmpty(attr)) return null;
        Dictionary<string, string> map = null;
        foreach (var tag in attr.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = tag.Split('=', StringSplitOptions.RemoveEmptyEntries);
            if (pair.Length != 2) continue;
            (map ??= new Dictionary<string, string>(StringComparer.Ordinal))[pair[0]] = pair[1];
        }
        return map;
    }
    public List<string> HitEffects = new();   // damage effectHit paths of its attack points
    /// <summary>SkillMotionData::m_nLoopCount ("loopCount", skillleveldata.cpp:1196): the loops StateSkill repeats between
    /// the sequence's "loopstart" and "loopend" keys (FUN_1417f0940 takes it as m_nRepeat).</summary>
    public int LoopCount;
    /// <summary>Each &lt;attack&gt;'s "point" in order (SkillAttackData::m_strPoint, skillattackdata.cpp:575; "" without one):
    /// GetAttackDataAt(i)'s text key.</summary>
    public List<string> AttackPoints = new();
    /// <summary>
    /// The motion's move, as SkillMotionData::Load (skillleveldata.cpp:1189-1194) reads it from the &lt;motion&gt; itself:
    /// "movetype" (m_eMoveType: 0 linear, 1 arc, 2 blink, 3 move, the motion controller StateSkill::vfunc_4 picks), "keyMove"
    /// (m_fAC) and "movedistance" (m_fB8), the units CSkillRecord::FUN_141759b60 spreads over a move phase, "moveangle"
    /// (m_fMoveAngle, degrees) and "ignoreCollisionGroups" (m_nIgnoreCollisionGroups).
    /// </summary>
    public int MoveType;
    public float KeyMove, MoveDistance, MoveAngle;
    public uint IgnoreCollisionGroups;
    /// <summary>
    /// The motion's other effect files and flags, as SkillMotionData::Load (@0x140808350, skillleveldata.cpp:1163-1175)
    /// reads them; its launch-option gate (Unk_LaunchOptions::FUN_1406af020, launchoptions.cpp:78-81) is always true.
    /// The region skill object (CRegionSkillObject) uses them: SplashInvokeEffect (+0x30) on a hit, LoopEffect (+0x38) as its
    /// second loop, SplashEndEffect (+0x48) on the reset, SplashDurationCastEffect (+0x98) once after the start tick,
    /// IgnoreOptimalHideCastEffect (+0xa0) to show them also for a non-local owner, SplashCastEffectStartDelay (+0xa1) to
    /// list the points instead of spawning at once (skilloverlapcompare.h:211-222).
    /// </summary>
    public string LoopEffect, SplashInvokeEffect, SplashEndEffect, SplashDurationCastEffect;
    public bool IgnoreOptimalHideCastEffect, SplashCastEffectStartDelay;
    /// <summary>The motion's push cylinder (SkillMotionData::Load, skillleveldata.cpp:1197-1201), the force zone
    /// <see cref="ForceZone"/> reads; all 0 without the attributes.</summary>
    public PushCylinder PushCylinder;
}

/// <summary>
/// What StateSkill reads of a skill's &lt;basic&gt;: &lt;stateAttr moveType movingInMovekey&gt; (SkillData::Load,
/// skilloverlapcompare.cpp:860, :870: m_nMoveType, m_bMovingInMovekey) and whether &lt;magicControl enable&gt; is set
/// (Unk_SkillBlock150::Load @0x140803860, skillleveldata.cpp:653-665: m_block150.m_eEnable != 0).
/// </summary>
public readonly record struct SkillStateMove(int MoveType, bool MovingInMovekey, bool MagicControl);

public sealed class SkillLevelRow
{
    public int Level;
    public List<SkillMotion> Motions = new();
}

/// <summary>Skill rows from the Xml pack (skilldata/*.xml): id -> levels -> motions. Only what the replay needs.</summary>
public sealed class SkillTable
{
    readonly Dictionary<int, List<SkillLevelRow>> skills = new();
    readonly HashSet<int> charging = new();
    readonly Dictionary<int, SkillStateMove> stateMoves = new();
    public int Count => skills.Count;

    /// <summary>A per-skill file's text in the shape the skilldata rows have: the recorded GMS2 data holds a motion's attributes in a child
    /// &lt;motionProperty&gt; and names an attack's children rangeProperty, arrowProperty and damageProperty (skilldata: attributes on &lt;motion&gt;,
    /// &lt;range&gt;, &lt;arrow&gt;, &lt;damage&gt;); the attribute names are the same. Not mapped, and so read as 0 / none: the cooldown
    /// (level/beginCondition@cooldownTime, skilldata level@cooldown) and the weapon condition (level/beginCondition/weapon, skilldata basic/condition/weapon),
    /// which have no recording to check the mapping against. Text that does not parse comes back unchanged.</summary>
    public static string ToSkillRows(string xml)
    {
        if (xml.IndexOf("Property", StringComparison.Ordinal) < 0) return xml;
        XDocument d;
        try { d = XDocument.Parse(xml); } catch { return xml; }
        ToSkillRows(d);
        return d.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>The same mapping on a parsed file, in place.</summary>
    public static void ToSkillRows(XDocument d)
    {
        foreach (var e in d.Descendants().ToList())
        {
            switch (e.Name.LocalName)
            {
                case "rangeProperty": e.Name = "range"; break;
                case "arrowProperty": e.Name = "arrow"; break;
                case "damageProperty": e.Name = "damage"; break;
                case "motion":
                    // the motion's own attributes win; the property child's attributes and children move up onto the motion
                    foreach (var mp in e.Elements("motionProperty").ToList())
                    {
                        foreach (var at in mp.Attributes()) if (e.Attribute(at.Name) == null) e.SetAttributeValue(at.Name, at.Value);
                        mp.Remove();
                        foreach (var c in mp.Nodes().ToList()) e.Add(c);
                    }
                    break;
            }
        }
    }

    /// <summary>The skill id (digits) of a per-skill file name, skill/&lt;nn&gt;/&lt;id&gt;.xml; null for any other entry.</summary>
    public static string PerSkillId(string entry)
    {
        if (!entry.StartsWith("skill/", StringComparison.OrdinalIgnoreCase) || !entry.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return null;
        int slash = entry.IndexOf('/', 6);
        if (slash < 0 || entry.IndexOf('/', slash + 1) >= 0) return null;
        string id = entry.Substring(slash + 1, entry.Length - slash - 5);
        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Lazy<SkillTable>> shared = new();

    /// <summary>The one table of a data set: the parse of every skilldata file (about 190 MB of XML nodes and 0.2 s) is paid
    /// once per PackSet, not again by each user (the own cast, the remote casts, the region skills, the replay). Nothing
    /// changes a table after its build, so the users read the same one. With a <see cref="SetSource"/> the rows come from
    /// that other read and the files are not read here.</summary>
    public static SkillTable For(PackSet packs) => shared.GetValue(packs, p => new Lazy<SkillTable>(() =>
        sources.TryGetValue(p, out var source) && source() is { } fed ? fed : new SkillTable(p))).Value;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Func<SkillTable>> sources = new();

    /// <summary>Gives <see cref="For"/> the table another reader of the same skilldata files fills (<see cref="Add"/> per
    /// &lt;skill&gt; as it reads them), so one read of the files serves both; <paramref name="source"/> returns null when that
    /// read failed, and For then reads the files itself. Has no effect once For has built the table of the set.</summary>
    /// <remarks>The first source given for a set stays: a second reader of the same set's files (another language's tables)
    /// would give the same rows, and replacing the first, already read, with it would put its whole read on the first cast's
    /// frame. A source belongs to the PackSet object it was given for (the table compares keys by reference, not by Equals),
    /// so another set (another Data folder: KMS2 and GMS2, which PackSet.Shared opens as a new set) has its own source and
    /// its own table, never this one's.</remarks>
    public static void SetSource(PackSet packs, Func<SkillTable> source) => sources.TryAdd(packs, source);

    /// <summary>An empty table, filled by <see cref="Add"/>.</summary>
    public SkillTable() { }

    public SkillTable(PackSet packs)
    {
        var entries = packs.List("Xml").Where(n => n.StartsWith("skilldata/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
        // the recorded GMS2 client's data keeps one file per skill (skill/<nn>/<id>.xml): its root holds the rows, the id is the file name
        if (entries.Count == 0) entries = packs.List("Xml").Where(n => PerSkillId(n) != null).ToList();
        foreach (var entry in entries)
        {
            XDocument x;
            string fileId = PerSkillId(entry);
            try
            {
                if (fileId != null) { x = XDocument.Parse(PackXml.Text(packs.Read("Xml", entry))); ToSkillRows(x); }
                else x = PackXml.Parse(packs.Read("Xml", entry));
            }
            catch { continue; }
            foreach (var s in fileId != null ? new[] { x.Root } : x.Descendants("skill"))
                if (s != null && int.TryParse(fileId ?? (string)s.Attribute("id"), out int id)) Add(id, s);
        }
    }

    /// <summary>Adds (or replaces) skill <paramref name="id"/>'s rows from its &lt;skill&gt; element, as the constructor does for each one it reads.</summary>
    public void Add(int id, XElement s)
    {
        if (s.Descendants("basic").Elements("combo").FirstOrDefault() is { } combo && Flag(combo, "chargingSkill")) charging.Add(id);
        else charging.Remove(id);
        var basic = s.Element("basic");
        var stateAttr = basic?.Element("stateAttr");
        var move = new SkillStateMove(Int(stateAttr, "moveType"), stateAttr != null && Flag(stateAttr, "movingInMovekey"),
            Int(basic?.Element("magicControl"), "enable") != 0);
        if (move != default) stateMoves[id] = move;
        else stateMoves.Remove(id);
        var levels = new List<SkillLevelRow>();
        foreach (var l in s.Elements("level"))
        {
            var row = new SkillLevelRow { Level = int.TryParse((string)l.Attribute("value"), out int lv) ? lv : 1 };
            foreach (var m in l.Elements("motion"))
            {
                var mo = new SkillMotion
                {
                    SequenceName = (string)m.Attribute("sequenceName"), MotionEffect = (string)m.Attribute("motionEffect"),
                    LoopEffect = (string)m.Attribute("loopEffect"), SplashInvokeEffect = (string)m.Attribute("splashInvokeEffect"),
                    SplashEndEffect = (string)m.Attribute("splashEndEffect"), SplashDurationCastEffect = (string)m.Attribute("splashDurationCastEffect"),
                    IgnoreOptimalHideCastEffect = Flag(m, "IgnoreOptimalHideCastEffect"), SplashCastEffectStartDelay = Flag(m, "splashCastEffectStartDelay"),
                };
                if (int.TryParse((string)m.Attribute("loopCount"), out int loops)) mo.LoopCount = loops;
                mo.TagEffects = SkillMotion.ParseTagEffects((string)m.Attribute("strTagEffects"));
                mo.MoveType = Int(m, "movetype");
                mo.KeyMove = Float(m, "keyMove");
                mo.MoveDistance = Float(m, "movedistance");
                mo.MoveAngle = Float(m, "moveangle");
                if (uint.TryParse((string)m.Attribute("ignoreCollisionGroups"), out uint ignore)) mo.IgnoreCollisionGroups = ignore;
                mo.PushCylinder = new PushCylinder(Float(m, "pushCylinderVelH"), Float(m, "pushCylinderVelV"), Float(m, "pushCylinderOuter"),
                    Float(m, "pushCylinderInner"), Float(m, "pushCylinderHeight"));
                foreach (var a in m.Elements("attack")) mo.AttackPoints.Add((string)a.Attribute("point") ?? "");
                foreach (var d in m.Descendants("damage"))
                    if ((string)d.Attribute("effectHit") is { Length: > 0 } eh) mo.HitEffects.Add(eh);
                row.Motions.Add(mo);
            }
            levels.Add(row);
        }
        skills[id] = levels;
    }

    /// <summary>The motion a cast plays: the level's row (the nearest level at or below, else the first) and the motion index from the packet.</summary>
    public SkillMotion Motion(int skillId, int level, int motionIndex)
    {
        var row = Row(skillId, level);
        if (row == null) return null;
        return row.Motions[Math.Clamp(motionIndex, 0, row.Motions.Count - 1)];
    }

    /// <summary>The motion count of the row <see cref="Motion"/> takes (SkillLevelData::GetMotionCount, which
    /// CSkillRecord::FUN_141759e40 @0x141759e40, skillrecord.cpp:315-323, compares the motion index with); 0 without a row.</summary>
    public int MotionCount(int skillId, int level) => Row(skillId, level)?.Motions.Count ?? 0;

    SkillLevelRow Row(int skillId, int level)
    {
        if (!skills.TryGetValue(skillId, out var levels) || levels.Count == 0) return null;
        var row = levels.Where(r => r.Level <= level).OrderByDescending(r => r.Level).FirstOrDefault() ?? levels[0];
        if (row.Motions.Count == 0) row = levels.FirstOrDefault(r => r.Motions.Count > 0);
        return row;
    }

    public bool Has(int skillId) => skills.ContainsKey(skillId);

    /// <summary>Every skill id the table has rows for.</summary>
    public IReadOnlyCollection<int> Ids => skills.Keys;

    /// <summary>Skill <paramref name="skillId"/>'s level rows in file order; empty without rows.</summary>
    public IReadOnlyList<SkillLevelRow> Levels(int skillId) => skills.TryGetValue(skillId, out var l) ? l : Array.Empty<SkillLevelRow>();

    /// <summary>&lt;basic&gt;&lt;combo chargingSkill&gt; (Unk_SkillBlockA4::Load @0x140802ef0, skillleveldata.cpp:493): SkillData
    /// +0xad, m_blockA4.m_bChargingSkill, which CStateManager::FUN_1417db550 tests for a record in StateSkill.</summary>
    public bool ChargingSkill(int skillId) => charging.Contains(skillId);

    /// <summary>The skill's <see cref="SkillStateMove"/>; all zero without the elements.</summary>
    public SkillStateMove StateMove(int skillId) => stateMoves.TryGetValue(skillId, out var m) ? m : default;

    static int Int(XElement e, string name) => e != null && int.TryParse((string)e.Attribute(name), out int v) ? v : 0;
    static float Float(XElement e, string name) =>
        e != null && float.TryParse((string)e.Attribute(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;

    /// <summary>XmlQuery of a bool (tinyxmlhelper.cpp, @0x1408736d0): TiXmlElement::QueryBoolAttribute (tinyxml.cpp:620-645) sets
    /// true when the value starts with "true", "yes" or "1" (StringEqual, case-insensitive prefix); else the flag stays false.</summary>
    public static bool Flag(XElement e, string name) =>
        (string)e.Attribute(name) is { Length: > 0 } v && (v.StartsWith("true", StringComparison.OrdinalIgnoreCase) || v.StartsWith("yes", StringComparison.OrdinalIgnoreCase) || v.StartsWith('1'));

    /// <summary>
    /// The level data and its motion 0, as CRegionSkillObject::Init gets them (regionskillobject.cpp:91-99: GetSkillData,
    /// GetLevelData(nLevel), GetMotion(0)): null when the skill, that exact level or a motion is missing.
    /// </summary>
    public SkillMotion RegionMotion(int skillId, int level)
    {
        if (!skills.TryGetValue(skillId, out var levels)) return null;
        var row = levels.FirstOrDefault(r => r.Level == level);
        return row == null || row.Motions.Count == 0 ? null : row.Motions[0];
    }
}

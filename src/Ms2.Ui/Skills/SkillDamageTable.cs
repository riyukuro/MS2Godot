using System.Globalization;
using System.Xml;

namespace Ms2.Ui.Skills;

/// <summary>
/// The &lt;damage&gt; of a skill attack (SkillDamage::Load @0x1407e01c0, commonfiles/skillattackdata.cpp:251, called from
/// SkillAttackData::Load :574 as m_damage): the damage and push attributes. A missing attribute keeps the ctor's value
/// (:175: count 1, pushprob 1, hitSpeedRate 1, damagedVibrateRatio 1, the rest 0); an attack without &lt;damage&gt; keeps
/// them all. STILL-OPEN, not read yet: IgnoreOptimalHideDamageHitEffect / RemainEffect, isConstDamageValue,
/// constDamageByTargetSP, attackMaterial (the hit sound), invokeEffectDisable and damageEventDisable.
/// </summary>
public sealed class SkillAttackDamage
{
    public static readonly SkillAttackDamage None = new();

    public int Count = 1, Hand;
    public float Rate;
    /// <summary>"value" (m_nValue, read as __int64).</summary>
    public long Value;
    public float HitSpeedRate = 1f, HitPauseTime, DamagedVibrateRatio = 1f;
    public string EffectHit = "", EffectRemain = "";
    public float DamageByTargetMaxHP;
    public long ConstDamageByTargetHP;
    /// <summary>"push" (SkillPushType, XmlValue: an enum read as a number).</summary>
    public int Push;
    public float PushDistance, PushDuration, PushProb = 1f;
    /// <summary>"pushEaseType", 0 outside 0..2 (:274).</summary>
    public int PushEaseType;
    /// <summary>"pushApplyField" (SkillPushApplyField): 1 no push on the ground states, 2 none off them (GetPush :393).</summary>
    public int PushApplyField;
    public float PushUpDistance;
    public int PushDown, PushFall, PushPriority, PushPriorityHitImmune;
    public string PushAnimation = "";
    /// <summary>"stateHitIgnore" / "stateHitOnly" (SkillStateHit enums).</summary>
    public int StateHitIgnore, StateHitOnly;
    public int SuperArmorBreak;

    /// <summary>HasDamage @0x1407e0f40 (:384): value, constDamageByTargetHP, rate or damageByTargetMaxHP above 0.</summary>
    public bool HasDamage => Value > 0 || ConstDamageByTargetHP > 0 || Rate > 0f || DamageByTargetMaxHP > 0f;

    /// <summary>The attack at the reader (an &lt;attack&gt; element), read to its end: its first &lt;damage&gt; child's
    /// attributes (the first XmlGetChild, as attack.Element("damage")).</summary>
    internal static SkillAttackDamage Read(XmlReader r)
    {
        SkillAttackDamage d = null;
        if (!r.IsEmptyElement)
        {
            int depth = r.Depth;
            while (SkillDamageTable.Child(r, depth))
            {
                if (d != null || !SkillDamageTable.Is(r, "damage")) continue;
                d = new SkillAttackDamage();
                while (r.MoveToNextAttribute()) d.Set(r.LocalName, r);
                r.MoveToElement();
                if (d.PushEaseType < 0 || d.PushEaseType > 2) d.PushEaseType = 0;
            }
        }
        return d ?? None;
    }

    // XmlGetAttr: a missing attribute leaves the value; only the attributes read here take their value string
    void Set(string name, XmlReader r)
    {
        switch (name)
        {
            case "count": Count = (int)Atoll(r.Value); break;
            case "hand": Hand = (int)Atoll(r.Value); break;
            case "rate": Rate = Float(r.Value); break;
            case "hitSpeedRate": HitSpeedRate = Float(r.Value); break;
            case "hitPauseTime": HitPauseTime = Float(r.Value); break;
            case "damagedVibrateRatio": DamagedVibrateRatio = Float(r.Value); break;
            case "value": Value = Atoll(r.Value); break;
            case "effectRemain": EffectRemain = r.Value; break;
            case "effectHit": EffectHit = r.Value; break;
            case "damageByTargetMaxHP": DamageByTargetMaxHP = Float(r.Value); break;
            case "constDamageByTargetHP": ConstDamageByTargetHP = Atoll(r.Value); break;
            case "push": Push = (int)Atoll(r.Value); break;
            case "pushdistance": PushDistance = Float(r.Value); break;
            case "pushduration": PushDuration = Float(r.Value); break;
            case "pushprob": PushProb = Float(r.Value); break;
            case "pushEaseType": PushEaseType = (int)Atoll(r.Value); break;
            case "pushApplyField": PushApplyField = (int)Atoll(r.Value); break;
            case "pushUpDistance": PushUpDistance = Float(r.Value); break;
            case "pushDown": PushDown = (int)Atoll(r.Value); break;
            case "pushFall": PushFall = (int)Atoll(r.Value); break;
            case "pushPriority": PushPriority = (int)Atoll(r.Value); break;
            case "pushPriorityHitImmune": PushPriorityHitImmune = (int)Atoll(r.Value); break;
            case "pushAnimation": PushAnimation = r.Value; break;
            case "stateHitIgnore": StateHitIgnore = (int)Atoll(r.Value); break;
            case "stateHitOnly": StateHitOnly = (int)Atoll(r.Value); break;
            case "superArmorBreak": SuperArmorBreak = (int)Atoll(r.Value); break;
        }
    }

    static float Float(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

    internal static long Atoll(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = v * 10 + (s[i++] - '0');
        return neg ? -v : v;
    }
}

/// <summary>
/// The attacks' &lt;damage&gt; blocks of every skill, by skill, level, motion and attack: the same files and the same walk
/// as <see cref="SkillDataTable"/> (a skill without &lt;basic&gt; has no levels, the &lt;level&gt; rows by FUN_14028faa0,
/// a level below 1 ends the skill's rows, a later skill with the same id replacing). Read on first use; read only.
/// The files are read with a forward-only reader that keeps the &lt;damage&gt; attributes and the level rows' keys, not a
/// document per file: the field entry reads every skilldata file.
/// </summary>
public sealed class SkillDamageTable
{
    /// <summary>A &lt;level&gt; element as FUN_14028faa0 picks among them: its "value", "feature" and "locale" ("" when
    /// missing) and the attacks of its &lt;motion&gt; children.</summary>
    public sealed class LevelRow
    {
        public string Value = "", Feature = "", Locale = "";
        internal SkillAttackDamage[][] Motions;
    }

    readonly Func<IEnumerable<TextReader>> files;
    readonly Func<List<LevelRow>, IEnumerable<LevelRow>> pick;
    Dictionary<uint, Dictionary<ushort, SkillAttackDamage[][]>> skills;
    readonly object loadLock = new();

    public static readonly SkillDamageTable Empty = new(() => Array.Empty<TextReader>(), rows => rows);

    /// <param name="files">The skilldata files' text, in load order (each reader is disposed when read; null for a missing
    /// file).</param>
    /// <param name="pick">FUN_14028faa0 over a skill's &lt;level&gt; rows, in the elements' order.</param>
    public SkillDamageTable(Func<IEnumerable<TextReader>> files, Func<List<LevelRow>, IEnumerable<LevelRow>> pick)
    {
        this.files = files;
        this.pick = pick;
    }

    /// <summary>Reads the files now (on the calling thread), so the first <see cref="Get"/> finds them read; the field entry
    /// runs it on a worker thread. A reader that comes while it runs waits for it instead of reading them a second time.</summary>
    public void Load() => All();

    static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true, IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    Dictionary<uint, Dictionary<ushort, SkillAttackDamage[][]>> All()
    {
        var read = System.Threading.Volatile.Read(ref skills);
        if (read != null) return read;
        lock (loadLock)
        {
            if (skills != null) return skills;
            var d = new Dictionary<uint, Dictionary<ushort, SkillAttackDamage[][]>>();
            var rows = new List<LevelRow>();
            var motions = new List<SkillAttackDamage[]>();
            var attacks = new List<SkillAttackDamage>();
            foreach (var text in files())
            {
                if (text == null) continue;
                using var r = XmlReader.Create(text, Settings);
                if (r.MoveToContent() != XmlNodeType.Element || !Is(r, "ms2") || r.IsEmptyElement) continue;
                while (Child(r, 0))
                    if (Is(r, "skill")) d[(uint)SkillAttackDamage.Atoll(r.GetAttribute("id") ?? "")] = Levels(r, rows, motions, attacks);
            }
            System.Threading.Volatile.Write(ref skills, d);
            return d;
        }
    }

    /// <summary>The skill at the reader, read to its end (the lists are the read's, reused from skill to skill).</summary>
    Dictionary<ushort, SkillAttackDamage[][]> Levels(XmlReader r, List<LevelRow> rows, List<SkillAttackDamage[]> motions, List<SkillAttackDamage> attacks)
    {
        var levels = new Dictionary<ushort, SkillAttackDamage[][]>();
        if (r.IsEmptyElement) return levels;
        rows.Clear();
        bool basic = false;
        int depth = r.Depth;
        while (Child(r, depth))
        {
            if (Is(r, "basic")) { basic = true; continue; }
            if (!Is(r, "level")) continue;
            var row = new LevelRow { Value = r.GetAttribute("value") ?? "", Feature = r.GetAttribute("feature") ?? "", Locale = r.GetAttribute("locale") ?? "" };
            motions.Clear();
            if (!r.IsEmptyElement)
            {
                int levelDepth = r.Depth;
                while (Child(r, levelDepth))
                {
                    if (!Is(r, "motion")) continue;
                    attacks.Clear();
                    if (!r.IsEmptyElement)
                    {
                        int motionDepth = r.Depth;
                        while (Child(r, motionDepth))
                            if (Is(r, "attack")) attacks.Add(SkillAttackDamage.Read(r));
                    }
                    motions.Add(attacks.ToArray());
                }
            }
            row.Motions = motions.ToArray();
            rows.Add(row);
        }
        if (!basic) return levels;
        foreach (var l in pick(rows))
        {
            var level = (ushort)Xml.Atoi(l.Value);
            if (level < 1) break;
            levels[level] = l.Motions;
        }
        return levels;
    }

    /// <summary>Moves to the next child element of the element at <paramref name="parentDepth"/> (its descendants'
    /// nodes skipped); false at the parent's end.</summary>
    internal static bool Child(XmlReader r, int parentDepth)
    {
        while (r.Read())
        {
            if (r.Depth <= parentDepth) return false;
            if (r.NodeType == XmlNodeType.Element && r.Depth == parentDepth + 1) return true;
        }
        return false;
    }

    /// <summary>An element named <paramref name="name"/> without a namespace (XName <paramref name="name"/>).</summary>
    internal static bool Is(XmlReader r, string name) => r.LocalName == name && r.NamespaceURI.Length == 0;

    /// <summary>
    /// CDamageRecord::GetAttackData (@0x14175a8e0, skillrecord.cpp:501) then m_damage: SkillDataTable::GetSkillData,
    /// SkillData::GetLevelData, SkillLevelData::GetAttack (skillleveldata.cpp:750: GetMotion :742 and
    /// SkillMotionData::GetAttack :1080, null past the rows). Null for an unknown skill, level, motion or attack.
    /// </summary>
    public SkillAttackDamage Get(int skillId, int level, int motion, int attack)
    {
        if (skillId == 0 || level < 0 || level > ushort.MaxValue || motion < 0 || attack < 0) return null;
        if (!All().TryGetValue((uint)skillId, out var levels) || !levels.TryGetValue((ushort)level, out var motions)) return null;
        if (motion >= motions.Length || attack >= motions[motion].Length) return null;
        return motions[motion][attack];
    }

    /// <summary>The skill ids read.</summary>
    public IEnumerable<uint> SkillIds => All().Keys;

    /// <summary>CDamageRecord::Init's test (@0x14175a610, skillrecord.cpp:465): the skill and its level exist.</summary>
    public bool HasLevel(int skillId, int level) =>
        skillId != 0 && level is >= 0 and <= ushort.MaxValue && All().TryGetValue((uint)skillId, out var levels) && levels.ContainsKey((ushort)level);
}

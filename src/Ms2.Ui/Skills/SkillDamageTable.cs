using System.Globalization;
using System.Xml.Linq;

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

    internal static SkillAttackDamage Load(XElement attack)
    {
        var e = attack.Element("damage");
        if (e == null) return None;
        var d = new SkillAttackDamage();
        Int(e, "count", ref d.Count);
        Int(e, "hand", ref d.Hand);
        Float(e, "rate", ref d.Rate);
        Float(e, "hitSpeedRate", ref d.HitSpeedRate);
        Float(e, "hitPauseTime", ref d.HitPauseTime);
        Float(e, "damagedVibrateRatio", ref d.DamagedVibrateRatio);
        Long(e, "value", ref d.Value);
        d.EffectRemain = (string)e.Attribute("effectRemain") ?? "";
        d.EffectHit = (string)e.Attribute("effectHit") ?? "";
        Float(e, "damageByTargetMaxHP", ref d.DamageByTargetMaxHP);
        Long(e, "constDamageByTargetHP", ref d.ConstDamageByTargetHP);
        Int(e, "push", ref d.Push);
        Float(e, "pushdistance", ref d.PushDistance);
        Float(e, "pushduration", ref d.PushDuration);
        Float(e, "pushprob", ref d.PushProb);
        Int(e, "pushEaseType", ref d.PushEaseType);
        if (d.PushEaseType < 0 || d.PushEaseType > 2) d.PushEaseType = 0;
        Int(e, "pushApplyField", ref d.PushApplyField);
        Float(e, "pushUpDistance", ref d.PushUpDistance);
        Int(e, "pushDown", ref d.PushDown);
        Int(e, "pushFall", ref d.PushFall);
        Int(e, "pushPriority", ref d.PushPriority);
        Int(e, "pushPriorityHitImmune", ref d.PushPriorityHitImmune);
        if (e.Attribute("pushAnimation") is { } anim) d.PushAnimation = anim.Value;
        Int(e, "stateHitIgnore", ref d.StateHitIgnore);
        Int(e, "stateHitOnly", ref d.StateHitOnly);
        Int(e, "superArmorBreak", ref d.SuperArmorBreak);
        return d;
    }

    // XmlGetAttr: a missing attribute leaves the value
    static void Int(XElement e, string name, ref int v) { if (e.Attribute(name) is { } a) v = (int)Atoll(a.Value); }
    static void Long(XElement e, string name, ref long v) { if (e.Attribute(name) is { } a) v = Atoll(a.Value); }
    static void Float(XElement e, string name, ref float v)
    {
        if (e.Attribute(name) is { } a) v = float.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
    }

    static long Atoll(string s)
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
/// </summary>
public sealed class SkillDamageTable
{
    readonly Func<IEnumerable<XDocument>> files;
    readonly Func<XElement, string, string, List<XElement>> collect;
    Dictionary<uint, Dictionary<ushort, SkillAttackDamage[][]>> skills;

    public static readonly SkillDamageTable Empty = new(() => Array.Empty<XDocument>(), (_, _, _) => new List<XElement>());

    /// <param name="files">The skilldata documents, in load order.</param>
    /// <param name="collect">FUN_14028faa0 over the &lt;level&gt; elements.</param>
    public SkillDamageTable(Func<IEnumerable<XDocument>> files, Func<XElement, string, string, List<XElement>> collect)
    {
        this.files = files;
        this.collect = collect;
    }

    Dictionary<uint, Dictionary<ushort, SkillAttackDamage[][]>> All()
    {
        if (skills != null) return skills;
        var d = new Dictionary<uint, Dictionary<ushort, SkillAttackDamage[][]>>();
        foreach (var doc in files())
        {
            if (doc?.Root is not { } root || root.Name != "ms2") continue;
            foreach (var e in root.Elements("skill"))
                d[Xml.UInt(e, "id")] = Levels(e);
        }
        return skills = d;
    }

    Dictionary<ushort, SkillAttackDamage[][]> Levels(XElement e)
    {
        var levels = new Dictionary<ushort, SkillAttackDamage[][]>();
        if (e.Element("basic") == null) return levels;
        foreach (var l in collect(e, "level", "value"))
        {
            var level = (ushort)Xml.Int(l, "value");
            var motions = l.Elements("motion").Select(m => m.Elements("attack").Select(SkillAttackDamage.Load).ToArray()).ToArray();
            if (level < 1) break;
            levels[level] = motions;
        }
        return levels;
    }

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

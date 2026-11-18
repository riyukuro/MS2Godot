using System.Xml.Linq;

namespace Ms2.Ui.Skills;

/// <summary>One &lt;level&gt; of a skill (SkillLevelData::Load @0x140804690, commonfiles/skillleveldata.cpp:923): the parts the skill
/// window and its tooltip read.</summary>
public sealed class SkillLevelData
{
    public ushort Level;
    public int Sp, Ep;
    /// <summary>m_nCooldown in ms; GetCooldown (:917) is it times 0.001.</summary>
    public uint CooldownMs;
    public string StringParam = "";

    public float Cooldown => CooldownMs * 0.001f;
}

/// <summary>A &lt;condition&gt;&lt;weapon lh rh&gt; of a skill (SkillCondition::Load @0x1407e6f20, skilloverlapcompare.cpp:554).</summary>
public readonly record struct SkillWeapon(int LeftHand, int RightHand);

/// <summary>One &lt;skill&gt; of skilldata/*.xml (SkillData::Load @0x1407e9130, commonfiles/skilloverlapcompare.cpp:814): the basic
/// block's parts the skill window and its tooltip read, and the levels.</summary>
public sealed class SkillData
{
    public uint Id, OriginIdRaw;
    public int MainType, AttackType, RangeType, Element;
    public string Icon = "";
    /// <summary>m_nTooltip: the skill whose text the tooltip shows instead (ASTooltip::FUN_1409e1c50).</summary>
    public uint Tooltip;
    public ushort MaxLevelRaw;
    public readonly List<SkillWeapon> Weapons = new();
    readonly Dictionary<ushort, SkillLevelData> levels = new();

    /// <summary>
    /// SkillData::Load: id, originID; under &lt;basic&gt; mainType, &lt;ui&gt; icon / attackType / tooltip, &lt;kinds&gt; rangeType /
    /// element, &lt;condition&gt; weapons; then the levels FUN_14028faa0(skill, "level", "value") picks: a level below 1 ends the
    /// load (SkillLevelData::Load returns 0, the skill keeps the levels read so far), and the max level is the highest.
    /// A level's motion and additional effect loads are not read here (they count as loaded).
    /// </summary>
    public static SkillData Load(XElement e, Func<XElement, string, string, List<XElement>> collect)
    {
        var s = new SkillData { Id = Xml.UInt(e, "id"), OriginIdRaw = Xml.UInt(e, "originID") };
        var basic = e.Element("basic");
        if (basic == null) return s;
        s.MainType = Xml.Int(basic, "mainType");
        if (basic.Element("ui") is { } ui)
        {
            s.Icon = (string)ui.Attribute("icon") ?? "";
            s.AttackType = Xml.Int(ui, "attackType");
            s.Tooltip = Xml.UInt(ui, "tooltip");
        }
        if (basic.Element("kinds") is { } kinds)
        {
            s.RangeType = Xml.Int(kinds, "rangeType");
            s.Element = Xml.Int(kinds, "element");
        }
        if (basic.Element("condition") is { } cond)
            foreach (var w in cond.Elements("weapon")) s.Weapons.Add(new SkillWeapon(Xml.Int(w, "lh"), Xml.Int(w, "rh")));
        foreach (var l in collect(e, "level", "value"))
        {
            var lv = new SkillLevelData
            {
                Level = (ushort)Xml.Int(l, "value"),
                Sp = Xml.Int(l, "sp"),
                Ep = Xml.Int(l, "ep"),
                CooldownMs = Xml.UInt(l, "cooldown"),
                StringParam = (string)l.Attribute("stringParam") ?? "",
            };
            if (lv.Level < 1) return s;
            s.levels[lv.Level] = lv;
            s.MaxLevelRaw = Math.Max(s.MaxLevelRaw, lv.Level);
        }
        return s;
    }

    /// <summary>GetLevelData @0x1407e90a0 (:805).</summary>
    public SkillLevelData GetLevelData(int level) => level is >= 0 and <= ushort.MaxValue && levels.TryGetValue((ushort)level, out var l) ? l : null;
    /// <summary>GetOriginId @0x1407e9b50 (:947): originID, else the id.</summary>
    public uint OriginId => OriginIdRaw != 0 ? OriginIdRaw : Id;
    /// <summary>FUN_1407e9de0 (:1053): main type 6 is a passive skill.</summary>
    public bool IsPassive => MainType == 6;
}

/// <summary>
/// SkillDataTable (skilloverlapcompare.cpp:1195): every skilldata/*.xml file (FindSkillFiles: ./Data/Xml/SkillData), each
/// &lt;skill&gt; under &lt;ms2&gt; added by id, a later one replacing (AddSkill :1295). The client loads the files in parallel, so
/// which of two rows with the same id stays is not fixed; here the files go in name order. Read on first use.
/// </summary>
public sealed class SkillDataTable
{
    readonly Func<IEnumerable<XDocument>> files;
    readonly Func<XElement, string, string, List<XElement>> collect;
    Dictionary<uint, SkillData> skills;

    public static readonly SkillDataTable Empty = new(() => Array.Empty<XDocument>(), (_, _, _) => new List<XElement>());

    /// <param name="files">The skilldata documents, in load order.</param>
    /// <param name="collect">FUN_14028faa0 over the &lt;level&gt; elements.</param>
    public SkillDataTable(Func<IEnumerable<XDocument>> files, Func<XElement, string, string, List<XElement>> collect)
    {
        this.files = files;
        this.collect = collect;
    }

    Dictionary<uint, SkillData> All()
    {
        if (skills != null) return skills;
        var d = new Dictionary<uint, SkillData>();
        foreach (var doc in files())        // LoadSkillFile @0x1407ee530 (:1277)
        {
            if (doc?.Root is not { } root || root.Name != "ms2") continue;
            foreach (var e in root.Elements("skill"))
            {
                var s = SkillData.Load(e, collect);
                d[s.Id] = s;
            }
        }
        return skills = d;
    }

    /// <summary>GetSkillData @0x1407ee1d0 (:1233): null for 0 or an unknown id.</summary>
    public SkillData GetSkillData(uint id) => id != 0 && All().TryGetValue(id, out var s) ? s : null;

    /// <summary>GetSkillLevelData @0x1407ee2f0 (:1246).</summary>
    public SkillLevelData GetSkillLevelData(uint id, int level) => GetSkillData(id)?.GetLevelData(level);
}

/// <summary>The job and skill tables together, with the job table's max level cap (SkillData::FUN_1407e9ac0).</summary>
public sealed class SkillTables
{
    public readonly JobTable Jobs;
    public readonly SkillDataTable Skills;

    public static readonly SkillTables Empty = new(JobTable.Empty, SkillDataTable.Empty);

    public SkillTables(JobTable jobs, SkillDataTable skills) { Jobs = jobs ?? JobTable.Empty; Skills = skills ?? SkillDataTable.Empty; }

    public SkillData GetSkillData(uint id) => Skills.GetSkillData(id);

    /// <summary>SkillData::FUN_1407e9ac0 @0x1407e9ac0 (skilloverlapcompare.cpp:923): the job table's maxLevel when set, else the
    /// skill's highest level.</summary>
    public ushort MaxLevel(SkillData s)
    {
        ushort cap = Jobs.GetSkillMaxLevel(s.Id);
        return cap != 0 ? cap : s.MaxLevelRaw;
    }
}

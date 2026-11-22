using System.Globalization;
using System.Numerics;
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
    /// <summary>&lt;level meso&gt; (:928).</summary>
    public int Meso;
    /// <summary>The skill the level belongs to (SkillLevelData(SkillData*); GetSkill / GetSkillId).</summary>
    public SkillData Skill;
    /// <summary>HasSequenceName @0x140804450 (:873): a &lt;motion&gt; of the level has a sequenceName (SkillMotionData::Load :1161).</summary>
    public bool HasSequenceName;
    /// <summary>The level's &lt;motion&gt; rows in file order (m_vMotion, SkillLevelData::Load :951).</summary>
    public readonly List<SkillMotionData> Motions = new();

    public float Cooldown => CooldownMs * 0.001f;

    /// <summary>
    /// GetMotionKey @0x140804c90 (skillleveldata.cpp:984), the cast's cooldown key: 0 for a motion past the level's rows, else
    /// the skill's targeting key (FUN_1407ea030) combined (Unk_HashKey::Combine, :89: h ^= v + 0x9e3779b9 + (h &lt;&lt; 6) + (h &gt;&gt; 2))
    /// with the motion's move key.
    /// </summary>
    public ulong MotionKey(int motion)
    {
        if (Skill == null || motion < 0 || motion >= Motions.Count) return 0;
        ulong h = Skill.TargetingKey;
        h ^= Motions[motion].MoveKey + 0x9e3779b9 + (h << 6) + (h >> 2);
        return h;
    }
}

/// <summary>
/// A &lt;motion&gt; of a skill level (SkillMotionData::Load @0x140808350, skillleveldata.cpp:1158): the parts the cast's cooldown
/// key reads. The ctor (:1006) sets sequenceSpeed 1 and movetype 0.
/// </summary>
public sealed class SkillMotionData
{
    public string SequenceName = "";
    public float SequenceSpeed = 1f;
    /// <summary>"movetype" (:1190), an enum read as a number (XmlValue).</summary>
    public int MoveType;
    /// <summary>"loopCount" (:1196), m_nLoopCount (+0xc8); the ctor (:1011) leaves it 0.</summary>
    public int LoopCount;
    /// <summary>"selfInputCombo" (SkillMotionData::Load @0x140808350, :1165), m_strSelfInputCombo: the text key that opens the
    /// motion's self combo (<see cref="SkillFollow.MayFollow"/>).</summary>
    public string SelfInputCombo = "";

    /// <summary>GetMoveKey @0x140808040 (:1124): (u64)((float)(u64)(movetype &lt;&lt; 10) + sequenceSpeed * 100).</summary>
    public ulong MoveKey => (ulong)((float)(ulong)((long)MoveType << 10) + SequenceSpeed * 100f);

    /// <summary>The motion's &lt;attack&gt; children in order (SkillMotionData::LoadMotion @0x1408080d0, skillleveldata.cpp:1134).</summary>
    public readonly List<SkillAttackData> Attacks = new();
}

/// <summary>
/// An area shape of a skill attack (Unk_RangeShape::Load @0x1408003b0, unit_1408001f0_skillrangeandtarget.cpp:67). The ctor
/// (:39) zeroes every field.
/// </summary>
public class SkillRangeShape
{
    /// <summary>"rangeType": box 1, cylinder 2, frustum 3, hole_cylinder 4; anything else 0.</summary>
    public int Shape;
    /// <summary>"distance" (+0x10), "height" (+0x14), "width" (+0x18), "endWidth" (+0x1c).</summary>
    public float Distance, Height, Width, EndWidth;
    /// <summary>"rangeAdd" (+0x20): the shape's extra size behind and below (the collision's m_ptSize).</summary>
    public Vector3 Add;
    /// <summary>"rangeOffset" (+0x2c): the shape's offset from the caster, turned with the caster.</summary>
    public Vector3 Offset;
    /// <summary>"rangeZRotateDegree" (+0x38).</summary>
    public float ZRotateDegree;

    internal void LoadShape(XElement e)
    {
        Shape = ((string)e.Attribute("rangeType")) switch { "box" => 1, "cylinder" => 2, "frustum" => 3, "hole_cylinder" => 4, _ => 0 };
        Distance = Xml.Float(e, "distance");
        Height = Xml.Float(e, "height");
        Width = Xml.Float(e, "width");
        EndWidth = Xml.Float(e, "endWidth");
        Add = Point(e, "rangeAdd");
        Offset = Point(e, "rangeOffset");
        ZRotateDegree = Xml.Float(e, "rangeZRotateDegree");
    }

    /// <summary>"x,y,z" (FUN_14028f060 @0x14028f060, commonutil.cpp:1172): anything but three numbers is the zero vector.</summary>
    internal static Vector3 Point(XElement e, string name)
    {
        var parts = ((string)e.Attribute(name) ?? "").Split(',');
        if (parts.Length != 3) return Vector3.Zero;
        static float F(string t) => float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
        return new Vector3(F(parts[0]), F(parts[1]), F(parts[2]));
    }
}

/// <summary>
/// An attack's &lt;range&gt; (Unk_RangeProperty::LoadElement @0x140800b50, unit_1408001f0_skillrangeandtarget.cpp:150): the
/// shape, then the target filter. Absent, every field stays 0.
/// STILL-OPEN: targetSelectType's buff / party / guild filter and targetStatCompare are not read.
/// </summary>
public sealed class SkillRange : SkillRangeShape
{
    /// <summary>"includeCaster" (+0x40), "applyTarget" (+0x44), "castTarget" (+0x48).</summary>
    public int IncludeCaster, ApplyTarget, CastTarget;
    /// <summary>"applyTargetIgnoreNpcRanks" (+0x70, :173): one bit per listed npc class, the target query's npc kinds.</summary>
    public int IgnoreNpcRanks;

    internal void LoadElement(XElement attack)
    {
        var r = attack.Element("range");
        if (r == null) return;
        LoadShape(r);
        IncludeCaster = Xml.Int(r, "includeCaster");
        ApplyTarget = Xml.Int(r, "applyTarget");
        CastTarget = Xml.Int(r, "castTarget");
        foreach (var rank in ((string)r.Attribute("applyTargetIgnoreNpcRanks") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(rank.Trim(), out int n)) IgnoreNpcRanks |= 1 << n;
    }
}

/// <summary>An attack's &lt;arrow&gt; (SkillArrow::Load @0x1407df190, skillattackdata.cpp:99): the fields the target pick reads.</summary>
public sealed class SkillArrow
{
    public int Overlap, NonTarget, BounceType, BounceCount;
    public float BounceRadius;
    /// <summary>"bounceOverlap", 1 by default (the ctor, :64).</summary>
    public int BounceOverlap = 1;
    /// <summary>"rayPhysxTest" (:106, row +0x120): non-zero drops the region hits a ray from the caster cannot reach
    /// (CMagic::FUN_141464d00, magicdata.cpp:1773).</summary>
    public int RayPhysxTest;

    /// <summary>IsNonTargetOne @0x1407dfbb0 (:168): nonTarget == 1.</summary>
    public bool IsNonTargetOne => NonTarget == 1;

    internal void Load(XElement attack)
    {
        var a = attack.Element("arrow");
        if (a == null) return;
        Overlap = Xml.Int(a, "overlap");
        NonTarget = Xml.Int(a, "nonTarget");
        BounceType = Xml.Int(a, "bounceType");
        BounceCount = Xml.Int(a, "bounceCount");
        BounceRadius = Xml.Float(a, "bounceRadius");
        if (a.Attribute("bounceOverlap") != null) BounceOverlap = Xml.Int(a, "bounceOverlap");
        RayPhysxTest = Xml.Int(a, "rayPhysxTest");
    }
}

/// <summary>
/// An &lt;attack&gt; of a skill motion (SkillAttackData::Load @0x1407e1a50, skillattackdata.cpp:574): the parts the hit pick
/// reads. The ctor (:490) sets direction 3.
/// </summary>
public sealed class SkillAttackData
{
    /// <summary>"point": the animation key the attack fires at.</summary>
    public string Point = "";
    /// <summary>"targetCount": the most targets one attack hits (CSkillSystem::FUN_141769410 @0x141769410).</summary>
    public int TargetCount;
    /// <summary>"direction": the hit order (CBaseCollision::Detect's sort mode, 0 random .. 6).</summary>
    public int Direction = 3;
    /// <summary>"attackMethodType" (an enum read as a number): 1 keeps the range at the caster (no rangeOffset).</summary>
    public int AttackMethodType;
    /// <summary>"brokenOffence" (m_nBrokenOffence, +0x40, skillattackdata.cpp:584): non-zero lets the attack's tag area hit
    /// tombs (WorldService::FUN_141bc3470).</summary>
    public int BrokenOffence;
    /// <summary>"magicPathID" (m_nMagicId): the magicPath.xml type its magics take (CMagicModel::Init, magicdata.cpp:2614).</summary>
    public uint MagicPathId;
    /// <summary>"pointGroupID" (:576, +0x28): the record's target group; a target already in it is not hit again
    /// (CMagic::FUN_141464d00, magicdata.cpp:1796).</summary>
    public uint PointGroupId;
    /// <summary>"cubeMagicPathID" (:579, +0x34): non-zero makes the magic send the splash (SkillDamage 1/2) at its end and per
    /// piercing interval (magicdata.cpp:1154, :2385).</summary>
    public int CubeMagicPathId;
    public readonly SkillRange Range = new();
    public readonly SkillArrow Arrow = new();

    internal static SkillAttackData Load(XElement e)
    {
        var a = new SkillAttackData { Point = (string)e.Attribute("point") ?? "", TargetCount = Xml.Int(e, "targetCount"), AttackMethodType = Xml.Int(e, "attackMethodType"), BrokenOffence = Xml.Int(e, "brokenOffence"), MagicPathId = Xml.UInt(e, "magicPathID"), PointGroupId = Xml.UInt(e, "pointGroupID"), CubeMagicPathId = Xml.Int(e, "cubeMagicPathID") };
        if (e.Attribute("direction") != null) a.Direction = Xml.Int(e, "direction");
        a.Range.LoadElement(e);
        a.Arrow.Load(e);
        return a;
    }
}

/// <summary>A &lt;condition&gt;&lt;weapon lh rh&gt; of a skill (SkillCondition::Load @0x1407e6f20, skilloverlapcompare.cpp:554).</summary>
public readonly record struct SkillWeapon(int LeftHand, int RightHand);

/// <summary>
/// &lt;basic&gt;&lt;condition&gt; (SkillCondition::Load @0x1407e6f20, skilloverlapcompare.cpp:522); the ctor (:443) leaves every flag 0.
/// The weapons are <see cref="SkillData.Weapons"/>.
/// </summary>
public sealed class SkillCondition
{
    public bool UseCoolDownShowUI, OnlyShadowWorld, OnlyFlyableMap, AllowBattleRidingState, OnlyBattleRidingState;
    public int Sp;
    /// <summary>"defaultRechargingCooldownTime" (:528): the seconds between two uses of a charge skill (CanUseRechargeSkill).</summary>
    public float DefaultRechargingCooldownTime;
    public readonly List<uint> RequireEffect = new(), RequireMapCodes = new();
    public readonly List<int> HasNotAdditionalEffectGroup = new();
}

/// <summary>&lt;basic&gt;&lt;changeSkill&gt; (Unk_SkillBlockB0::Load @0x1408034a0, skillleveldata.cpp:600): the effect checks and the skills
/// they swap in, as comma lists. changeSkillCheckEffectCompareFunc (SkillCompareSign) is padded with 1 (equals) up to the id count;
/// when the levels, overlap counts or compare signs do not match the id count in number, all five lists are emptied.</summary>
public sealed class SkillChange
{
    public readonly List<uint> CheckEffectId = new(), CheckEffectOverlapCount = new(), ChangeSkillId = new();
    public readonly List<ushort> CheckEffectLevel = new();
    public readonly List<int> CheckEffectCompareFunc = new();

    /// <summary>The ChangeSkillId at <paramref name="i"/>, 0 past the list's end.</summary>
    public uint ChangeSkillAt(int i) => i < ChangeSkillId.Count ? ChangeSkillId[i] : 0;
}

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
    public readonly SkillCondition Condition = new();
    public readonly SkillChange Change = new();
    /// <summary>&lt;ui disableChangeSkillIconEffect&gt;.</summary>
    public bool DisableChangeSkillIconEffect;
    /// <summary>&lt;stateAttr rechargeMaxCount cooldownGroupID&gt;.</summary>
    public uint RechargeMaxCount, CooldownGroupId;
    /// <summary>&lt;stateAttr rechargeShareOrigin&gt; (skilloverlapcompare.cpp:870): the charges are kept under the origin skill.</summary>
    public bool RechargeShareOrigin;
    /// <summary>&lt;stateAttr ignoreReduceCooldown&gt; (m_bIgnoreReduceCooldown +0x81, skilloverlapcompare.cpp:868): the own
    /// cast's cooldown keeps out the cooldown-reduce stats (FUN_1417664e0, skillslot.cpp:2288).</summary>
    public bool IgnoreReduceCooldown;
    /// <summary>&lt;stateAttr autoTargeting autoTargetingType moveType keyIsDown useDefaultSkill useDefaultSkillID&gt; (:858-864).</summary>
    public int AutoTargeting, MoveType;
    /// <summary>&lt;kinds jump&gt; (:841): the skill cast instead while jumping up or falling (GetCastSkillId).</summary>
    public uint Jump;
    /// <summary>&lt;kinds continue&gt; (:842), m_bContinue.</summary>
    public bool Continue;
    /// <summary>&lt;kinds unrideOnUse disableWater&gt; (skilloverlapcompare.cpp:846, :848): m_bUnrideOnUse, m_bDisableWater.</summary>
    public bool UnrideOnUseRaw, DisableWater;
    /// <summary>&lt;kinds immediateActive&gt; (:845), m_eImmediateActive read as a number (XmlValue): the chain type a press
    /// of this skill takes in a running skill state (<see cref="SkillFollow.MayFollow"/>).</summary>
    public int ImmediateActive;
    /// <summary>&lt;kinds groupIDs&gt; (m_nGroupIDs, skilloverlapcompare.cpp:850): the skill group the table indexes it under
    /// (SkillDataTable::GetGroupSkills @0x1407ee3b0, :1313).</summary>
    public uint GroupIds;
    /// <summary>
    /// &lt;basic&gt;&lt;combo inputSkill outputSkill comboSkill chargingSkill&gt; (Unk_SkillBlockA4::Load @0x140802ef0,
    /// skillleveldata.cpp:493), loaded from &lt;basic&gt; (skilloverlapcompare.cpp:874).
    /// </summary>
    public uint ComboInputSkill, ComboOutputSkill;
    public bool ComboSkill, ChargingSkill;
    /// <summary>&lt;combo disableChargingAttackSkipFrame&gt; (skillleveldata.cpp:502): a released charging skill does not skip
    /// to its charging_attack key (StateSkill::vfunc_8, stateskill.cpp:705).</summary>
    public bool DisableChargingAttackSkipFrame;
    /// <summary>Unk_SkillBlockA4::FUN_140803040 @0x140803040 (skillleveldata.cpp:524): the output skill, 0 without an input skill.</summary>
    public uint ComboSwapSkill => ComboInputSkill == 0 ? 0 : ComboOutputSkill;
    /// <summary>Unk_SkillBlockA4::GetOutputSkill @0x140802fc0 (skillleveldata.cpp:508): the output skill for the input skill, else
    /// the id.</summary>
    public uint OutputSkill(uint id) => id == ComboInputSkill ? ComboOutputSkill : id;
    public uint AutoTargetingType, UseDefaultSkillId;
    public bool KeyIsDown, UseDefaultSkill;
    /// <summary>&lt;basic&gt;&lt;autoTargeting&gt; (Unk_SkillBlock160::Load @0x140803140, skillleveldata.cpp:551), loaded from &lt;basic&gt; (:877).</summary>
    public float AutoTargetingMaxDegree, AutoTargetingMaxDistance, AutoTargetingMaxHeight;
    public int AutoTargetUseMove;
    /// <summary>&lt;basic&gt;&lt;magicControl enable&gt; (Unk_SkillBlock150::Load @0x140803860, skillleveldata.cpp:653, read as a number):
    /// a magic of a skill with 2 makes no region fill (CMagic::FUN_141461bd0 reads it as m_n538).</summary>
    public int MagicControlEnable;
    readonly Dictionary<ushort, SkillLevelData> levels = new();

    /// <summary>
    /// SkillData::Load: id, originID; under &lt;basic&gt; mainType, &lt;ui&gt; icon / attackType / tooltip, &lt;kinds&gt; rangeType /
    /// element, &lt;condition&gt; weapons; then the levels FUN_14028faa0(skill, "level", "value") picks: a level below 1 ends the
    /// load (SkillLevelData::Load returns 0, the skill keeps the levels read so far), and the max level is the highest.
    /// A level's &lt;motion&gt; rows keep the parts the cooldown key reads; the additional effect loads are not read here (they
    /// count as loaded).
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
            s.DisableChangeSkillIconEffect = Xml.Bool(ui, "disableChangeSkillIconEffect");
        }
        if (basic.Element("kinds") is { } kinds)
        {
            s.RangeType = Xml.Int(kinds, "rangeType");
            s.Element = Xml.Int(kinds, "element");
            s.Jump = Xml.UInt(kinds, "jump");
            s.Continue = Xml.Bool(kinds, "continue");
            s.UnrideOnUseRaw = Xml.Bool(kinds, "unrideOnUse");
            s.DisableWater = Xml.Bool(kinds, "disableWater");
            s.ImmediateActive = Xml.Int(kinds, "immediateActive");
            s.GroupIds = Xml.UInt(kinds, "groupIDs");
        }
        if (basic.Element("combo") is { } combo)
        {
            s.ComboInputSkill = Xml.UInt(combo, "inputSkill");
            s.ComboOutputSkill = Xml.UInt(combo, "outputSkill");
            s.ComboSkill = Xml.Bool(combo, "comboSkill");
            s.ChargingSkill = Xml.Bool(combo, "chargingSkill");
            s.DisableChargingAttackSkipFrame = Xml.Bool(combo, "disableChargingAttackSkipFrame");
        }
        if (basic.Element("stateAttr") is { } state)
        {
            s.RechargeMaxCount = Xml.UInt(state, "rechargeMaxCount");
            s.CooldownGroupId = Xml.UInt(state, "cooldownGroupID");
            s.RechargeShareOrigin = Xml.Bool(state, "rechargeShareOrigin");
            s.IgnoreReduceCooldown = Xml.Bool(state, "ignoreReduceCooldown");
            s.AutoTargeting = Xml.Int(state, "autoTargeting");
            s.AutoTargetingType = Xml.UInt(state, "autoTargetingType");
            s.MoveType = Xml.Int(state, "moveType");
            s.KeyIsDown = Xml.Bool(state, "keyIsDown");
            s.UseDefaultSkill = Xml.Bool(state, "useDefaultSkill");
            s.UseDefaultSkillId = Xml.UInt(state, "useDefaultSkillID");
        }
        if (basic.Element("autoTargeting") is { } at)
        {
            s.AutoTargetingMaxDegree = Xml.Float(at, "autoTargetingMaxDegree");
            s.AutoTargetingMaxDistance = Xml.Float(at, "autoTargetingMaxDistance");
            s.AutoTargetingMaxHeight = Xml.Float(at, "autoTargetingMaxHeight");
            s.AutoTargetUseMove = Xml.Int(at, "autoTargetUseMove");
        }
        if (basic.Element("magicControl") is { } mc) s.MagicControlEnable = Xml.Int(mc, "enable");
        if (basic.Element("changeSkill") is { } ch)
        {
            static List<string> T(XElement e, string n) => Xml.Tokens((string)e.Attribute(n));
            s.Change.CheckEffectId.AddRange(T(ch, "changeSkillCheckEffectID").Select(t => (uint)Xml.Atoi(t)));
            s.Change.CheckEffectLevel.AddRange(T(ch, "changeSkillCheckEffectLevel").Select(t => (ushort)Xml.Atoi(t)));
            s.Change.CheckEffectOverlapCount.AddRange(T(ch, "changeSkillCheckEffectOverlapCount").Select(t => (uint)Xml.Atoi(t)));
            s.Change.ChangeSkillId.AddRange(T(ch, "changeSkillID").Select(t => (uint)Xml.Atoi(t)));
            s.Change.CheckEffectCompareFunc.AddRange(T(ch, "changeSkillCheckEffectCompareFunc").Select(Xml.Atoi));
        }
        var change = s.Change;
        while (change.CheckEffectCompareFunc.Count < change.CheckEffectId.Count) change.CheckEffectCompareFunc.Add(1);
        if (change.CheckEffectLevel.Count != change.CheckEffectId.Count || change.CheckEffectOverlapCount.Count != change.CheckEffectId.Count
            || change.CheckEffectCompareFunc.Count != change.CheckEffectId.Count)
        {
            change.CheckEffectId.Clear(); change.CheckEffectLevel.Clear(); change.CheckEffectOverlapCount.Clear();
            change.CheckEffectCompareFunc.Clear(); change.ChangeSkillId.Clear();
        }
        if (basic.Element("condition") is { } cond)
        {
            var c = s.Condition;
            c.UseCoolDownShowUI = Xml.Bool(cond, "useCoolDownShowUI");
            c.OnlyShadowWorld = Xml.Bool(cond, "onlyShadowWorld");
            c.OnlyFlyableMap = Xml.Bool(cond, "onlyFlyableMap");
            c.AllowBattleRidingState = Xml.Bool(cond, "allowBattleRidingState");
            c.OnlyBattleRidingState = Xml.Bool(cond, "onlyBattleRidingState");
            c.Sp = Xml.Int(cond, "sp");
            c.DefaultRechargingCooldownTime = Xml.Float(cond, "defaultRechargingCooldownTime");
            foreach (var r in cond.Elements("requireEffect")) c.RequireEffect.Add(Xml.UInt(r, "code"));
            foreach (var r in cond.Elements("requireMapCodes")) c.RequireMapCodes.Add(Xml.UInt(r, "code"));
            foreach (var r in cond.Elements("hasNotAdditionalEffectGroup")) c.HasNotAdditionalEffectGroup.Add(Xml.Int(r, "id"));
            foreach (var w in cond.Elements("weapon")) s.Weapons.Add(new SkillWeapon(Xml.Int(w, "lh"), Xml.Int(w, "rh")));
        }
        foreach (var l in collect(e, "level", "value"))
        {
            var lv = new SkillLevelData
            {
                Level = (ushort)Xml.Int(l, "value"),
                Sp = Xml.Int(l, "sp"),
                Ep = Xml.Int(l, "ep"),
                CooldownMs = Xml.UInt(l, "cooldown"),
                StringParam = (string)l.Attribute("stringParam") ?? "",
                Meso = Xml.Int(l, "meso"),
                Skill = s,
                HasSequenceName = l.Elements("motion").Any(m => !string.IsNullOrEmpty((string)m.Attribute("sequenceName"))),
            };
            foreach (var m in l.Elements("motion"))
            {
                var md = new SkillMotionData { SequenceName = (string)m.Attribute("sequenceName") ?? "", MoveType = Xml.Int(m, "movetype"), LoopCount = Xml.Int(m, "loopCount"), SelfInputCombo = (string)m.Attribute("selfInputCombo") ?? "" };
                if (m.Attribute("sequenceSpeed") != null) md.SequenceSpeed = Xml.Float(m, "sequenceSpeed");
                foreach (var a in m.Elements("attack")) md.Attacks.Add(SkillAttackData.Load(a));
                lv.Motions.Add(md);
            }
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
    /// <summary>GetRechargeId @0x1407e9b90 (skilloverlapcompare.cpp:956): the origin id when the charges are shared, else the id.</summary>
    public uint RechargeId => RechargeShareOrigin ? OriginId : Id;
    /// <summary>FUN_1407e9de0 (:1053): main type 6 is a passive skill.</summary>
    public bool IsPassive => MainType == 6;
    /// <summary>IsEmotion @0x1407e9d30 (:1018): main types 14 and 15.</summary>
    public bool IsEmotion => MainType is 14 or 15;
    /// <summary>FUN_1407e9d70 (:1035): main types 3, 5, 7, 10, 17, 18, 19 (the item / ride / pet / glide kinds).</summary>
    public bool IsItemKind => MainType is 3 or 5 or 7 or 10 or 17 or 18 or 19;
    /// <summary>IsUnrideOnUse @0x1407e9e20 (:1060): main types 1, 4, 7, 11-15, 17-19, else unrideOnUse.</summary>
    public bool IsUnrideOnUse => MainType is 1 or 4 or 7 or 11 or 12 or 13 or 14 or 15 or 17 or 18 or 19 || UnrideOnUseRaw;
    /// <summary>FUN_1407e9ef0 (:1097): main types 17, 18, 19.</summary>
    public bool IsShutdownKind => MainType is 17 or 18 or 19;
    /// <summary>FUN_1407e9ea0 (:1082): main types 3, 5, 17, 18.</summary>
    public bool IsItemCountKind => MainType is 3 or 5 or 17 or 18;
    /// <summary>FUN_1407e9bd0 (:965): the action of main types 7, 10, 11, 12, 13 (0x1e glide, 0x26, 0x1c, 0x20, 0x1d climb), else 0.</summary>
    public int ActionKind => MainType switch { 7 => 0x1e, 10 => 0x26, 11 => 0x1c, 12 => 0x20, 13 => 0x1d, _ => 0 };
    /// <summary>
    /// FUN_1407ea030 (skilloverlapcompare.cpp:1156): the autoTargeting block's key (Unk_SkillBlock160::GetKey,
    /// skillleveldata.cpp:571: useMove + maxHeight &lt;&lt; 1 + maxDistance &lt;&lt; 11 + maxDegree &lt;&lt; 22, each float cut to u64), plus
    /// autoTargetingType &lt;&lt; 32, autoTargeting &lt;&lt; 34 and moveType &lt;&lt; 35.
    /// </summary>
    public ulong TargetingKey
    {
        get
        {
            ulong k = (ulong)(long)AutoTargetUseMove;
            k += (ulong)AutoTargetingMaxHeight << 1;
            k += (ulong)AutoTargetingMaxDistance << 11;
            k += (ulong)AutoTargetingMaxDegree << 22;
            k += (ulong)AutoTargetingType << 32;
            k += (ulong)((long)AutoTargeting << 34);
            k += (ulong)((long)MoveType << 35);
            return k;
        }
    }

    /// <summary>GetCooldownKey @0x1407e9c40 (:985): the cooldown group in the high word, the id in the low one.</summary>
    public ulong CooldownKey => ((ulong)CooldownGroupId << 32) | Id;
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

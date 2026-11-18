using System.Globalization;
using Ms2.Quest;

namespace Ms2.Ui.Skills;

/// <summary>What the skill tooltip reads of the local player (ASTooltip's FUN_1409c6540 PC) and of the open skill window.</summary>
public sealed class SkillTooltipPlayer
{
    /// <summary>PC::GetJobSystem; null = no player (the quick slot line and the requirements are left out).</summary>
    public JobSystem Jobs;
    /// <summary>PCBase::vfunc_78, the player's level.</summary>
    public int Level;
    /// <summary>CQuestManagerCommon::IsQuestRewarded.</summary>
    public Func<uint, bool> IsQuestRewarded = _ => false;
    /// <summary>GetQuestName.</summary>
    public Func<uint, string> QuestName = _ => "";
    /// <summary>PC::GetEquipItemLayer0(slot)->GetItemData()->GetEquipSubtype(): 0 when the slot is empty (5 = right hand, 4 = left).</summary>
    public Func<int, int> EquipSubtype = _ => 0;
    /// <summary>CKeyTable::FindQuickSlotBySkillAndItemUid(id, 0) @0x14142e090: the slot, -1 when the skill is on none.</summary>
    public Func<uint, int> QuickSlotOf = _ => -1;
    /// <summary>The skill book window's simulator (FUN_1409eb120(0x4f2d)->GetSimulator()); null when it is closed.</summary>
    public SkillPointSimulator DialogSimulator;
}

/// <summary>
/// ASTooltip::FUN_1409e1c50 @0x1409e1c50 (client/ms2/astooltip.cpp:4875): the skill tooltip ("export_common_tooltip_kindsSkill")
/// and its parts, SetSkillIconPath (:3910), SetSkillName (:3885), SetSkillElement (:3893), FUN_1409dbfa0 (:3921),
/// SetSkillDescription (:3943), FUN_1409dc1f0 (:3950), FUN_1409dcf10 (:4059) and FUN_1409dc700 (:3984).
/// </summary>
public static class SkillTooltip
{
    const string Gray = "aaaaaa", Yellow = "ffd400", Red = "ff4040";

    /// <param name="flags">The ASTooltip flags (m_48): 0x40 or 2 aligns 7, else 5; 0x100 keeps enablePuton unset.</param>
    /// <param name="fromDialog">The third argument: 1 when the skill book window asks (its simulator's edit skills count).</param>
    public static AsTooltip Build(GameTables tables, SkillTooltipPlayer player, uint skillId, int level, int flags, bool fromDialog)
    {
        var t = tables.Skills;
        var skill = t.GetSkillData(skillId);
        if (skill == null) return null;
        uint origId = skillId;
        // m_nTooltip: another skill's text at this level, else at level 1; with g_nDebugUI the name gets "<-[id]" (off here)
        if (skill.Tooltip != 0)
        {
            int tipLevel = level;
            var tl = t.Skills.GetSkillLevelData(skill.Tooltip, tipLevel);
            if (tl == null) { tl = t.Skills.GetSkillLevelData(skill.Tooltip, 1); tipLevel = 1; }
            if (tl != null) { skillId = skill.Tooltip; level = tipLevel; }
        }
        // the level data is checked on the skill the window asked for, at the (possibly redirected) level
        if (skill.GetLevelData(level) == null) return null;
        var o = new AsTooltip { ["tooltip"] = "export_common_tooltip_kindsSkill" };
        if (t.GetSkillData(origId) is { } icon) o["iconPath"] = icon.Icon;
        o["skillName"] = tables.SkillName(skillId);
        SetSkillElement(o, tables, skillId);
        SetQuickSlotLine(o, tables, player, origId, flags);
        o["skillDescription"] = Description(tables, skillId, level);
        SetStat(o, tables, skillId, level);
        SetRequirements(o, tables, player, skillId, level, fromDialog);
        SetExtra(o, tables, player, skillId, level);
        o.SetTooltipAlign((flags & 0x40) != 0 || (flags & 2) != 0 ? 7 : 5);
        return o;
    }

    static string Common(GameTables tables, string key) => tables.Common(key) ?? "";
    static string Fmt(GameTables tables, string key, params object[] args) => ClientFormat.Format(Common(tables, key), args);

    /// <summary>SetSkillElement (:3893): elements 1-6 (fire, ice, lightning, holy, darkness, poison) get { type, text }; 0 and 7
    /// get { type, "" }; 8 and up nothing.</summary>
    static void SetSkillElement(AsTooltip o, GameTables tables, uint id)
    {
        if (tables.Skills.GetSkillData(id) is not { } s) return;
        string[] keys = { null, "s_word_element_fire", "s_word_element_ice", "s_word_element_lightning", "s_word_element_holy",
                          "s_word_element_darkness", "s_word_element_poison", null };     // GetStringCommon 0x5c4-0x5c9
        if ((uint)s.Element >= 8) return;
        o["skillElement"] = new Dictionary<string, object>
        {
            ["type"] = (uint)s.Element,
            ["text"] = keys[s.Element] is { } k ? Common(tables, k) : "",
        };
    }

    /// <summary>FUN_1409dbfa0 (:3921): a learnt active skill shows "Equipped" (itemEquip, 0x58a) when on a quick slot, else the
    /// right-click hint (enablePuton, 0x75a).</summary>
    static void SetQuickSlotLine(AsTooltip o, GameTables tables, SkillTooltipPlayer player, uint id, int flags)
    {
        if (player?.Jobs == null) return;
        if (tables.Skills.GetSkillData(id) is not { } s || s.IsPassive) return;
        if (!player.Jobs.IsLearned(id)) return;
        if (player.QuickSlotOf(id) != -1) o["itemEquip"] = Common(tables, "s_word_equiped");
        else if ((flags & 0x100) == 0) o["enablePuton"] = Common(tables, "s_msg_rclick_puton_qslot");
    }

    /// <summary>
    /// SetSkillDescription (:3943) over CStringTable::GetSkillDescription (stringtable.cpp:750): the "name" text of the skill's
    /// stringSkillDescription.xml row ("\n" as a line break, FUN_141815650), "" without a row, skill data or level data; a level's
    /// stringParam fills it as comma separated string arguments (FUN_141813790). The "$" reserved words of the text
    /// (CReservedWordTable::ReplaceObjectWords) are not replaced here.
    /// </summary>
    public static string Description(GameTables tables, uint id, int level)
    {
        var text = tables.SkillDescriptionText(id);
        if (text == null) return "";
        text = text.Replace("\\n", "\n");
        var lv = tables.Skills.Skills.GetSkillLevelData(id, level);
        if (lv == null) return "";
        if (lv.StringParam.Length > 0)
            text = ClientFormat.Format(text, lv.StringParam.Split(',').Cast<object>().ToArray());
        return text;
    }

    /// <summary>FUN_1409dc1f0 (:3950): statDescription { type 1, level "Level n" (+ 0x79b at the max level), statSP / statEP
    /// "&lt;name&gt; n" when above 0, cooltime when the whole seconds are above 0 }.</summary>
    static void SetStat(AsTooltip o, GameTables tables, uint id, int level)
    {
        var s = tables.Skills.GetSkillData(id);
        var lv = s?.GetLevelData(level);
        if (lv == null) return;
        string levelText = Fmt(tables, "s_word_level", level);
        if (tables.Skills.MaxLevel(s) == level) levelText += Common(tables, "s_maid_affinity_gauge_level_max");
        int cool = (int)lv.Cooldown;
        o["statDescription"] = new Dictionary<string, object>
        {
            ["type"] = 1,
            ["level"] = levelText,
            ["statSP"] = lv.Sp > 0 ? $"{Common(tables, "s_word_stat_sp")} {lv.Sp.ToString(CultureInfo.InvariantCulture)}" : "",
            ["statEP"] = lv.Ep > 0 ? $"{Common(tables, "s_word_stat_ep")} {lv.Ep.ToString(CultureInfo.InvariantCulture)}" : "",
            ["cooltime"] = cool > 0 ? Fmt(tables, "s_word_cooltime", cool) : "",
        };
    }

    static Dictionary<string, object> Row(string text, string color) => new() { ["text"] = text, ["color"] = color };

    /// <summary>
    /// FUN_1409dcf10 (:4059): the job's row of the skill (else of its origin skill); nothing when it has no requirement. Then
    /// "[Requirements]", the player level the level needs, each required skill (from the window's simulator when it asks and
    /// is open, else the player's book; a skill neither has is skipped) and the required quest, yellow when met and red when not.
    /// </summary>
    static void SetRequirements(AsTooltip o, GameTables tables, SkillTooltipPlayer player, uint id, int level, bool fromDialog)
    {
        if (tables.Skills.GetSkillData(id) is not { } s) return;
        var job = player?.Jobs?.Job;
        if (job == null) return;
        var req = job.FindSkill(id) ?? job.FindSkill(s.OriginId);
        if (req == null || !req.HasRequirement) return;
        var rows = new List<object> { Row(Common(tables, "s_word_learn_require_condition"), Gray) };
        ushort needLevel = req.GetRequireLevel(level);
        if (needLevel > 0)
            rows.Add(Row(Fmt(tables, "s_word_require_player_level", needLevel), (ushort)player.Level >= needLevel ? Yellow : Red));
        var sim = fromDialog ? player.DialogSimulator : null;
        for (int i = 0; i < req.RequireSkillIds.Count; i++)
        {
            var js = sim != null ? sim.GetEditSkill(req.RequireSkillIds[i]) : player.Jobs.FindSkill(req.RequireSkillIds[i]);
            if (js == null) continue;
            ushort need = req.RequireSkillLevel(i);
            bool ok = js.Learned && js.Level >= need;
            string text = Fmt(tables, "s_word_require_skill_level", tables.SkillName(js.Id), need);
            // PC::GetSkillTree()->GetSkillLevel: the skill's tier option level
            if (!ok && player.Jobs.TierLevel(js.Id) > 0)
                text = Fmt(tables, "s_word_require_skill_level_without_leveloption", text);
            rows.Add(Row(text, ok ? Yellow : Red));
        }
        if (req.RequireQuestId != 0)
            rows.Add(Row(Fmt(tables, "s_word_require_quest_completed", player.QuestName(req.RequireQuestId) ?? ""),
                player.IsQuestRewarded(req.RequireQuestId) ? Yellow : Red));
        o["requireDescription"] = rows;
    }

    /// <summary>
    /// FUN_1409dc700 (:3984): Active / Passive (0x5c1 / 0x5c2); range (close 0x597, long 0x598) and attack type (physical 0x5c3,
    /// magic 0x596) joined by " / "; the weapons the skill needs (FUN_1409e6d00 :5543, slot names of table 8 in
    /// "Weapon Required: {0}", 0x584), gray when the right or left hand holds one of them, else red.
    /// </summary>
    static void SetExtra(AsTooltip o, GameTables tables, SkillTooltipPlayer player, uint id, int level)
    {
        var s = tables.Skills.GetSkillData(id);
        if (s?.GetLevelData(level) == null) return;
        var rows = new List<object>();
        string kind = Common(tables, s.IsPassive ? "s_word_skill_passive" : "s_word_skill_active");
        if (kind.Length > 0) rows.Add(Row(kind, Gray));
        string[] range = { null, "s_word_nearrange", "s_word_longrange" }, attack = { null, "s_word_element_physics", "s_word_magic" };
        string line = "";
        if ((uint)s.RangeType < 3 && range[s.RangeType] is { } rk) line += Common(tables, rk);
        if ((uint)s.AttackType < 3 && attack[s.AttackType] is { } ak) line += line.Length == 0 ? Common(tables, ak) : " / " + Common(tables, ak);
        if (line.Length > 0) rows.Add(Row(line, Gray));
        // The hands are not fed yet (EquipSubtype is 0 for both), so the colour is only the client's for empty hands: then any
        // entry with a one-sided hand (lh or rh 0) counts as allowed, as FUN_1409e6d00 does with no weapon equipped.
        int right = player?.EquipSubtype(5) ?? 0, left = player?.EquipSubtype(4) ?? 0;
        var (allowed, weapons) = Weapons(tables, right, left, s.Weapons);
        if (weapons.Length > 0) rows.Add(Row(weapons, allowed ? Gray : Red));
        o["extraDescription"] = rows;
    }

    /// <summary>FUN_1409e6d00 @0x1409e6d00 (astooltip.cpp:5543): allowed when a weapon's right hand kind is the right hand's or its
    /// left hand kind the left hand's (an empty hand is 0, so a one-sided entry matches it); the kinds' names, ", " between.</summary>
    public static (bool Allowed, string Text) Weapons(GameTables tables, int right, int left, IEnumerable<SkillWeapon> weapons)
    {
        bool allowed = false;
        string text = "", sep = "";
        foreach (var w in weapons)
        {
            if (w.RightHand == right || w.LeftHand == left) allowed = true;
            if (w.RightHand != 0) { text += sep + tables.SlotName(w.RightHand); sep = ", "; }
            if (w.LeftHand != 0) { text += sep + tables.SlotName(w.LeftHand); sep = ", "; }
        }
        if (text.Length > 0) text = ClientFormat.Format(Common(tables, "s_word_needs"), text);
        return (allowed, text);
    }
}

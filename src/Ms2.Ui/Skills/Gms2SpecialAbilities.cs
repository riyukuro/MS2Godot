using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Ui.Skills;

/// <summary>
/// The special abilities of a GMS2 additionaleffect/&lt;id&gt;.xml level as the KMS &lt;SpecialAbility&gt;&lt;v type value/&gt; list
/// (see <see cref="AdditionalLevelData.SpecialAbilities"/>), and the special ability rows of a pack without
/// table/specialAbilityStringTable.xml (the GMS2 data has none).
/// INFERRED (the GMS2 exe is not decompiled): the rule below is read from the KMS2_DEV data, which has both forms (additional/
/// and additionaleffect/). Built over its levels that are in both, the rule gives the KMS list exactly for 13,642 of 13,963 (feature-enabled); the
/// rest differ in their values or in which abilities they have (a retuned skill), not in a name or a scale.
/// <list type="bullet">
/// <item>StatusProperty/Stat and StatusProperty/SpecialAbility: one attribute per ability. "&lt;name&gt;value" is the ability of
/// that name (a flat one, valueType 1) at the value; "&lt;name&gt;rate" is a rate one (valueType 2) at the value times 1000, the
/// per-mille int the KMS list has. A Stat rate is "&lt;name&gt;Rate" when the table has it (msprate: mspRate), else the name
/// itself (penrate: pen). The names match case-insensitively and without underscores (sp_invrate: spInvRate).</item>
/// <item>The weapon attack rate is split in two (wapminrate, wapmaxrate) where the KMS list has one wapRate; the min one is
/// taken (in the GMS2 data 3 of 314 levels have them differ).</item>
/// <item>OffensiveProperty / DefensiveProperty papDamageR and mapDamageR are offensive_ / defensive_ physicaldamage and
/// magicaldamage (times 1000); DefensiveProperty papDamageV and mapDamageV (both the same in the data) are defensive_damageV.</item>
/// </list>
/// STILL-OPEN: the GMS2 attributes with no KMS ability (improve_socket_unlock_probabilityvalue,
/// improve_survival_kill_exprate, improve_survival_time_exprate, sp_invvalue, penvalue; 38 levels) are dropped.
/// </summary>
public static class Gms2SpecialAbilities
{
    /// <summary>
    /// STAND-IN (STILL-OPEN): rows 1-206 (index + 1 is the id) of the KMS2_DEV table/specialAbilityStringTable.xml (option name,
    /// valueType), the same rows as the KMS2 data's (which adds a 207th, past the 0xce LoadSpecialAbilityStringTable keeps).
    /// The GMS2 data has no such table, so the ability types of its effects are given these ids.
    /// </summary>
    public static readonly (string Option, int ValueType)[] DefaultRows =
    {
        ("seg", 2), ("smd", 2), ("sss", 2), ("dashDistance", 1), ("finalAttackPower", 2), ("wapRate", 2), ("finalAdditionalDamage", 2),
        ("cri", 2), ("sgi", 2), ("atpToBap", 2), ("sgi_elite", 2), ("sgi_boss", 2), ("killHPRestore", 1), ("killSPRestore", 1),
        ("killEPRestore", 1), ("heal", 2), ("receivedhealincrease", 2), ("iceDamage", 2), ("fireDamage", 2), ("darkDamage", 2),
        ("lightDamage", 2), ("poisonDamage", 2), ("thunderDamage", 2), ("nddincrease", 2), ("lddincrease", 2), ("parpen", 2),
        ("marpen", 2), ("icedamagereduce", 2), ("firedamagereduce", 2), ("darkdamagereduce", 2), ("lightdamagereduce", 2),
        ("poisondamagereduce", 2), ("thunderdamagereduce", 2), ("stunReduce", 2), ("skillCooldown", 2), ("conditionReduce", 2),
        ("nearDistanceDamageReduce", 2), ("longDistanceDamageReduce", 2), ("KnockBackReduce", 1), ("strRate", 2), ("dexRate", 2),
        ("intRate", 2), ("lukRate", 2), ("hpRate", 2), ("hpInvRate", 2), ("spRate", 2), ("spInvRate", 2), ("npcKillDropItemIncRate", 2),
        ("seg_questReward", 2), ("epRate", 2), ("epInvRate", 2), ("aspRate", 2), ("mspRate", 2), ("pvpdamageincrease", 1),
        ("pvpdamagereduce", 1), ("improveguildexp", 2), ("improveguildcoin", 2), ("improvemassiveeventbexpball", 2),
        ("seg_fishingReward", 2), ("seg_arcadeReward", 2), ("seg_playinstrumentReward", 2), ("improve_maid_mood", 2),
        ("reduce_maid_recipe", 2), ("reduce_meso_trade_fee", 2), ("reduce_enchant_matrial_fee", 2), ("reduce_merat_revival_fee", 2),
        ("improve_mining_reward_item", 2), ("improve_breeding_reward_item", 2), ("improve_blacksmithing_reward_mastery", 2),
        ("improve_engraving_reward_mastery", 2), ("improve_gathering_reward_item", 2), ("improve_farming_reward_item", 2),
        ("improve_alchemist_reward_mastery", 2), ("improve_cooking_reward_mastery", 2), ("improve_acquire_gathering_exp", 2),
        ("improve_acquire_manufacturing_exp", 2), ("skill_levelup_tier_1", 1), ("skill_levelup_tier_2", 1), ("skill_levelup_tier_3", 1),
        ("skill_levelup_tier_4", 1), ("skill_levelup_tier_5", 1), ("skill_levelup_tier_6", 1), ("skill_levelup_tier_7", 1),
        ("skill_levelup_tier_8", 1), ("skill_levelup_tier_9", 1), ("skill_levelup_tier_10", 1), ("skill_levelup_tier_11", 1),
        ("skill_levelup_tier_12", 1), ("skill_levelup_tier_13", 1), ("skill_levelup_tier_14", 1), ("improve_massive_ox_exp", 2),
        ("improve_massive_trapmaster_exp", 2), ("improve_massive_finalsurvival_exp", 2), ("improve_massive_crazyrunner_exp", 2),
        ("improve_massive_escape_exp", 2), ("improve_massive_springbeach_exp", 2), ("improve_massive_dancedance_exp", 2),
        ("improve_massive_ox_msp", 1), ("improve_massive_trapmaster_msp", 1), ("improve_massive_finalsurvival_msp", 1),
        ("improve_massive_crazyrunner_msp", 1), ("improve_massive_escape_msp", 1), ("improve_massive_springbeach_msp", 1),
        ("improve_massive_dancedance_msp", 1), ("npc_hit_reward_sp_ball", 2), ("npc_hit_reward_ep_ball", 2), ("improve_honor_token", 2),
        ("improve_pvp_exp", 2), ("improve_darkstream_damage", 2), ("reduce_darkstream_recive_damage", 2), ("strToInt", 2),
        ("fishing_double_mastery", 2), ("playinstrument_double_mastery", 2), ("complete_fieldmission_msp", 1),
        ("improve_glide_vertical_velocity", 2), ("rmspRate", 2), ("additionaleffect_95000012", 1), ("additionaleffect_95000014", 1),
        ("atpRate", 2), ("evpRate", 2), ("capRate", 2), ("carRate", 2), ("nddRate", 2), ("jmpRate", 2), ("papRate", 2), ("mapRate", 2),
        ("parRate", 2), ("marRate", 2), ("reduce_recovery_ep_inv", 2), ("improve_stat_wap_u", 2), ("mining_double_reward", 2),
        ("breeding_double_reward", 2), ("gathering_double_reward", 2), ("farming_double_reward", 2), ("blacksmithing_double_reward", 2),
        ("engraving_double_reward", 2), ("alchemist_double_reward", 2), ("cooking_double_reward", 2), ("mining_double_mastery", 2),
        ("breeding_double_mastery", 2), ("gathering_double_mastery", 2), ("farming_double_mastery", 2),
        ("blacksmithing_double_mastery", 2), ("engraving_double_mastery", 2), ("alchemist_double_mastery", 2),
        ("cooking_double_mastery", 2), ("improve_chaosraid_wap", 2), ("improve_chaosraid_asp", 1), ("improve_chaosraid_atp", 1),
        ("improve_chaosraid_hp", 1), ("improve_recovery_ball", 2), ("str", 1), ("dex", 1), ("int", 1), ("luk", 1),
        ("improve_pettrap_reward", 1), ("mining_multiaction", 2), ("breeding_multiaction", 2), ("gathering_multiaction", 2),
        ("farming_multiaction", 2), ("improve_massive_sh_crazyrunner_exp", 2), ("improve_massive_sh_crazyrunner_msp", 1),
        ("reduce_damage_by_targetmaxhp", 2), ("reduce_meso_revival_fee", 2), ("improve_riding_run_speed", 2),
        ("improve_dungeon_reward_meso", 2), ("improve_shop_buying_meso", 2), ("improve_itembox_reward_meso", 2),
        ("reduce_remakeoption_fee", 2), ("reduce_airtaxi_fee", 2), ("defensive_damageV", 1), ("reduce_gemstone_upgrade_fee", 2),
        ("reduce_pet_remakeoption_fee", 2), ("improve_riding_speed", 2), ("defensive_physicaldamage", 2), ("defensive_magicaldamage", 2),
        ("offensive_physicaldamage", 2), ("offensive_magicaldamage", 2), ("reduce_gameitem_socket_unlock_fee", 2), ("hp", 1),
        ("hp_rgp", 1), ("sp", 1), ("sp_rgp", 1), ("ep", 1), ("ep_inv", 1), ("asp", 1), ("msp", 1), ("atp", 1), ("evp", 1), ("cap", 1),
        ("cad", 1), ("car", 1), ("ndd", 1), ("abp", 2), ("pap", 1), ("map", 1), ("par", 1), ("mar", 1), ("pen", 2), ("rmsp", 1),
        ("bap", 1), ("cap_ignore_limit", 1), ("cad_ignore_limit", 1), ("aevp", 2), ("cap_ignore_limit_extra", 2), ("reduce_do_heal_hp", 2),
    };

    /// <summary>
    /// STAND-IN (STILL-OPEN): the special abilities whose name the GMS2 data has under the common string s_item_opt_sa_&lt;stem&gt;
    /// (the GMS2 data has no table/specialAbilityStringTable.xml and no stringspecialabilityname.xml, so <see cref="Ms2.Ui.GameTables.SpecialAbilityName"/>
    /// had no text for any ability). INFERRED from the KMS2 data, which has both: an ability's name text (its row's
    /// commonStringID in string/&lt;lang&gt;/stringspecialabilityname.xml) equals the text of exactly one s_item_opt_sa_&lt;stem&gt;
    /// key of string/&lt;lang&gt;/stringcommon.xml, the same key in the cn and the kr strings, for these 130 of the 207 rows (no
    /// row matches a different key in the two languages; the other 77 match none or several). Each key is in the GMS2 English
    /// strings. The GMS2 exe is not decompiled, so how it names an ability is not seen.
    /// </summary>
    public static readonly (int Id, string Stem)[] LegacyNameKeys =
    {
        (1, "improve_acquire_exp"), (2, "improve_acquire_meso"), (3, "improve_speed_swim"), (4, "improve_speed_dash"), (7, "improve_damage_final"),
        (8, "improve_damage_critical"), (9, "improve_damage_normalNpc"), (11, "improve_damage_namedNpc"), (13, "improve_recovery_hp_dokill"),
        (14, "improve_recovery_sp_dokill"), (15, "improve_recovery_ep_dokill"), (16, "improve_recovery_regen_doheal"),
        (17, "improve_recovery_regen_receiveheal"), (18, "improve_elements_ice"), (19, "improve_elements_fire"), (20, "improve_elements_dark"),
        (21, "improve_elements_light"), (22, "improve_elements_poison"), (23, "improve_elements_thunder"), (24, "improve_damage_nearrange"),
        (25, "improve_damage_longrange"), (26, "improve_piercing_par"), (27, "improve_piercing_mar"), (28, "reduce_elements_ice"),
        (29, "reduce_elements_fire"), (30, "reduce_elements_dark"), (31, "reduce_elements_light"), (32, "reduce_elements_poison"),
        (33, "reduce_elements_thunder"), (34, "reduce_time_stun"), (35, "reduce_time_cooldown"), (36, "reduce_time_condition"),
        (37, "reduce_damage_nearrange"), (38, "reduce_damage_longrange"), (39, "reduce_distance_knockBack"),
        (48, "improve_npckill_dropitem_incrate"), (49, "improve_acquire_questreward_exp"), (54, "improve_damage_pvp"), (55, "reduce_damage_pvp"),
        (56, "improve_guild_exp"), (57, "improve_guild_coin"), (58, "improve_massive_event_expball"), (59, "improve_acquire_fishing_exp"),
        (60, "improve_acquire_arcade_exp"), (61, "improve_acquire_playinstrument_exp"), (62, "improve_maid_mood"), (63, "reduce_maid_recipe"),
        (64, "reduce_meso_trade_fee"), (65, "reduce_enchant_material_fee"), (66, "reduce_merat_revival_fee"), (67, "improve_mining_reward_item"),
        (68, "improve_breeding_reward_item"), (69, "improve_blacksmithing_reward_mastery"), (70, "improve_engraving_reward_mastery"),
        (71, "improve_gathering_reward_item"), (72, "improve_farming_reward_item"), (73, "improve_alchemist_reward_mastery"),
        (74, "improve_cooking_reward_mastery"), (75, "improve_acquire_gathering_exp"), (76, "improve_acquire_manufacturing_exp"),
        (77, "skill_levelup_tier_1"), (78, "skill_levelup_tier_2"), (79, "skill_levelup_tier_3"), (80, "skill_levelup_tier_4"),
        (81, "skill_levelup_tier_5"), (82, "skill_levelup_tier_6"), (83, "skill_levelup_tier_7"), (84, "skill_levelup_tier_8"),
        (85, "skill_levelup_tier_9"), (86, "skill_levelup_tier_10"), (87, "skill_levelup_tier_11"), (88, "skill_levelup_tier_12"),
        (89, "skill_levelup_tier_13"), (90, "skill_levelup_tier_14"), (91, "improve_massive_ox_exp"), (92, "improve_massive_trapmaster_exp"),
        (93, "improve_massive_finalsurvival_exp"), (94, "improve_massive_crazyrunner_exp"), (95, "improve_massive_escape_exp"),
        (96, "improve_massive_springbeach_exp"), (97, "improve_massive_dancedance_exp"), (98, "improve_massive_ox_msp"),
        (99, "improve_massive_trapmaster_msp"), (100, "improve_massive_finalsurvival_msp"), (101, "improve_massive_crazyrunner_msp"),
        (102, "improve_massive_escape_msp"), (103, "improve_massive_springbeach_msp"), (104, "improve_massive_dancedance_msp"),
        (105, "npc_hit_reward_sp_ball"), (106, "npc_hit_reward_ep_ball"), (107, "improve_honor_token"), (108, "improve_pvp_exp"),
        (109, "improve_darkstream_damage"), (110, "reduce_darkstream_recive_damage"), (112, "fishing_double_mastery"),
        (113, "playinstrument_double_mastery"), (114, "complete_fieldmission_msp"), (115, "improve_glide_vertical_velocity"),
        (117, "additionaleffect_95000012"), (118, "additionaleffect_95000014"), (130, "improve_stat_wap_u"), (131, "mining_double_reward"),
        (132, "breeding_double_reward"), (133, "gathering_double_reward"), (134, "farming_double_reward"), (135, "blacksmithing_double_reward"),
        (136, "engraving_double_reward"), (137, "alchemist_double_reward"), (138, "cooking_double_reward"), (139, "mining_double_mastery"),
        (140, "breeding_double_mastery"), (141, "gathering_double_mastery"), (142, "farming_double_mastery"), (143, "blacksmithing_double_mastery"),
        (144, "engraving_double_mastery"), (145, "alchemist_double_mastery"), (146, "cooking_double_mastery"), (147, "improve_chaosraid_wap"),
        (148, "improve_chaosraid_asp"), (149, "improve_chaosraid_atp"), (150, "improve_chaosraid_hp"), (151, "improve_recovery_ball"),
        (156, "improve_pettrap_reward"), (157, "ming_multiaction"), (158, "breeding_multiaction"), (159, "gathering_multiaction"),
        (160, "farming_multiaction"), (161, "improve_massive_sh_crazyrunner_exp"), (162, "improve_massive_sh_crazyrunner_msp"),
        (163, "reduce_damage_by_targetmaxhp")
    };

    /// <summary>The names of <see cref="LegacyNameKeys"/> from a pack's common strings (a missing or empty one left out).</summary>
    public static Dictionary<int, string> LegacyNames(IReadOnlyDictionary<string, string> common)
    {
        var d = new Dictionary<int, string>();
        foreach (var (id, stem) in LegacyNameKeys)
            if (common.TryGetValue("s_item_opt_sa_" + stem, out var n) && !string.IsNullOrEmpty(n)) d[id] = n;
        return d;
    }

    /// <summary><see cref="DefaultRows"/> as the table's &lt;ms2&gt;&lt;key id option valueType/&gt; document.</summary>
    public static XDocument DefaultTable() => new(new XElement("ms2", DefaultRows.Select((r, i) => new XElement("key",
        new XAttribute("id", i + 1), new XAttribute("option", r.Option), new XAttribute("valueType", r.ValueType)))));

    static Dictionary<string, (string Option, int ValueType)> byKey;
    static string Key(string name) => name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

    static string Option(string name, int valueType)
    {
        if (byKey == null)
        {
            var d = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
            foreach (var r in DefaultRows) d.TryAdd(Key(r.Option), r);
            byKey = d;
        }
        return byKey.TryGetValue(Key(name), out var r2) && r2.ValueType == valueType ? r2.Option : null;
    }

    static double Num(XElement e, string name) =>
        double.TryParse((string)e?.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    /// <summary>The level's abilities as a &lt;SpecialAbility&gt; of &lt;v type value/&gt;, null when it has none.</summary>
    public static XElement FromLevel(XElement level)
    {
        var list = new List<(string Type, int Value)>();
        void Add(string type, double value)
        {
            if (type == null || value == 0) return;
            int i = list.FindIndex(v => v.Type == type);
            if (i >= 0) list[i] = (type, (int)Math.Round(value)); else list.Add((type, (int)Math.Round(value)));
        }
        var status = level.Element("StatusProperty");
        foreach (var group in new[] { "Stat", "SpecialAbility" })
            foreach (var a in status?.Element(group)?.Attributes() ?? Enumerable.Empty<XAttribute>())
            {
                string n = a.Name.LocalName;
                double v = Num(a.Parent, n);
                if (v == 0) continue;
                if (n is "wapminrate" or "wapmaxrate")
                {
                    if (n == "wapminrate") Add("wapRate", v * 1000);
                }
                else if (n.EndsWith("value", StringComparison.Ordinal)) Add(Option(n[..^5], 1), v);
                else if (n.EndsWith("rate", StringComparison.Ordinal))
                {
                    string b = n[..^4];
                    Add((group == "Stat" ? Option(b + "rate", 2) : null) ?? Option(b, 2), v * 1000);
                }
            }
        var off = level.Element("OffensiveProperty");
        Add("offensive_physicaldamage", Num(off, "papDamageR") * 1000);
        Add("offensive_magicaldamage", Num(off, "mapDamageR") * 1000);
        var def = level.Element("DefensiveProperty");
        Add("defensive_physicaldamage", Num(def, "papDamageR") * 1000);
        Add("defensive_magicaldamage", Num(def, "mapDamageR") * 1000);
        Add("defensive_damageV", Num(def, "papDamageV") != 0 ? Num(def, "papDamageV") : Num(def, "mapDamageV"));
        return list.Count == 0 ? null
            : new XElement("SpecialAbility", list.Select(v => new XElement("v", new XAttribute("type", v.Type),
                new XAttribute("value", v.Value.ToString(CultureInfo.InvariantCulture)))));
    }
}

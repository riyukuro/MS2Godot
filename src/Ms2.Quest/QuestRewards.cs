using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

/// <summary>
/// The quest reward amounts the quest log shows (questmasktable.cpp, the readers 0x1407622b0-0x140762c80 and their wrappers
/// 0x140761ba0-0x140761ea0), with the tables they read: CExpTable (exptable.cpp), RewardContentsTable
/// (rewardcontentstable.cpp), CGuildXmlData's quest reward factors (guilddef_2.cpp), CGloabalFactor's multi control
/// (gloabalfactor.cpp), and the two client Lua functions (CLua::CalcQuestRewardExpFactorRateByRank and
/// CalcQuestRewardFamePoint, luautil.cpp, calling the Lua functions of the same name in the client's precompiled Lua).
/// <para>
/// Each wrapper builds an Unk_QuestRewardCalc (ctor 0x1407621d0: enabled 1, the local player's level, the guild's npc level
/// 6), runs its reader, and returns value * (1 + bonus) in float, truncated.
/// </para>
/// </summary>
public sealed class QuestRewards
{
    // CExpTable
    readonly long[] nextExp = new long[100];                            // m_nextExp
    readonly Dictionary<uint, ExpCurve> curves = new();                 // m_curves
    readonly ExpCurve[] typeCurves = Enumerable.Range(0, 0x30).Select(_ => new ExpCurve()).ToArray();
    // RewardContentsTable
    readonly Dictionary<uint, List<long>> mesoTables = new();
    readonly Dictionary<uint, (uint MesoTable, double MesoFactor)> contents = new();
    // CGuildXmlData::questFactors (memset 0 by the ctor)
    readonly float[,] guildFactors = new float[300, 5];
    // CGloabalFactor::m_multi
    readonly Dictionary<int, float>[] multi = Enumerable.Range(0, 6).Select(_ => new Dictionary<int, float>()).ToArray();

    public const int MultiQuestRewardMeso = 5;                          // CGloabalFactor::MULTI_QUEST_REWARD_MESO

    public QuestRewards()
    {
        // LoadNextExp: levels 0 .. 98 start at the int64 maximum (the 100th slot is never set by the loop)
        for (int i = 0; i < 99; i++) nextExp[i] = long.MaxValue;
    }

    /// <summary>The client tables from the packs: Table/nextExp, ExpBaseTable, RewardContentExpStaticTable, commonExpTable,
    /// RewardContentMesoTable, RewardContentMesoStaticTable, RewardContent, GuildQuestRewardFactor and multicontrol.</summary>
    public static QuestRewards Load(PackSet packs, string locale, Func<XElement, bool> available)
    {
        var r = new QuestRewards();
        XDocument Doc(string file)
        {
            // the locale's table folder first (as the field metadata table), then the shared one
            foreach (var entry in new[] { $"table/{locale.ToLowerInvariant()}/{file}", $"table/{file}" })
                if (packs.Exists("Xml", entry))
                    return XDocument.Parse(Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'));
            return null;
        }
        r.LoadNextExp(Doc("nextexp.xml"), available);
        r.ReadExpTable(Doc("expbasetable.xml"), available);
        r.ReadExpTable(Doc("rewardcontentexpstatictable.xml"), available);
        r.LoadCommonExp(Doc("commonexptable.xml"), available);
        r.ReadMesoTable(Doc("rewardcontentmesotable.xml"), available);
        r.ReadMesoTable(Doc("rewardcontentmesostatictable.xml"), available);
        r.ReadContents(Doc("rewardcontent.xml"), available);
        r.LoadGuildQuestRewardFactor(Doc("guildquestrewardfactor.xml"), available);
        r.LoadMultiControl(Doc("multicontrol.xml"));
        return r;
    }

    // ---- CExpTable --------------------------------------------------------------------------------------------------

    /// <summary>Unk_ExpCurve: a value per level 1 .. 99 (SetValue), or one base value for level 0.</summary>
    sealed class ExpCurve
    {
        public long Base;
        public List<long> Values = new();

        public void SetValue(int level, long value)
        {
            if (level > 99) return;
            if (level == 0) Base = value;
            else
            {
                if (Values.Count == 0) Values.AddRange(new long[99]);
                Values[level - 1] = value;
            }
        }

        public long GetValue(int level)
        {
            if (Values.Count == 0) return Base;
            if (level <= 0 || level > Values.Count) return 0;
            return Values[level - 1];
        }

        // Scale: the source's values times the factor, rounded, at least 1 (the base is not copied)
        public void Scale(ExpCurve src, double factor) =>
            Values = src.Values.Select(v => Math.Max((long)(v * factor + 0.5), 1L)).ToList();

        public bool IsValid => Base != 0 || Values.Count > 0;
    }

    /// <summary>CExpTable::LoadNextExp: each available exp row's value at its level (1 .. 99).</summary>
    public void LoadNextExp(XDocument doc, Func<XElement, bool> available)
    {
        foreach (var e in doc?.Root?.Elements("exp") ?? Enumerable.Empty<XElement>())
        {
            if (!available(e)) continue;
            int level = 0;
            QuestTable.QueryInt(e, "level", ref level);                // Attribute("level", &n): atoi
            if (level < 1 || level > 99) continue;
            nextExp[level] = Int64Attr(e, "value", 0);
        }
    }

    /// <summary>CExpTable::ReadExpTable: each table by expTableID (a table already read is kept), its available base rows.</summary>
    public void ReadExpTable(XDocument doc, Func<XElement, bool> available)
    {
        foreach (var t in doc?.Root?.Elements("table") ?? Enumerable.Empty<XElement>())
        {
            int id = 0;
            QuestTable.QueryUInt(t, "expTableID", ref id);
            if (id == 0) continue;
            if (!curves.TryGetValue((uint)id, out var curve)) curves[(uint)id] = curve = new ExpCurve();
            if (curve.IsValid) continue;
            foreach (var b in t.Elements("base"))
            {
                if (!available(b)) continue;
                int level = 0;
                QuestTable.QueryInt(b, "level", ref level);
                curve.SetValue(level, Int64Attr(b, "exp", 0));
            }
        }
    }

    /// <summary>CExpTable::LoadCommonExp: each available row's exp type gets its table scaled by the factor.</summary>
    public void LoadCommonExp(XDocument doc, Func<XElement, bool> available)
    {
        foreach (var e in doc?.Root?.Elements("exp") ?? Enumerable.Empty<XElement>())
        {
            if (!available(e)) continue;
            int type = -1, id = 0;
            if ((string)e.Attribute("expType") is { } name) type = ParseExpType(name);
            double factor = DoubleAttr(e, "factor", 0);
            QuestTable.QueryUInt(e, "expTableID", ref id);
            if (type == -1 || id == 0) continue;
            if (!curves.TryGetValue((uint)id, out var src)) curves[(uint)id] = src = new ExpCurve();
            typeCurves[type].Scale(src, factor);
        }
    }

    /// <summary>CExpTable::GetNextExp: the exp of a level, the int64 maximum outside 1 .. 99.</summary>
    public long GetNextExp(int level) => level < 1 || level > 99 ? long.MaxValue : nextExp[level];

    /// <summary>
    /// CExpTable::GetTypeValue: the exp type's curve at the level; type 8 reads GetBaseValue(level, 6), whose m_base no loader
    /// fills (CExpTable::Reserved returns true), so 0.
    /// </summary>
    public long GetTypeValue(int level, int type)
    {
        if (type < 0 || type >= 0x30) return 0;
        if (type == 8) return 0;
        return typeCurves[type].GetValue(level);
    }

    // ---- RewardContentsTable ----------------------------------------------------------------------------------------

    /// <summary>
    /// RewardContentsTable::ReadMesoTable: each table by mesoTableID (one already read is kept), sized 100 by a first row
    /// with a level and 1 by one without; a row past level 99 or without meso fails the load (the rest is not read).
    /// </summary>
    public bool ReadMesoTable(XDocument doc, Func<XElement, bool> available)
    {
        foreach (var t in doc?.Root?.Elements("table") ?? Enumerable.Empty<XElement>())
        {
            int id = 0;
            QuestTable.QueryUInt(t, "mesoTableID", ref id);
            if (id == 0) continue;
            if (!mesoTables.TryGetValue((uint)id, out var values)) mesoTables[(uint)id] = values = new List<long>();
            if (values.Count > 0) continue;
            foreach (var v in t.Elements("v"))
            {
                if (!available(v)) continue;
                int level = 0;
                QuestTable.QueryInt(v, "level", ref level);
                level &= 0xffff;
                long meso = Int64Attr(v, "meso", 0);
                if (values.Count == 0) values.AddRange(new long[level > 0 ? 100 : 1]);
                if (level > 99 || meso == 0) return false;
                if (level < values.Count) values[level] = meso;
            }
        }
        return true;
    }

    /// <summary>RewardContentsTable::ReadContents (RewardContents::Load): each available row with a rewardID, a later one replacing.</summary>
    public void ReadContents(XDocument doc, Func<XElement, bool> available)
    {
        foreach (var v in doc?.Root?.Elements("v") ?? Enumerable.Empty<XElement>())
        {
            if (!available(v)) continue;
            int id = 0, meso = 0;
            QuestTable.QueryUInt(v, "rewardID", ref id);
            QuestTable.QueryUInt(v, "mesoTableID", ref meso);
            double factor = DoubleAttr(v, "mesoFactor", 1.0);
            if (id == 0) continue;
            contents[(uint)id] = ((uint)meso, factor);
        }
    }

    /// <summary>RewardContentsTable::GetReward's meso (GetMeso): the table's value at the level (its one value for a level-less table) times the factor.</summary>
    public bool GetRewardMeso(int rewardId, int level, out long meso)
    {
        meso = 0;
        if (!contents.TryGetValue((uint)rewardId, out var c)) return false;
        if (!mesoTables.TryGetValue(c.MesoTable, out var values)) return true;
        if (values.Count == 1) meso = (long)(values[0] * c.MesoFactor + 0.00001);
        else if (level > 0 && level < values.Count) meso = (long)(values[level] * c.MesoFactor + 0.00001);
        return true;
    }

    // ---- CGuildXmlData / CGloabalFactor -----------------------------------------------------------------------------

    /// <summary>
    /// CGuildXmlData::LoadGuildQuestRewardFactor: each available guildNpc row's factors at its level (guild exp, user exp,
    /// guild fund, user meso, guild coin). The rows are picked by FUN_14028faa0, which keeps one row per level; the data
    /// has one per level.
    /// </summary>
    public void LoadGuildQuestRewardFactor(XDocument doc, Func<XElement, bool> available)
    {
        foreach (var e in doc?.Root?.Elements("guildNpc") ?? Enumerable.Empty<XElement>())
        {
            if (!available(e)) continue;
            int level = 0;
            QuestTable.QueryInt(e, "level", ref level);
            level &= 0xffff;
            if (level >= 300) continue;
            string[] names = { "guildExpFactor", "userExpFactor", "guildFundFactor", "userMesoFactor", "GuildCoinFactor" };
            for (int k = 0; k < 5; k++) guildFactors[level, k] = (float)DoubleAttr(e, names[k], 0);
        }
    }

    /// <summary>CGuildXmlData::GetQuestRewardFactor: the factor of a kind at a guild npc level, 1 past 299.</summary>
    public float GetQuestRewardFactor(int level, int kind) => level < 0 || level >= 300 ? 1.0f : guildFactors[level, kind];

    /// <summary>CGloabalFactor::LoadMultiControl: each multi row (no availability test) by type and key, the first one kept.</summary>
    public void LoadMultiControl(XDocument doc)
    {
        foreach (var e in doc?.Root?.Elements("multi") ?? Enumerable.Empty<XElement>())
        {
            int type = ParseMultiType((string)e.Attribute("type"));
            int key = 0;
            if (type == 4) continue;    // dungeonClearMeso keys are dungeon group names; the quest rewards read type 5 only
            QuestTable.QueryInt(e, "key", ref key);
            float value = (float)DoubleAttr(e, "value", 1.0);
            if (type == 0 || type >= 6) continue;
            multi[type].TryAdd(key, value);
        }
    }

    /// <summary>CGloabalFactor::GetMultiControlFactor: the factor of a type and key, 1 when it has none.</summary>
    public float GetMultiControlFactor(int type, int key) =>
        type == 0 || type >= 6 || !multi[type].TryGetValue(key, out var v) ? 1.0f : v;

    // ReadMultiType (gloabalfactor.cpp)
    static int ParseMultiType(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (Is(s, "enchantScrollReceipe")) return 1;
        if (Is(s, "globalDropItemCount")) return 2;
        if (Is(s, "equipmentSellPrice")) return 3;
        if (Is(s, "dungeonClearMeso")) return 4;
        if (Is(s, "questRewardMeso")) return 5;
        return 0;
    }

    // ---- the client Lua ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The Lua calcQuestRewardExpFactorRateByRank(type, rank, extra, alliance): a rate by rank for quest types 8 and 9
    /// (alliances up to 5 have their own type 9 rates); other types index a missing table, the call fails and
    /// CLua::CalcQuestRewardExpFactorRateByRank returns 0.
    /// </summary>
    public static float LuaExpFactorRateByRank(int type, int rank, int extra, int alliance)
    {
        double[] rates = type switch
        {
            8 => new[] { 0.32, 0.32, 0.32, 0.32, 0.32 },
            9 => alliance <= 5 ? new[] { 5, 4, 1.2, 0.426, 0.32 } : new[] { 1.4, 1.3, 1.2, 1.1, 1 },
            _ => null,
        };
        if (rates == null) return 0;
        return (float)rates[Math.Max(Math.Min(rank + 1, rates.Length), 1) - 1];
    }

    /// <summary>
    /// The Lua calcQuestRewardFamePoint(rank, grade, useMainFamePoint, alliance): the points of the rank (0 for none) times
    /// the multiplier of useMainFamePoint; alliances up to 5 have their own points. The grade is not used.
    /// </summary>
    public static int LuaFamePoint(int rank, int grade, int useMainFamePoint, int alliance)
    {
        double[] points = alliance <= 5 ? new double[] { 2000, 1500, 1000, 800, 700, 0 } : new double[] { 230, 200, 140, 100, 90, 0 };
        double[] mul = alliance <= 5 ? new double[] { 1, 2 } : new double[] { 1, 10 };
        double v = points[Math.Max(Math.Min(rank + 1, points.Length), 1) - 1] * mul[Math.Max(Math.Min(useMainFamePoint, mul.Length), 1) - 1];
        return (int)v;
    }

    // ---- the readers ------------------------------------------------------------------------------------------------

    /// <summary>The reader's context (Unk_QuestRewardCalc): the player's level and the guild's npc level (0 without a guild).</summary>
    public readonly record struct Context(int Level, int GuildNpcLevel);

    // ScaleValue (itemsocketsystemutil.cpp, @0x140632990)
    static long ScaleValue(long value, float rate) => (long)(value * (double)rate + 0.000001);

    // IsNearlyEqual (commonutil.cpp, @0x14028d470)
    static bool IsNearlyEqual(float a, float b) => Math.Abs(a - b) < 1e-06f;

    // the guild bonus of a type 6 quest (the readers' common tail)
    float GuildBonus(QuestData d, Context c, int kind)
    {
        if (d.Type != 6) return 0;
        float f = GetQuestRewardFactor(c.GuildNpcLevel, kind);
        return f >= 1.0 ? f - 1.0f : 0;
    }

    static long Total(long value, float bonus) => (long)((float)value * (1.0f + bonus));

    /// <summary>
    /// FUN_140761cc0 over FUN_1407622b0, the meso: a relativeMeso quest takes the reward contents' meso at the level times the
    /// quest's meso rate (plus the guild bonus, kind 3, for a type 6 quest), the others the money; then the multi control
    /// factor of the quest type, when it is not 1.
    /// </summary>
    public long Meso(QuestData d, Context c)
    {
        long value = 0;
        float bonus = 0;
        var r = d.Complete;
        if (r.RelativeMeso != 0)
        {
            GetRewardMeso(r.RelativeMeso, c.Level, out long meso);
            value = (long)(meso * (double)r.MesoRate);
            bonus = GuildBonus(d, c, 3);
        }
        else value = r.Money;
        float f = GetMultiControlFactor(MultiQuestRewardMeso, d.Type);
        if (!IsNearlyEqual(1.0f, f)) value = (long)((float)value * f);
        return Total(value, bonus);
    }

    /// <summary>
    /// FUN_140761ba0 over FUN_140762500, the exp: the larger of the exp and the relativeExp type's exp at the level times the
    /// quest's exp rate; for a quest with a rank, scaled by the Lua rate of its type, rank and alliance when that is not 0;
    /// plus the guild bonus (kind 1) for a type 6 quest.
    /// </summary>
    public long Exp(QuestData d, Context c)
    {
        var r = d.Complete;
        long relative = 0;
        if (r.RelativeExp != -1) relative = (long)(GetTypeValue(c.Level, r.RelativeExp) * (double)r.ExpRate);
        long exp = Math.Max((long)r.Exp, relative);
        if (d.Rank != 5)
        {
            float add = LuaExpFactorRateByRank(d.Type, d.Rank, r.RewardExpExtra, d.Alliance);
            if (!IsNearlyEqual(add, 0)) exp = ScaleValue(exp, add);
        }
        return Total(exp, GuildBonus(d, c, 1));
    }

    /// <summary>FUN_140761d20 over FUN_1407627a0: the guild fund, plus the guild bonus (kind 2).</summary>
    public long GuildFund(QuestData d, Context c) => Total(d.Complete.GuildFund, GuildBonus(d, c, 2));

    /// <summary>FUN_140761c00 over FUN_1407628a0: the guild exp, plus the guild bonus (kind 0).</summary>
    public long GuildExp(QuestData d, Context c) => Total(d.Complete.GuildExp, GuildBonus(d, c, 0));

    /// <summary>FUN_140761c60 over FUN_1407629a0: the guild coin, plus the guild bonus (kind 4).</summary>
    public int GuildCoin(QuestData d, Context c) => (int)((float)d.Complete.GuildCoin * (1.0f + GuildBonus(d, c, 4)));

    /// <summary>FUN_140761d80 over FUN_140762aa0: the karma.</summary>
    public long Karma(QuestData d) => d.Complete.Karma;

    /// <summary>FUN_140761de0 over FUN_140762b40: the lu.</summary>
    public long Lu(QuestData d) => d.Complete.Lu;

    /// <summary>FUN_140761e40 over FUN_140762be0: the mentee coin.</summary>
    public int MenteeCoin(QuestData d) => d.Complete.MenteeCoin;

    /// <summary>
    /// FUN_140761ea0 over FUN_140762c80, the fame points: for a useMainFamePoint quest, the Lua points of its rank and
    /// alliance (the alliance's grade it passes is not used by the Lua).
    /// </summary>
    public int FamePoint(QuestData d) =>
        d.Complete.UseMainFamePoint == 0 ? 0 : LuaFamePoint(d.Rank, 0, d.Complete.UseMainFamePoint, d.Alliance);

    // ---- the enum parsers (commonenum.cpp) --------------------------------------------------------------------------

    /// <summary>Parse_14021a330: the alliance name, any case; 0 for another.</summary>
    public static int ParseAlliance(string s)
    {
        string[] names = { "darkwind", "mapleunion", "lumiknight", "triaroyalguard", "greenhood", "mapleunion_kritiasexped",
                           "lumiknight_kritiasexped", "greenhood_kritiasexped", "georg" };
        for (int i = 0; i < names.Length; i++)
            if (Is(s, names[i])) return i + 1;
        return 0;
    }

    /// <summary>Parse_14021a570: S 0, A 1, B 2, C 3, D 4, any case; 5 for another.</summary>
    public static int ParseRank(string s)
    {
        string[] names = { "S", "A", "B", "C", "D" };
        for (int i = 0; i < names.Length; i++)
            if (Is(s, names[i])) return i;
        return 5;
    }

    /// <summary>Parse_140217cd0: the reward contents name (0x3eb GuildQuest ..), any case; 0 for an empty or unknown one.</summary>
    public static int ParseRewardContents(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        (string, int)[] names =
        {
            ("GuildQuest", 0x3eb), ("GuildWeeklyQuest", 0x3ec), ("MiniGame", 0x3ed), ("DungeonHelper", 0x3ee),
            ("MiniGameType2", 0x3ef), ("UserOpenMiniGameExtraReward", 0x3f0), ("MentorDungeonNormal", 0x3f1),
            ("MentorDungeonRaid", 0x3f2), ("MentorDungeonEvent", 0x3f3), ("MenteeDungeonNormal1", 0x3f4),
            ("MenteeDungeonNormal2", 0x3f5), ("MenteeDungeonRaid", 0x3f6), ("MenteeDungeonEvent", 0x3f7),
            ("DungeonMenteePartyGift", 0x3f8), ("MentoringReturnUserGift", 0x3f9), ("MentoringReturnUserCoin1", 0x3fa),
            ("MentoringReturnUserCoin2", 0x3fb), ("MentorDungeonChaos", 0x409), ("MenteeDungeonChaos", 0x40a),
            ("MentoringReturnUserCoin3", 0x40b),
        };
        foreach (var (n, v) in names)
            if (Is(s, n)) return v;
        return 0;
    }

    static readonly Dictionary<string, int> expTypeCache = new(StringComparer.Ordinal)
    {
        ["Exp_MapCommon"] = 0, ["Exp_MapHidden"] = 1, ["Exp_TaxiStation"] = 2, ["Exp_Telescope"] = 3, ["Exp_RareChest"] = 4,
        ["Exp_RareChestFirst"] = 5, ["Exp_NormalChest"] = 6, ["Exp_DropItem"] = 7, ["Exp_DungeonBoss"] = 8,
        ["Exp_MusicMasteryLv1"] = 9, ["Exp_MusicMasteryLv2"] = 10, ["Exp_MusicMasteryLv3"] = 11, ["Exp_MusicMasteryLv4"] = 12,
        ["Exp_Arcade"] = 13, ["Exp_Fishing"] = 14, ["Exp_Rest"] = 15, ["Exp_Quest"] = 16, ["Exp_QuestSkyFortress"] = 17,
        ["Exp_PvpBloodMineRank1"] = 18, ["Exp_PvpBloodMineRank2"] = 19, ["Exp_PvpBloodMineRank3"] = 20,
        ["Exp_PvpBloodMineRankOther"] = 21, ["Exp_PvpRedDuelWin"] = 22, ["Exp_PvpRedDuelLose"] = 23,
        ["Exp_PvpBtiTeamWin"] = 24, ["Exp_PvpBtiTeamLose"] = 25, ["Exp_PvpRankDuelWin"] = 26, ["Exp_PvpRankDuelLose"] = 27,
        ["Exp_Gathering"] = 28, ["Exp_Manufacturing"] = 29, ["Exp_RandomDungeonBonus"] = 30, ["Exp_MiniGame"] = 31,
        ["Exp_UserMiniGame"] = 32, ["Exp_UserMiniGameExtra"] = 33, ["Exp_Mission"] = 34, ["Exp_DungeonRelative"] = 35,
        ["Exp_GuildUserExp"] = 36, ["Exp_DailyGuildQuest"] = 37, ["Exp_WeeklyGuildQuest"] = 38, ["Exp_PetTaming"] = 39,
        ["Exp_Construct"] = 42, ["Exp_DailyMission"] = 40, ["Exp_DailyMissionLevelUp"] = 41, ["Exp_MapleSurvival"] = 43,
        ["Exp_EpicQuest"] = 44, ["Exp_DarkStream"] = 45, ["Exp_DungeonClear"] = 46, ["Exp_KillMonster"] = 47,
    };

    static readonly Dictionary<string, int> expTypeNames = new(StringComparer.Ordinal)
    {
        ["mapCommon"] = 0, ["mapHidden"] = 1, ["taxi"] = 2, ["telescope"] = 3, ["rareChestFirst"] = 5, ["rareChest"] = 4,
        ["normalChest"] = 6, ["expDrop"] = 7, ["musicMastery1"] = 9, ["musicMastery2"] = 10, ["musicMastery3"] = 0xb,
        ["musicMastery4"] = 0xc, ["arcade"] = 0xd, ["fishing"] = 0xe, ["rest"] = 0xf, ["bloodMineRank1"] = 0x12,
        ["bloodMineRank2"] = 0x13, ["bloodMineRank3"] = 0x14, ["bloodMineRankOther"] = 0x15, ["redDuelWin"] = 0x16,
        ["redDuelLose"] = 0x17, ["btiTeamWin"] = 0x18, ["btiTeamLose"] = 0x19, ["rankDuelWin"] = 0x1a, ["rankDuelLose"] = 0x1b,
        ["gathering"] = 0x1c, ["manufacturing"] = 0x1d, ["randomDungeonBonus"] = 0x1e, ["miniGame"] = 0x1f,
        ["userMiniGame"] = 0x20, ["userMiniGameExtra"] = 0x21, ["dungeonRelative"] = 0x23, ["guildUserExp"] = 0x24,
        ["petTaming"] = 0x27, ["construct"] = 0x2a, ["mapleSurvival"] = 0x2b, ["quest"] = 0x10, ["questSkyFortress"] = 0x11,
        ["epicQuest"] = 0x2c, ["mission"] = 0x22, ["dailyGuildQuest"] = 0x25, ["weeklyGuildQuest"] = 0x26,
        ["dailymission"] = 0x28, ["dailymissionLevelUp"] = 0x29,
    };

    /// <summary>Parse_14021af40: the exp type name (case sensitive; the Exp_ names too); -1 for an empty or unknown one.</summary>
    public static int ParseExpType(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        if (expTypeCache.TryGetValue(s, out int v)) return v;
        return expTypeNames.TryGetValue(s, out v) ? v : -1;
    }

    // ---- attribute helpers ------------------------------------------------------------------------------------------

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // XmlGetAttr(__int64&) / (double&): the attribute's value, or the default when it is absent or not a number
    static long Int64Attr(XElement e, string name, long def) =>
        long.TryParse(((string)e.Attribute(name))?.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : def;

    static double DoubleAttr(XElement e, string name, double def) =>
        double.TryParse(((string)e.Attribute(name))?.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : def;
}

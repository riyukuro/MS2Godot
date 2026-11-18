using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// CNametagSymbolTable (commonfiles/nametagsymboltable.cpp): the name tag symbols (insignias) of Table/nametagSymbol.xml by
/// id, the rule that picks a symbol's icon (GetIconRanked) and the condition that unlocks it (CheckCondition).
/// </summary>
public sealed class NametagSymbols
{
    /// <summary>NametagConditionType (FUN_1406c67a0, nametagsymboltable.cpp:347): the conditionType names; "" or an unknown
    /// name is None.</summary>
    public enum Condition { None, Level, Enchant, Title, TrophyPoint, Vip, TencentVip, Tgp, Gm, ReturnUser, Birthday, AdventureLevel, Burning, SurvivalLevel }

    /// <summary>CNametagSymbolData: id, icon, conditionType and code (the other attributes are not read here).</summary>
    public sealed record Symbol(ushort Id, string Icon, Condition Condition, uint Code);

    /// <summary>
    /// The local player's state CheckCondition reads. The conditions whose state is not ported (enchant, vip, tencentvip, tgp,
    /// return_user, birthday, burning, survivallevel) are not met. STILL-OPEN: PC::FUN_141661e40 (enchant), IsVip,
    /// FUN_141663080 / FUN_1416630c0 (Tencent), FUN_14165ffe0 with FUN_1406c7610's flag (return user), FUN_141660030
    /// (birthday), MyPC::FUN_1415275b0 (burning) and FUN_141663b50 (survival level).
    /// </summary>
    public sealed record Player(int Level, int AdventureLevel, int GmGrade, IReadOnlyCollection<uint> Titles, uint TrophySum);

    readonly Dictionary<ushort, Symbol> symbols = new();

    public static readonly NametagSymbols Empty = new();
    NametagSymbols() { }

    /// <summary>Load (@0x1406c54d0): Table/nametagSymbol.xml's &lt;symbol&gt; elements, one per id (FUN_14028faa0).</summary>
    public NametagSymbols(PackSet packs)
    {
        const string path = "table/nametagsymbol.xml";
        if (packs == null || !packs.Exists("Xml", path)) return;
        var root = GameStrings.Parse(packs.Read("Xml", path)).Root;
        if (root == null) return;
        foreach (var e in FeatureElements.Collect(packs, root, "symbol", "id"))
        {
            var s = new Symbol((ushort)Atoi(e, "id"), (string)e.Attribute("icon") ?? "", ParseCondition((string)e.Attribute("conditionType")),
                (uint)Atoi(e, "code"));
            symbols[s.Id] = s;                                               // m_map.SetAt
        }
    }

    static long Atoi(XElement e, string attr) => Items.ItemData.Atoi((string)e.Attribute(attr) ?? "0");

    /// <summary>FUN_1406c67a0 (@0x1406c67a0): CompareNoCase against each name.</summary>
    static Condition ParseCondition(string s) => (s ?? "").ToLowerInvariant() switch
    {
        "level" => Condition.Level,
        "enchant" => Condition.Enchant,
        "title" => Condition.Title,
        "trophy_point" => Condition.TrophyPoint,
        "vip" => Condition.Vip,
        "tencentvip" => Condition.TencentVip,
        "tgp" => Condition.Tgp,
        "gm" => Condition.Gm,
        "return_user" => Condition.ReturnUser,
        "birthday" => Condition.Birthday,
        "adventure_level" => Condition.AdventureLevel,
        "burning" => Condition.Burning,
        "survivallevel" => Condition.SurvivalLevel,
        _ => Condition.None,
    };

    /// <summary>GetData (@0x1406c58c0): the symbol, null without one.</summary>
    public Symbol Get(ushort id) => symbols.TryGetValue(id, out var s) ? s : null;

    /// <summary>
    /// CheckCondition (@0x1406c5d60): 1 when the local player meets the symbol's condition, else 0 (also for an unknown id or
    /// a symbol without a condition): level 50, a trophy tally of 1000, the title owned, GM grade 50, adventure level 100.
    /// </summary>
    public uint CheckCondition(ushort id, Player p)
    {
        if (p == null || Get(id) is not { } s) return 0;
        return s.Condition switch
        {
            Condition.Level => p.Level >= 50 ? 1u : 0u,
            Condition.TrophyPoint => p.TrophySum >= 1000 ? 1u : 0u,
            Condition.Title => p.Titles != null && p.Titles.Contains(s.Code) ? 1u : 0u,
            Condition.Gm => p.GmGrade >= 50 ? 1u : 0u,
            Condition.AdventureLevel => p.AdventureLevel >= 100 ? 1u : 0u,
            _ => 0,
        };
    }

    /// <summary>GetIconRanked (@0x1406c61a0): the icon, with the adventure level step (GetAdventureLevelIcon @0x1406c6990)
    /// or the survival level step (GetSurvivalLevelIcon @0x1406c6ab0, the level not ported: its lowest step); "" for an
    /// unknown id.</summary>
    public string IconRanked(ushort id, Player p)
    {
        if (Get(id) is not { } s) return "";
        return s.Condition switch
        {
            Condition.AdventureLevel => Step(s.Icon, (ushort)(p?.AdventureLevel ?? 0), AdventureSteps, 100),
            Condition.SurvivalLevel => Step(s.Icon, 0, SurvivalSteps, 5),
            _ => s.Icon,
        };
    }

    static readonly ushort[] AdventureSteps = { 1000, 700, 500, 300, 100 }, SurvivalSteps = { 60, 55, 45, 35, 25, 15, 5 };

    static string Step(string icon, ushort value, ushort[] steps, int last)
    {
        foreach (var step in steps)
            if (value >= step) return icon + "_" + step.ToString(CultureInfo.InvariantCulture);
        return icon + "_" + last.ToString(CultureInfo.InvariantCulture);
    }
}

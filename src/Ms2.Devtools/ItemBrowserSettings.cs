#if MS2_DEVTOOLS
using System.Globalization;
using System.Text;
using Ms2.Ui;
using Ms2.Ui.Items;

namespace Ms2.Devtools;

/// <summary>
/// What the item browser adds to a spawn: the rarity, an enchant, a limit break, unlocked sockets with a gem each, and
/// explicit bonus stat or special lines. The limits come from the client's tables (CItemEnchantLimitTable,
/// CItemData::unlimitedEnchantMaxGrade, CItemSocketTable, the option tables), mirrored in <see cref="ItemBrowserLimits"/>.
/// </summary>
public sealed class ItemBrowserSettings
{
    /// <summary>0 = the item's own rank, else 1..6.</summary>
    public int Rarity;
    public int Enchant;
    public int LimitBreak;
    /// <summary>The number of unlocked sockets (0..5); a socket is a gem where <see cref="Gems"/> has its id.</summary>
    public int Sockets;
    /// <summary>The gem item id of each socket, 0 for an open empty socket.</summary>
    public readonly int[] Gems = new int[ItemBrowserLimits.MaxSockets];

    /// <summary>One bonus line: a client stat id (0..34) or a client special ability id (1..0xce).</summary>
    public readonly record struct Bonus(bool Special, int Id, int Value);

    public readonly List<Bonus> BonusLines = new();

    public void Reset()
    {
        Rarity = 0; Enchant = 0; LimitBreak = 0; Sockets = 0;
        Array.Clear(Gems);
        BonusLines.Clear();
    }

    /// <summary>A signature of every field, for the last spawned item's server-made blocks.</summary>
    public string Signature() => string.Create(CultureInfo.InvariantCulture,
        $"{Rarity}|{Enchant}|{LimitBreak}|{Sockets}|{string.Join(',', Gems)}|{string.Join(',', BonusLines.ConvertAll(b => $"{(b.Special ? "s" : "b")}{b.Id}={b.Value}"))}");
}

/// <summary>
/// The limits of the enhancement fields, all from the client's data: the enchant's highest grade is the slot's maxGrade
/// in table/enchantlimittable.xml (CItemEnchantLimitTable, the tooltip's CanEnchant reads the same), the limit break's
/// highest level is the item's unlimitedEnchantMaxGrade (CItemData; the limit break is the client's "unlimited enchant",
/// astooltip's s_tooltip_itemoption_unlimited), and the sockets' highest count is
/// CItemSocketTable::GetMaxSocketCount for the item's socketDataId and grade (the server caps it at 5). A bonus stat's
/// normal range is GetRandomOptionRange of the item's random option at its rarity.
/// </summary>
public static class ItemBrowserLimits
{
    public const int MaxSockets = 5;
    public const int MaxEnchant = 15;       // ItemEnchantRules.MaxGrade, the client's maxGrade in the KMS rows
    public const int MaxLimitBreak = 99;    // the data's highest unlimitedEnchantMaxGrade (the Fire Prism equipment)
    public const int ClientStatCount = 0x23;
    public const int SpecialCount = 0xcf;

    /// <summary>The item's highest enchant grade: 0 when the data cannot be enchanted (not a gear item, exceptEnchant, or
    /// the slot's limit has no row for the item's level).</summary>
    public static int EnchantMax(GameTables tables, ItemData d)
    {
        if (d == null || d.ExceptEnchant) return 0;
        if (tables.ItemOptions.EnchantLimitOf(d) is not { } limit) return 0;
        int level = (ushort)d.LevelLimit;
        if (level < limit.MinLevel || level > limit.MaxLevel) return 0;
        return Math.Min(limit.MaxGrade, MaxEnchant);
    }

    /// <summary>The item's highest limit break level (CItemData's unlimitedEnchantMaxGrade), 0 when it has none.</summary>
    public static int LimitBreakMax(ItemData d) => d?.UnlimitedEnchantMaxGrade ?? 0;

    /// <summary>
    /// The most sockets the client gives the item at a rarity (CItemSocketTable::GetMaxSocketCount, capped at the server's
    /// 5). Without a socketDataId the client asks its precompiled Lua CalcItemSocketMaxCount (itemsocketsystemutil.cpp:1462):
    /// a non-skin earring, necklace or ring (subtypes 12, 19, 20) of level 50 and up, grade 3 one socket, a higher grade
    /// three. The chunk is in the data's Precompiled.m2d; the rule here is the one the Maple2 server's Lua.CalcItemSocketMaxCount
    /// ports from it, and it is what the server's own item creation gives those items.
    /// </summary>
    public static int SocketMax(GameTables tables, Func<int, bool> feature, ItemData d, int rarity)
    {
        if (d == null) return 0;
        int grade = rarity > 0 ? rarity : d.Rank;
        uint? table = tables.ItemSockets.GetMaxSocketCount(d, grade, feature);
        int n = table is { } v ? (int)v : LuaSocketMax(d, grade);
        return Math.Min(MaxSockets, n);
    }

    static int LuaSocketMax(ItemData d, int grade)
    {
        if (d.SkinType > 0) return 0;
        if (d.Subtype is not (12 or 19 or 20)) return 0;
        if ((ushort)d.LevelLimit < 50) return 0;
        return grade == 3 ? 1 : grade >= 4 ? 3 : 0;
    }

    /// <summary>The item's normal roll range of a stat at its rarity (GetRandomOptionRange); (0, 0) when it cannot roll it.</summary>
    public static (int First, int Last) StatRange(GameTables tables, ItemData d, int rarity, int stat) =>
        d == null ? (0, 0) : tables.ItemOptions.RandomOptionRange(d, rarity > 0 ? rarity : d.Rank, stat, 0);
}

/// <summary>
/// The item command's options as one server's help reply lists them. The browser reads the reply of "/item --help" once
/// and offers only what the server names (the KMS fork takes the enhancement options; the upstream server takes the
/// spawning ones only), so no region is hard-coded. Offline (no live session) the KMS fork's set is offered.
/// </summary>
public sealed class ItemCommandSupport
{
    public bool Enchant, LimitBreak, Socket, Gem, Stat, Special;

    public static readonly string[] All = { "--enchant", "--limit-break", "--socket", "--gem", "--stat", "--special" };

    /// <summary>Every enhancement option (the fork's item command).</summary>
    public static ItemCommandSupport Fork => new() { Enchant = true, LimitBreak = true, Socket = true, Gem = true, Stat = true, Special = true };

    /// <summary>The options a help reply names, as whole option tokens; false when the reply names none of them.</summary>
    public static ItemCommandSupport Parse(string help)
    {
        var s = new ItemCommandSupport();
        if (string.IsNullOrEmpty(help)) return s;
        s.Enchant = HasToken(help, "--enchant");
        s.LimitBreak = HasToken(help, "--limit-break");
        s.Socket = HasToken(help, "--socket");
        s.Gem = HasToken(help, "--gem");
        s.Stat = HasToken(help, "--stat");
        s.Special = HasToken(help, "--special");
        return s;
    }

    /// <summary>
    /// Whether the text names the option as a whole token, so a help row for --stats, --status or --gemstone does not
    /// count as --stat or --gem. An option is bounded by whitespace or punctuation (or the text's ends); a name
    /// character (a letter, a digit, '-' or '_') on either side means it is part of a longer name.
    /// </summary>
    static bool HasToken(string help, string option)
    {
        int i = 0;
        while ((i = help.IndexOf(option, i, StringComparison.Ordinal)) >= 0)
        {
            int end = i + option.Length;
            bool left = i == 0 || !IsNameChar(help[i - 1]);
            bool right = end == help.Length || !IsNameChar(help[end]);
            if (left && right) return true;
            i = end;
        }
        return false;
    }

    static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';

    /// <summary>
    /// Whether a notice is one of the /item --help reply's lines: the usage line, the section headers, an option row
    /// (two spaces and a dash) or the argument row (two spaces and &lt;id&gt;). Any other notice (a spawn error, a
    /// server message) is not help and passes through to the window's status.
    /// </summary>
    public static bool IsHelpLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains("Usage:", StringComparison.Ordinal)) return true;
        string trimmed = text.TrimStart();
        // The server's console joins "Description:" and "Usage:" with the line after them (GameConsole), so the
        // description arrives as one notice, "Description:  Item spawning."
        if (trimmed.StartsWith("Description:", StringComparison.Ordinal) || trimmed.StartsWith("Options:", StringComparison.Ordinal)
            || trimmed.StartsWith("Arguments:", StringComparison.Ordinal)) return true;
        if (!text.StartsWith("  ", StringComparison.Ordinal)) return false;
        return trimmed.StartsWith("-", StringComparison.Ordinal)
            || trimmed.Contains("<id>", StringComparison.Ordinal)
            || trimmed.StartsWith("item ", StringComparison.Ordinal);
    }

    public bool Any => Enchant || LimitBreak || Socket || Gem || Stat || Special;
}
#endif

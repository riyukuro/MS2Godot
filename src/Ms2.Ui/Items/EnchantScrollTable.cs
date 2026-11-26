using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// CEnchantScrollTable's scrolls (commonfiles/enchantscrolldata.cpp). LoadScrolls (@0x14034de20, :238) reads
/// Table/enchantScroll.xml's &lt;ms2&gt;&lt;scroll&gt; rows: an unavailable row (IsElementAvailable: feature and locale) is skipped,
/// a row <see cref="EnchantScroll.Read"/> rejects is skipped, and a later row of an id replaces the earlier one (SetAt).
/// FindScroll (@0x14034e360, :290) looks an id up; null for none.
/// <para>STILL-OPEN: the recipes (LoadRecipes @0x14034e0d0, the mastery craft) and GetMinKey / GetMaxKey's m_map are not
/// read here.</para>
/// </summary>
public sealed class EnchantScrollTable
{
    public static readonly EnchantScrollTable Empty = new(null, _ => true);

    readonly Dictionary<uint, EnchantScroll> scrolls = new();

    public EnchantScrollTable(XDocument doc, Func<XElement, bool> available)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2") return;
        foreach (var e in root.Elements("scroll"))
        {
            if (!available(e)) continue;
            int id = 0;
            ItemData.Int(e, "id", ref id);
            if (EnchantScroll.Read(e) is not { } s) continue;
            scrolls[(uint)id] = s;
        }
    }

    /// <summary>FindScroll (@0x14034e360): the scroll of an id, null for none.</summary>
    public EnchantScroll Find(int id) => scrolls.GetValueOrDefault((uint)id);

    /// <summary>
    /// UIItemEnchantDialog::GetProtectorRank (@0x1419c86b0, uiitemenchantdialog.cpp:1844): an EnchantSuperProtect item's
    /// function argument 0; an EnchantScroll item's scroll (FindScroll(argument 0)) max rank; else, or with no data or no
    /// scroll, 0.
    /// </summary>
    public static int ProtectorRank(ItemData d, EnchantScrollTable scrolls) => d?.FunctionName switch
    {
        "EnchantSuperProtect" => d.FunctionArgInt(0, 0),
        "EnchantScroll" => (int)((scrolls ?? Empty).Find(d.FunctionArgInt(0, 0))?.MaxRank ?? 0),
        _ => 0,
    };

    /// <summary>
    /// GetProtectorCount (@0x1419c8830, :1861): an EnchantSuperProtect item's function argument 1; an EnchantScroll item's
    /// scroll grade (m_1c); else 0.
    /// </summary>
    public static int ProtectorCount(ItemData d, EnchantScrollTable scrolls) => d?.FunctionName switch
    {
        "EnchantSuperProtect" => d.FunctionArgInt(1, 0),
        "EnchantScroll" => (scrolls ?? Empty).Find(d.FunctionArgInt(0, 0))?.Grade ?? 0,
        _ => 0,
    };

    /// <summary>The ids that have a scroll.</summary>
    public IEnumerable<int> Ids => scrolls.Keys.Select(k => (int)k);
    public int Count => scrolls.Count;
}

/// <summary>CEnchantScrollData (enchantscrolldata.h, 0xc0 bytes): one &lt;scroll&gt; row.</summary>
public sealed class EnchantScroll
{
    /// <summary>m_nScrollType (+0x18): 1 enchant, 2 restore, 3 random, 4, 5 (0 is no scroll).</summary>
    public int ScrollType;
    /// <summary>m_nGrade (+0x1c): the enchant level; not read for type 3.</summary>
    public int Grade;
    public ushort MinLv, MaxLv;
    public bool LockTrade, DisableBreak;
    /// <summary>m_nMinGrade / m_nMaxGrade (+0x2c / +0x30), read for type 4 only.</summary>
    public int MinGrade, MaxGrade;
    public IReadOnlyList<uint> Slots = Array.Empty<uint>(), Ranks = Array.Empty<uint>();

    /// <summary>
    /// CEnchantScrollData::Read (@0x14034d190, enchantscrolldata.cpp:80): scrollType (XmlQuery, FUN_14034e730), minLv, maxLv,
    /// isLockTrade and disableBreak; scrollType 0 rejects the row (null). Then grade unless the type is 3, minGrade and maxGrade
    /// for type 4, and the slot and rank lists split at ',' (XmlSplitImpl, xmlattr.h:91: atoi per token, empty tokens skipped).
    /// </summary>
    public static EnchantScroll Read(XElement e)
    {
        var s = new EnchantScroll();
        int type = 0, minLv = 0, maxLv = 0;
        ItemData.Int(e, "scrollType", ref type);
        ItemData.Int(e, "minLv", ref minLv);
        ItemData.Int(e, "maxLv", ref maxLv);
        ItemData.Bool(e, "isLockTrade", ref s.LockTrade);
        ItemData.Bool(e, "disableBreak", ref s.DisableBreak);
        if (type == 0) return null;
        s.ScrollType = type;
        s.MinLv = (ushort)minLv;
        s.MaxLv = (ushort)maxLv;
        if (type != 3) ItemData.Int(e, "grade", ref s.Grade);
        if (type == 4)
        {
            ItemData.Int(e, "minGrade", ref s.MinGrade);
            ItemData.Int(e, "maxGrade", ref s.MaxGrade);
        }
        s.Slots = Split((string)e.Attribute("slot"));
        s.Ranks = Split((string)e.Attribute("rank"));
        return s;
    }

    /// <summary>GetMaxRank (@0x14034d370, :106): the largest rank in the list, 0 for none.</summary>
    public uint MaxRank
    {
        get
        {
            uint max = 0;
            foreach (uint r in Ranks) if (r > max) max = r;
            return max;
        }
    }

    static uint[] Split(string s)
    {
        if (string.IsNullOrEmpty(s)) return Array.Empty<uint>();
        var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var a = new uint[parts.Length];
        for (int i = 0; i < parts.Length; i++) a[i] = (uint)ItemData.Atoi(parts[i]);
        return a;
    }
}

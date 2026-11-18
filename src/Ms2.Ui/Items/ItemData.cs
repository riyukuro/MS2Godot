using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemData (ms2/commonfiles/itemdata.cpp), the parts an item slot shows: CItemData::Load (:331) reads them from the
/// selected environment of an &lt;item&gt; in itemdata/&lt;id / 100000&gt;.xml.
/// </summary>
public sealed class ItemData
{
    public int Id;
    // <property> (:340): plain int attributes (FUN_1405402a0 and its kind, XmlQuery -> QueryIntAttribute)
    public int Type, Subtype, IconCode, ItemGroup, SkinType, Rank, Param1, Param2;
    // ItemProperty::Load (itemdatastruct.cpp:1122)
    public uint SlotMax = 1;                                   // ctor default 1 (:1115), capped at 99999
    public string SlotIcon = "", Category = "";
    /// <summary>ItemProperty moveDisable (itemdatastruct.cpp:1158) and socketDataId.</summary>
    public bool MoveDisable;
    public int SocketDataId;
    /// <summary>ItemUccMesh::LoadXml (itemdatastruct.cpp:1583): &lt;ucc mesh&gt;.</summary>
    public string UccMesh = "";
    // <PCBang PCBang> (:371)
    public bool PCBang;
    // ItemLimit::Load (itemdatastruct.cpp:567)
    /// <summary>ItemLimit levelLimit (itemdatastruct.cpp:573; the constructor's 1, :560).</summary>
    public int LevelLimit = 1;
    public bool Vip, Wedding;                                  // "vip" -> flag6a, "wedding"
    /// <summary>ItemLimit transferType (:574; "globalTransferType" instead with feature 0x84 GlobalTransferType, :576),
    /// shopSell (:578) and enableBreak (:579); the ctor's 0 / false / 0 (:560).</summary>
    public int TransferType, EnableBreak;
    public bool ShopSell;
    public readonly SortedSet<int> RecommendJobs = new();      // XmlTokenList (",") into std::set
    // ItemSkill::LoadXml (itemdatastruct.cpp:871)
    public int SkillId;
    /// <summary>&lt;basic stringTag&gt;: the tag name, which CItemData::Load maps through CItemDataTable::GetTagByName (itemdatatable.cpp:578,
    /// a case-insensitive name match of the AddItemTag list) to stringTag.</summary>
    public string StringTag = "";
    // ItemFunction::Load (itemdatastruct.cpp:1755)
    public string FunctionName = "", FunctionParameter = "";
    public int FunctionParam1, FunctionParam2, FunctionParam3;
    /// <summary>ItemFunctionArgs words: the parameter split at ',' (FUN_1408d1e00 -> FUN_14025a680, CStringW::Tokenize, empty
    /// tokens dropped); not split when the parameter is "&lt;...&gt;" (parsed as xml values).</summary>
    public readonly List<string> FunctionArgs = new();
    /// <summary>CItemData::Load (itemdata.cpp:364): &lt;tool itemPreset&gt;, the item model id.</summary>
    public int ItemPreset;
    /// <summary>ItemInstallData::Load (itemdatastruct.cpp:1289): &lt;install objCode&gt;.</summary>
    public int InstallObjCode;

    public static ItemData Load(int id, XElement env, bool globalTransferType = false)
    {
        var d = new ItemData { Id = id };
        if (env.Element("basic") is { } b) d.StringTag = (string)b.Attribute("stringTag") ?? "";
        if (env.Element("ucc") is { } ucc) d.UccMesh = (string)ucc.Attribute("mesh") ?? "";
        if (env.Element("property") is { } p)
        {
            Int(p, "type", ref d.Type);
            Int(p, "subtype", ref d.Subtype);
            Int(p, "iconCode", ref d.IconCode);
            Int(p, "itemGroup", ref d.ItemGroup);
            Int(p, "skinType", ref d.SkinType);
            Int(p, "rank", ref d.Rank);                         // Unk_ReadRank (achievedata.cpp:552)
            Int(p, "param1", ref d.Param1);
            Int(p, "param2", ref d.Param2);
            if (Unsigned(p, "slotMax", out uint slotMax)) d.SlotMax = slotMax;
            d.SlotMax = Math.Min(d.SlotMax, 99999u);
            d.Category = (string)p.Attribute("category") ?? d.Category;
            d.SlotIcon = (string)p.Attribute("slotIcon") ?? d.SlotIcon;
            Bool(p, "moveDisable", ref d.MoveDisable);
            Int(p, "socketDataId", ref d.SocketDataId);
        }
        if (env.Element("limit") is { } l)
        {
            Int(l, "levelLimit", ref d.LevelLimit);
            Bool(l, "vip", ref d.Vip);
            Bool(l, "wedding", ref d.Wedding);
            Int(l, "transferType", ref d.TransferType);
            if (globalTransferType && l.Attribute("globalTransferType") != null) Int(l, "globalTransferType", ref d.TransferType);
            Bool(l, "shopSell", ref d.ShopSell);
            Int(l, "enableBreak", ref d.EnableBreak);
            if ((string)l.Attribute("recommendJobs") is { } jobs)
                foreach (var t in jobs.Split(',', StringSplitOptions.RemoveEmptyEntries)) d.RecommendJobs.Add(Atoi(t));
        }
        if (env.Element("skill") is { } sk) Int(sk, "skillID", ref d.SkillId);
        if (env.Element("function") is { } f)
        {
            d.FunctionName = (string)f.Attribute("name") ?? "";
            d.FunctionParameter = (string)f.Attribute("parameter") ?? "";
            Int(f, "param1", ref d.FunctionParam1);
            Int(f, "param2", ref d.FunctionParam2);
            Int(f, "param3", ref d.FunctionParam3);
            string s = d.FunctionParameter;
            bool xml = s.Length > 1 && s[0] == '<' && s[^1] == '>';
            if (!xml) d.FunctionArgs.AddRange(s.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }
        if (env.Element("tool") is { } tool) Int(tool, "itemPreset", ref d.ItemPreset);
        if (env.Element("install") is { } inst) Int(inst, "objCode", ref d.InstallObjCode);
        if (env.Element("PCBang") is { } pc) Bool(pc, "PCBang", ref d.PCBang);
        return d;
    }

    public int EquipSubtype => Type == 1 ? Subtype : 0;        // GetEquipSubtype @0x14053c620 (commonfiles/itemdata.cpp:509)
    public bool IsGemItem => Type == 7;                        // :669
    public bool IsPetItem => Type == 0xb;                      // :675
    public bool IsScoreItem => Type == 0xc;                    // :695
    public bool IsInstallItem => Type == 6;                    // :657
    /// <summary>FUN_14053cbc0 (:663): an equipment item.</summary>
    public bool IsEquipItem => Type == 1;
    /// <summary>HasCubeTextureMesh (:987).</summary>
    public bool HasCubeTextureMesh => UccMesh.Length > 0;
    public bool IsRideItem => Type == 9;                       // :681
    /// <summary>FUN_14053ccc0 (:687): a ride item of subtype 1.</summary>
    public bool IsMultiRideItem => IsRideItem && Subtype == 1;
    /// <summary>FUN_14053d900 (:1056).</summary>
    public bool IsUgcType => Type == 0x16;
    public bool IsGemstone => Type == 0xd;                     // FUN_14053d840 (:1038)
    public bool IsLapenshard => Type == 0x15;                  // IsLapenshardType (:1050)
    public bool IsPieceItem => Type == 0x1b;                   // :1068
    public bool IsCashItem => ItemGroup == 4;                  // :1280
    public bool HasSkinType => SkinType != 0;                  // :756
    /// <summary>:564: not type 1, and more than one per slot.</summary>
    public bool IsStackable => Type != 1 && SlotMax > 1;
    public bool IsEquip => Type == 1;                          // FUN_14053cbc0 (:663)
    public bool IsConsumable => Type == 2;                     // FUN_14053d270 (:842)
    public bool IsSkillItem => Type == 5;                      // :651
    public bool IsType16 => Type == 0x16;                      // FUN_14053d900 (:1056)
    public bool HasFunction => FunctionName.Length != 0;       // :707
    /// <summary>The stringTag is ItemTagKey 0xd9, "UGCMapOX" (CItemDataTable::AddItemTag list, itemdatatable.cpp:1460).</summary>
    public bool IsUgcMapOX => string.Equals(StringTag, "UGCMapOX", StringComparison.OrdinalIgnoreCase);
    /// <summary>IsLevelAllowed @0x14053c9a0 (:618): levelLimit &lt;= level.</summary>
    public bool IsLevelAllowed(int level) => LevelLimit <= (ushort)level;
    /// <summary>FUN_14053e460 (:1358): a consumable, skill, pet or ride item, an item with a function other than TreasureMap, or type 4.</summary>
    public bool IsQuickSlotUsable => IsConsumable || IsSkillItem || IsPetItem || IsRideItem
        || (HasFunction ? FunctionName != "TreasureMap" : Type == 4);
    /// <summary>FUN_14053e900 (:1460): a gem's subtype, else 0.</summary>
    public int GemSubtype => IsGemItem ? Subtype : 0;

    /// <summary>ItemLimit::IsJobRecommended (itemdatastruct.cpp:716): any job when the set is empty; job 1 counts as 10.</summary>
    public bool IsJobRecommended(int job)
    {
        if (RecommendJobs.Count == 0) return true;
        if (job == 1) job = 10;
        return RecommendJobs.Contains(job);
    }

    /// <summary>GetOpenGachaBoxParam0 (:1224): an OpenGachaBox item's first argument (_wtol), else 0.</summary>
    public int OpenGachaBoxParam0 => FunctionName != "OpenGachaBox" ? 0 : FunctionArgs.Count > 0 ? Atoi(FunctionArgs[0]) : 0;

    /// <summary>
    /// GetIconPath (:2070): without a slot icon "./Data/Resource/Image/item/icon/{id}.png"; a slot icon not starting with
    /// '.' is put into that pattern in place of the id; one starting with '.' is the path.
    /// </summary>
    public string IconPath
    {
        get
        {
            const string pattern = "./Data/Resource/Image/item/icon/{0}.png";
            if (SlotIcon.Length == 0) return string.Format(CultureInfo.InvariantCulture, pattern, (uint)Id);
            if (SlotIcon[0] != '.') return string.Format(CultureInfo.InvariantCulture, pattern, SlotIcon);
            return SlotIcon;
        }
    }

    // TiXmlElement::QueryIntAttribute (sscanf "%d"): a missing or unreadable attribute leaves the value
    static void Int(XElement e, string name, ref int v)
    {
        if ((string)e.Attribute(name) is { } s && TryScanInt(s, out long n)) v = (int)n;
    }

    static bool Unsigned(XElement e, string name, out uint v)
    {
        v = 0;
        if ((string)e.Attribute(name) is not { } s || !TryScanInt(s, out long n)) return false;
        v = (uint)n;
        return true;
    }

    // TiXmlElement::QueryBoolAttribute (tinyxml.cpp:620): true/yes/1 or false/no/0, any case; anything else leaves the value
    internal static void Bool(XElement e, string name, ref bool v)
    {
        if ((string)e.Attribute(name) is not { } s) return;
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s == "1") v = true;
        else if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s == "0") v = false;
    }

    static bool TryScanInt(string s, out long v)
    {
        v = 0;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        int start = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = v * 10 + (s[i++] - '0');
        if (i == start) return false;
        if (neg) v = -v;
        return true;
    }

    /// <summary>_wtol / atoi: the leading number, 0 when there is none.</summary>
    internal static int Atoi(string s) => TryScanInt(s, out long v) ? (int)v : 0;
}

/// <summary>
/// CItemDataTable (LoadItemDataFile, itemdatatable.cpp:743): items are read per file, itemdata/&lt;id / 100000&gt;.xml (GetItemDataFileId), on
/// first use; each &lt;item&gt; uses its selected environment (FUN_14028f8d0), and one without any has no data.
/// </summary>
public sealed class ItemDataTable
{
    readonly PackSet packs;
    readonly Dictionary<int, Dictionary<int, ItemData>> files = new();

    public static readonly ItemDataTable Empty = new(null);

    public ItemDataTable(PackSet packs) { this.packs = packs; }

    /// <summary>FUN_14053f870: the item's data, null when unknown.</summary>
    public ItemData Get(int id)
    {
        if (packs == null || id <= 0) return null;
        int file = id / 100000;
        if (!files.TryGetValue(file, out var rows))
        {
            rows = new();
            string entry = $"itemdata/{file:000}.xml";                 // LoadItemDataFile: "{0:03d}.xml"
            if (packs.Exists("Xml", entry))
                foreach (var e in GameStrings.Parse(packs.Read("Xml", entry)).Root.Elements("item"))
                {
                    if (!int.TryParse((string)e.Attribute("id"), out int i)) continue;
                    if (FeatureElements.SelectEnvironment(packs, e) is not { } env) continue;
                    rows.TryAdd(i, ItemData.Load(i, env, LobbyFeatures.IsEnabled(packs, "GlobalTransferType")));                  // std::map::insert keeps the first
                }
            files[file] = rows;
        }
        return rows.TryGetValue(id, out var d) ? d : null;
    }
}

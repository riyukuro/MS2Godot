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
    /// <summary>ItemProperty doNotPreview: non-zero keeps the item out of the preview (IsPreviewable).</summary>
    public int DoNotPreview;
    public uint SlotMax = 1;                                   // ctor default 1 (:1115), capped at 99999
    public string SlotIcon = "", Category = "";
    /// <summary>ItemProperty movie (itemdatastruct.cpp:1132): the tooltip's preview movie.</summary>
    public string Movie = "";
    /// <summary>ItemProperty moveDisable (itemdatastruct.cpp:1158) and socketDataId.</summary>
    public bool MoveDisable;
    /// <summary>property remakeDisable (ItemProperty::Load, itemdatastruct.cpp:1133).</summary>
    public bool RemakeDisable;
    /// <summary>ItemProperty rePackingLimitCount and rePackingItemConsumeCount (itemdatastruct.cpp:1140).</summary>
    public int RePackingLimitCount, RePackingItemConsumeCount;
    public int SocketDataId;
    /// <summary>ItemProperty gearScore and unlimitedEnchantMaxGrade (itemdatastruct.cpp:1137-1139): the item level inputs
    /// (<see cref="ItemLevels"/>).</summary>
    public int GearScore, UnlimitedEnchantMaxGrade;
    /// <summary>ItemUccMesh::LoadXml (itemdatastruct.cpp:1583): &lt;ucc mesh&gt;.</summary>
    public string UccMesh = "";
    // <PCBang PCBang> (:371)
    public bool PCBang;
    /// <summary>ItemFusion::LoadXml (itemdatastruct.cpp:1563): &lt;fusion fusionable&gt;, the item can be a merge base or material.</summary>
    public int Fusionable;
    // ItemPeriod::Load (itemdatastruct.cpp:796), <life>
    /// <summary>"usePeriod": the use period in minutes.</summary>
    public int UsePeriod;
    /// <summary>"expirationPeriod" (y-m-d-h-m-s) as CMsTime's 6-field constructor (mstime.cpp:43) keeps it: _mkgmtime of
    /// the fields, 0 when a field is out of range or the list is not 6 numbers; a server zone time.</summary>
    public long ExpirationFields;
    /// <summary>"expirationType" (1 weekly, 2 monthly as the tooltip reads it), "numberOfWeeksMonths", "termTag".</summary>
    public int ExpirationType;
    public uint NumberOfWeeksMonths;
    public string TermTag = "";
    // ItemLimit::Load (itemdatastruct.cpp:567)
    /// <summary>ItemLimit levelLimit (itemdatastruct.cpp:573; the constructor's 1, :560).</summary>
    public int LevelLimit = 1;
    public bool Vip, Wedding;                                  // "vip" -> flag6a, "wedding"
    /// <summary>ItemLimit transferType (:574; "globalTransferType" instead with feature 0x84 GlobalTransferType, :576),
    /// shopSell (:578) and enableBreak (:579); the ctor's 0 / false / 0 (:560).</summary>
    public int TransferType, EnableBreak;
    /// <summary>ItemLimit enableRegisterMeratMarket (itemdatastruct.cpp:580; ctor false, :561): CItemData::IsMeratMarketRegisterable.</summary>
    public bool EnableRegisterMeratMarket;
    public bool ShopSell;
    public readonly SortedSet<int> RecommendJobs = new();      // XmlTokenList (",") into std::set
    /// <summary>ItemLimit genderLimit (:572; the ctor's 2, any gender).</summary>
    public int GenderLimit = 2;
    /// <summary>ItemLimit jobLimit / disableJobLimit (:585-586, XmlTokenList ",").</summary>
    public readonly List<int> JobLimit = new(), DisableJobLimit = new();
    // ItemSkill::LoadXml (itemdatastruct.cpp:871)
    public int SkillId;
    /// <summary>ItemSkill skillLevel (:877), GetSkillLevel (itemdata.cpp:1128).</summary>
    public int SkillLevel;
    /// <summary>&lt;basic stringTag&gt;: the tag name, which CItemData::Load maps through CItemDataTable::GetTagByName (itemdatatable.cpp:578,
    /// a case-insensitive name match of the AddItemTag list) to stringTag.</summary>
    public string StringTag = "";
    // ItemFunction::Load (itemdatastruct.cpp:1755)
    public string FunctionName = "", FunctionParameter = "";
    public int FunctionParam1, FunctionParam2, FunctionParam3;
    /// <summary>ItemFunctionArgs words: the parameter split at ',' (FUN_1408d1e00 -> FUN_14025a680, CStringW::Tokenize, empty
    /// tokens dropped); not split when the parameter is "&lt;...&gt;" (parsed as xml values).</summary>
    public readonly List<string> FunctionArgs = new();
    /// <summary>function.xml (the parameter as "&lt;v .../&gt;" xml values) GetString(L"notifyDlg") (CSceneStateGame::OnItemUse, scenestategame.cpp:8626): the
    /// dialog the use notice opens; "" when the parameter is no xml or has no such attribute.</summary>
    public string NotifyDlg => FunctionXmlString("notifyDlg");
    /// <summary>function.xml FUN_14042cad0(L"notifyMsg", L"") (scenestategame.cpp:8627): the common string key of the use notice's message.</summary>
    public string NotifyMsg => FunctionXmlString("notifyMsg");

    /// <summary>ItemFunction::HasXmlValues (itemdatastruct.cpp:1815): the parameter is "&lt;...&gt;" xml values.</summary>
    public bool HasFunctionXml => FunctionParameter.Length >= 2 && FunctionParameter[0] == '<' && FunctionParameter[^1] == '>';
    /// <summary>The function's xml values GetInt(name, 0), the attribute read with atoi.</summary>
    public int FunctionXmlInt(string name) => Atoi(FunctionXmlString(name));

    string FunctionXmlString(string name)
    {
        string s = FunctionParameter;
        if (s.Length < 2 || s[0] != '<' || s[^1] != '>') return "";
        try { return (string)System.Xml.Linq.XElement.Parse(s).Attribute(name) ?? ""; }
        catch (System.Xml.XmlException) { return ""; }
    }
    /// <summary>CItemData::Load (itemdata.cpp:364): &lt;tool itemPreset&gt;, the item model id.</summary>
    public int ItemPreset;
    /// <summary>ItemProperty "sell price" (itemdatastruct.cpp:1136): the &lt;property&gt;&lt;sell price&gt; list, each token
    /// read with atoi (XmlConvert, xmlattr.h).</summary>
    public List<long> SellPrices = new();
    /// <summary>ItemInstallData::Load (itemdatastruct.cpp:1289): &lt;install objCode&gt; and space (bit 1 outdoors, bits 2 / 4
    /// indoors too, as the tooltip's FUN_1409de630 reads them).</summary>
    public int InstallObjCode, InstallSpace;
    /// <summary>ItemInstallData attachable (itemdatastruct.cpp:1299, read as an int): ConfirmPlacement takes the attach
    /// path for a lifted cube when it is nonzero (cubecraft_2.cpp:971).</summary>
    public int InstallAttachable;
    /// <summary>ItemInstallData staticAngle (itemdatastruct.cpp:1306, QueryFloatAttribute): added to the construction
    /// guide's yaw in cube mode 1 (cubecraft_2.cpp:984, 1014).</summary>
    public float InstallStaticAngle;
    /// <summary>ItemLimit exceptEnchant (itemdatastruct.cpp:581): the item takes no enchant (CItemEnchantLimitTable::CanEnchant).</summary>
    public bool ExceptEnchant;
    /// <summary>CItemData::Load (itemdata.cpp:366): &lt;option constantID randomID&gt;, the option table ids.</summary>
    public int ConstantId, RandomId;
    /// <summary>&lt;basic originID&gt; (CItemData::Load, itemdata.cpp:335).</summary>
    public int OriginId;
    /// <summary>CItemData::GetOriginOrItemId (itemdata.cpp:477): originID, else the item id.</summary>
    public int OriginOrItemId => OriginId != 0 ? OriginId : Id;
    /// <summary>ItemAdditionalEffect::Load (itemdatastruct.cpp:1687): the &lt;AdditionalEffect id&gt; list (XmlSplit ",").</summary>
    public readonly List<string> AdditionalEffectIds = new();
    /// <summary>The name of the first &lt;slots&gt;/&lt;slot&gt; of the item itself (GMS2 data names the model slot in the item file): HR, FD, CP, or empty.</summary>
    public string FirstSlot = "";
    /// <summary>The item's own &lt;MusicScore isCustomNote&gt; (GMS2 item files; KMS keeps the flag in musicscore.xml).</summary>
    public bool CustomNote;
    /// <summary>ItemMaterial::Load @0x1405473c0 (itemdatastruct.cpp:1086): &lt;material attack&gt;, the CItemData +0x10c a worn
    /// weapon gives a hit sound whose attack has no attackMaterial (CMagic::PlayHitEffects, magicdata.cpp:1709).</summary>
    public int AttackMaterial;

    public static ItemData Load(int id, XElement env, bool globalTransferType = false)
    {
        var d = new ItemData { Id = id };
        if (env.Element("basic") is { } b)
        {
            Int(b, "originID", ref d.OriginId);
            d.StringTag = (string)b.Attribute("stringTag") ?? "";
        }
        if (env.Element("slots")?.Element("slot") is { } slot) d.FirstSlot = (string)slot.Attribute("name") ?? "";
        d.CustomNote = ((string)env.Element("MusicScore")?.Attribute("isCustomNote"))?.ToLowerInvariant() is "true" or "1";
        if (env.Element("ucc") is { } ucc) d.UccMesh = (string)ucc.Attribute("mesh") ?? "";
        if (env.Element("property") is { } p)
        {
            Int(p, "type", ref d.Type);
            if (p.Element("sell") is { } sell)
                d.SellPrices = ((string)sell.Attribute("price") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => (long)Atoi(t)).ToList();
            Int(p, "subtype", ref d.Subtype);
            Int(p, "iconCode", ref d.IconCode);
            Int(p, "itemGroup", ref d.ItemGroup);
            Int(p, "skinType", ref d.SkinType);
            Int(p, "rank", ref d.Rank);                         // Unk_ReadRank (achievedata.cpp:552)
            Int(p, "param1", ref d.Param1);
            Int(p, "param2", ref d.Param2);
            Int(p, "doNotPreview", ref d.DoNotPreview);         // ItemProperty::Load (itemdatastruct.cpp:1127)
            if (Unsigned(p, "slotMax", out uint slotMax)) d.SlotMax = slotMax;
            d.SlotMax = Math.Min(d.SlotMax, 99999u);
            d.Category = (string)p.Attribute("category") ?? d.Category;
            d.SlotIcon = (string)p.Attribute("slotIcon") ?? d.SlotIcon;
            d.Movie = (string)p.Attribute("movie") ?? d.Movie;
            Bool(p, "moveDisable", ref d.MoveDisable);
            Bool(p, "remakeDisable", ref d.RemakeDisable);
            Int(p, "rePackingLimitCount", ref d.RePackingLimitCount);
            Int(p, "rePackingItemConsumeCount", ref d.RePackingItemConsumeCount);
            Int(p, "socketDataId", ref d.SocketDataId);
            Int(p, "gearScore", ref d.GearScore);
            Int(p, "unlimitedEnchantMaxGrade", ref d.UnlimitedEnchantMaxGrade);
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
            Bool(l, "enableRegisterMeratMarket", ref d.EnableRegisterMeratMarket);
            Bool(l, "exceptEnchant", ref d.ExceptEnchant);
            Int(l, "genderLimit", ref d.GenderLimit);
            if ((string)l.Attribute("jobLimit") is { } jl)
                foreach (var t in jl.Split(',', StringSplitOptions.RemoveEmptyEntries)) d.JobLimit.Add(Atoi(t));
            if ((string)l.Attribute("disableJobLimit") is { } dj)
                foreach (var t in dj.Split(',', StringSplitOptions.RemoveEmptyEntries)) d.DisableJobLimit.Add(Atoi(t));
            if ((string)l.Attribute("recommendJobs") is { } jobs)
                foreach (var t in jobs.Split(',', StringSplitOptions.RemoveEmptyEntries)) d.RecommendJobs.Add(Atoi(t));
        }
        if (env.Element("material") is { } mat) Int(mat, "attack", ref d.AttackMaterial);
        if (env.Element("option") is { } opt)
        {
            Int(opt, "constantID", ref d.ConstantId);
            Int(opt, "randomID", ref d.RandomId);
        }
        if (env.Element("AdditionalEffect") is { } ae)
            d.AdditionalEffectIds.AddRange(((string)ae.Attribute("id") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
        if (env.Element("skill") is { } sk) { Int(sk, "skillID", ref d.SkillId); Int(sk, "skillLevel", ref d.SkillLevel); }
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
        if (env.Element("install") is { } inst)
        {
            Int(inst, "objCode", ref d.InstallObjCode);
            Int(inst, "space", ref d.InstallSpace);
            Int(inst, "attachable", ref d.InstallAttachable);
            Guide.GuideXml.Float(inst, "staticAngle", ref d.InstallStaticAngle);
        }
        if (env.Element("fusion") is { } fu) Int(fu, "fusionable", ref d.Fusionable);
        if (env.Element("PCBang") is { } pc) Bool(pc, "PCBang", ref d.PCBang);
        if (env.Element("life") is { } life)
        {
            Int(life, "usePeriod", ref d.UsePeriod);
            var fields = ((string)life.Attribute("expirationPeriod") ?? "").Split('-', StringSplitOptions.RemoveEmptyEntries).Select(Atoi).ToArray();
            if (fields.Length == 6) d.ExpirationFields = MsTimeFields(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]);
            Int(life, "expirationType", ref d.ExpirationType);
            int weeks = 0; Int(life, "numberOfWeeksMonths", ref weeks); d.NumberOfWeeksMonths = (uint)weeks;
            d.TermTag = (string)life.Attribute("termTag") ?? "";
        }
        return d;
    }

    public int EquipSubtype => Type == 1 ? Subtype : 0;        // GetEquipSubtype @0x14053c620 (commonfiles/itemdata.cpp:509)

    /// <summary>ItemFunctionArgs::FUN_1408d1f60 (unit_1408d1ec0_commandargumentaccessors.cpp:61): the word at the index as _wtoi, the default past the list.</summary>
    public int FunctionArgInt(int index, int fallback) => index < FunctionArgs.Count ? Atoi(FunctionArgs[index]) : fallback;

    /// <summary>CItemData::FUN_14053e410 (commonfiles/itemdata.cpp:1348): the merge revert kind, 2 for subtype 0x16 and for the
    /// equip subtypes 0x32-0x38 and 0x50-0x53 (FUN_14053d460), else 1. STILL-OPEN: FUN_14053d460's type 0x17 branch (model group types 5 and 4).</summary>
    public int MergeRevertKind => EquipSubtype is 0x16 or (>= 0x32 and <= 0x38) or (>= 0x50 and <= 0x53) ? 2 : 1;

    /// <summary>CMsTime(y, m, d, h, min, s) (mstime.cpp:43): _mkgmtime64 of the fields when each is in range, else 0.</summary>
    static long MsTimeFields(int y, int mo, int d, int h, int mi, int s)
    {
        if (!(y >= 1900 && mo >= 1 && mo <= 12 && d >= 1 && d <= 31 && h >= 0 && h <= 23 && mi >= 0 && mi <= 59 && s >= 0 && s <= 59)) return 0;
        // _mkgmtime64 normalizes a day past the month's end into the next month; -1 outside 1970 .. 3000-12-31 23:59:59
        var t = new DateTime(y, mo, 1, h, mi, s, DateTimeKind.Utc).AddDays(d - 1);
        if (t.Year < 1970 || t.Year > 3000) return -1;
        return new DateTimeOffset(t).ToUnixTimeSeconds();
    }

    /// <summary>The expiration's CMsTime::GetTime (mstime.h): 0 when unset, else the fields' time plus the zone offset.</summary>
    public long ExpirationTime => ExpirationFields == 0 ? 0 : ExpirationFields + Ms2.Net.MsTime.CalcZoneOffset;

    /// <summary>ItemProperty::GetSellPrice (itemdatastruct.cpp:1263): none 0, one the price, six the rarity's, else 0.</summary>
    public long PropertySellPrice(int rarity) =>
        SellPrices.Count == 0 ? 0 : SellPrices.Count == 1 ? SellPrices[0]
        : SellPrices.Count == 6 && rarity >= 1 && rarity <= 6 ? SellPrices[rarity - 1] : 0;

    /// <summary>CItemData::GetMaidId (itemdata.cpp:1163): param2 of a type 6 subtype 7 item; HasMaidId is above 0.</summary>
    public bool HasMaidId => Type == 6 && Subtype == 7 && Param2 > 0;
    /// <summary>CItemData::HasPeriod (itemdata.cpp:981).</summary>
    public bool HasPeriod => UsePeriod > 0 || ExpirationTime > 0 || ExpirationType != 0 || TermTag.Length != 0;

    public bool IsGemItem => Type == 7;                        // :669
    public bool IsPetItem => Type == 0xb;                      // :675
    public bool IsScoreItem => Type == 0xc;                    // :695
    public bool IsInstallItem => Type == 6;                    // :657
    /// <summary>FUN_14053cbc0 (:663): an equipment item.</summary>
    public bool IsEquipItem => Type == 1;
    /// <summary>GetShopId (:1181): param1 of a type 0x14 item, else 0.</summary>
    public int ShopId => Type == 0x14 ? Param1 : 0;
    /// <summary>GetPetId (itemdata.cpp:1134): param1 of a pet item (type 0xb), else 0.</summary>
    public int PetId => Type == 0xb ? Param1 : 0;
    /// <summary>HasCubeTextureMesh (:987).</summary>
    public bool HasCubeTextureMesh => UccMesh.Length > 0;
    public bool IsRideItem => Type == 9;                       // :681
    /// <summary>GetRideeId (:1173): param1 of a ride item (type 9), else 0.</summary>
    public int RideeId => Type == 9 ? Param1 : 0;
    /// <summary>FUN_14053ccc0 (:687): a ride item of subtype 1.</summary>
    public bool IsMultiRideItem => IsRideItem && Subtype == 1;
    /// <summary>FUN_14053d900 (:1056).</summary>
    public bool IsUgcType => Type == 0x16;
    public bool IsGemstone => Type == 0xd;                     // FUN_14053d840 (:1038)
    public bool IsLapenshard => Type == 0x15;                  // IsLapenshardType (:1050)
    public bool IsPieceItem => Type == 0x1b;                   // :1068
    /// <summary>GetMaidId (:1163): the npc a type 6 / subtype 7 item summons, else 0; HasMaidId (:713) is its being set.</summary>
    public int MaidId => Type == 6 && Subtype == 7 ? Param2 : 0;
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
    /// <summary>
    /// CItemData::HasStat (itemdata.cpp:1849): the stat is the part's own base stat, which the tooltip's constant rows
    /// leave out: 0x1b and 0x1c for a weapon (FUN_14053d760 :999, equip subtype 0x1e-0x22 or 0x32-0x38), else the
    /// subtype's one (FUN_14053f7c0): 0xc / 0x14 0x1a, 0x12 / 0x13 0x19, 0xd-0x11 / 0x15 / 0x16 / 0x29 0x14, 0x28 0x18.
    /// </summary>
    public bool HasStat(int stat)
    {
        int sub = EquipSubtype;
        if ((stat == 0x1b || stat == 0x1c) && Type == 1 && (sub >= 0x1e && sub <= 0x22 || sub >= 0x32 && sub <= 0x38)) return true;
        int own = sub switch
        {
            0xc or 0x14 => 0x1a,
            0x12 or 0x13 => 0x19,
            >= 0xd and <= 0x11 or 0x15 or 0x16 or 0x29 => 0x14,
            0x28 => 0x18,
            _ => -1,
        };
        return own == stat;
    }

    /// <summary>FUN_14053e900 (:1460): a gem's subtype, else 0.</summary>
    public int GemSubtype => IsGemItem ? Subtype : 0;

    /// <summary>CItemData::IsGenderAllowed (itemdata.cpp:610): genderLimit 2 is any, else the same gender.</summary>
    public bool IsGenderAllowed(int gender) => GenderLimit == 2 || GenderLimit == gender;

    /// <summary>ItemLimit::IsJobAllowed (itemdatastruct.cpp:690): job 0 never; a disabled job no; then (job 1 counted as 10) a
    /// jobLimit entry of 0, 1 or the job yes; else only an empty jobLimit.</summary>
    public bool IsJobAllowed(int job)
    {
        if (job == 0) return false;
        if (DisableJobLimit.Contains(job)) return false;
        if (job == 1) job = 10;
        foreach (var l in JobLimit)
            if (l == 0 || l == 1 || l == job) return true;
        return JobLimit.Count == 0;
    }

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
    /// <summary>
    /// The worn layer of an item as PC::vfunc_127 / vfunc_128 (pc.cpp:1400, :1415) need it: null for an unknown item or
    /// one without a model preset (not worn); else CItem::FUN_14052a170 (item.cpp:1629) without a variant: 1 for a skin
    /// item, else 0 (property flag08's layer 3 is never loaded).
    /// </summary>
    public int? WornLayer(int itemId) => Get(itemId) is { ItemPreset: not 0 } d ? (d.HasSkinType ? 1 : 0) : null;

    readonly PackSet packs;
    bool hasItemdata;
    static string entry0(int file) => $"itemdata/{file:000}.xml";
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
            bool present = packs.Exists("Xml", entry);
            hasItemdata |= present;
            if (present)
                using (var r = Ms2.Formats.Text.XmlRows.Open(packs.Read("Xml", entry)))
                {
                    // the root's <item> children, each read as its own element (no document of the whole file)
                    r.MoveToContent();
                    if (!r.IsEmptyElement)
                    {
                        r.Read();
                        while (r.NodeType != System.Xml.XmlNodeType.EndElement && !r.EOF)
                        {
                            if (r.NodeType != System.Xml.XmlNodeType.Element) { r.Read(); continue; }
                            if (r.LocalName != "item" || r.NamespaceURI.Length != 0) { r.Skip(); continue; }
                            var e = (XElement)XNode.ReadFrom(r);
                            if (!int.TryParse((string)e.Attribute("id"), out int i)) continue;
                            if (FeatureElements.SelectEnvironment(packs, e) is not { } env) continue;
                            rows.TryAdd(i, ItemData.Load(i, env, globalTransferType ??= LobbyFeatures.IsEnabled(packs, "GlobalTransferType")));   // std::map::insert keeps the first
                        }
                    }
                }
            files[file] = rows;
        }
        if (rows.TryGetValue(id, out var d)) return d;
        return hasItemdata || packs.Exists("Xml", entry0(file)) ? null : PerItem(id);   // a KMS miss: itemdata/ exists, so no per-item entries
    }

    /// <summary>
    /// The data that keeps one file per item (GMS2: item/&lt;a&gt;/&lt;bb&gt;/&lt;id&gt;.xml, whose &lt;ms2&gt; root holds the item's
    /// &lt;environment&gt; children). STILL-OPEN: the GMS2 client's own path rule (its exe is packed, so unread); the entries are
    /// indexed by the id in their file name, once, and only when the pack has no itemdata/ files, so the KMS data never pays.
    /// </summary>
    ItemData PerItem(int id)
    {
        perItemIndex ??= PerItemIndex(packs.List("Xml"));
        if (perItemRows.TryGetValue(id, out var d)) return d;
        if (perItemIndex.TryGetValue(id, out var entry)
            && GameStrings.Parse(packs.Read("Xml", entry))?.Root is { } root
            && FeatureElements.SelectEnvironment(packs, root) is { } env)
            d = ItemData.Load(id, env, globalTransferType ??= LobbyFeatures.IsEnabled(packs, "GlobalTransferType"));
        perItemRows[id] = d;
        return d;
    }

    /// <summary>id -> entry for every item/.../&lt;id&gt;.xml, empty when the names hold itemdata/ files.</summary>
    internal static Dictionary<int, string> PerItemIndex(IEnumerable<string> names)
    {
        var index = new Dictionary<int, string>();
        if (names.Any(n => n.StartsWith("itemdata/", StringComparison.Ordinal))) return index;
        foreach (var n in names)
        {
            if (!n.StartsWith("item/", StringComparison.Ordinal) || !n.EndsWith(".xml", StringComparison.Ordinal)) continue;
            int slash = n.LastIndexOf('/');
            if (int.TryParse(n.AsSpan(slash + 1, n.Length - slash - 5), NumberStyles.None, CultureInfo.InvariantCulture, out int i)) index.TryAdd(i, n);
        }
        return index;
    }

    internal int PerItemIndexCount => perItemIndex?.Count ?? 0;
    Dictionary<int, string> perItemIndex;
    readonly Dictionary<int, ItemData> perItemRows = new();
    bool? globalTransferType;

    Dictionary<int, uint> textColors;

    /// <summary>
    /// GetTextColor (itemdatatable.cpp:399): a grade's name colour, 0xffffff for a grade without one. LoadTextColor (:1032)
    /// reads table/itemdata.xml's &lt;Text id color="r,g,b"&gt; through FUN_14028c0d0, which gives 0xffRRGGBB.
    /// </summary>
    public uint TextColor(int grade)
    {
        if (textColors == null)
        {
            textColors = new();
            if (packs != null && packs.Exists("Xml", "table/itemdata.xml"))
                foreach (var e in GameStrings.Parse(packs.Read("Xml", "table/itemdata.xml")).Root.Elements("Text"))
                {
                    int.TryParse((string)e.Attribute("id"), out int id);
                    textColors[id] = Rgb((string)e.Attribute("color") ?? "");
                }
        }
        return textColors.TryGetValue(grade, out var c) ? c : 0xffffff;
    }

    /// <summary>FUN_14028c0d0 (commonutil): "r,g,b" as 0xffRRGGBB (atoi of each part); 0 unless there are three parts.</summary>
    static uint Rgb(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 3) return 0;
        static int Atoi(string s) { int i = 0, n = 0; s = s.TrimStart(); bool neg = false; if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-'; while (i < s.Length && char.IsAsciiDigit(s[i])) n = n * 10 + (s[i++] - '0'); return neg ? -n : n; }
        return 0xff000000u | (uint)(Atoi(parts[0]) & 0xff) << 16 | (uint)(Atoi(parts[1]) & 0xff) << 8 | (uint)(Atoi(parts[2]) & 0xff);
    }
}

using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// The camera and bloom parts of a field's entry in Xml.m2d : table/fielddata.xml (&lt;environment&gt; without a locale).
/// Unset values keep the client's defaults (CFieldDataCli / CFieldData constructors).
/// </summary>
public sealed class FieldCamera
{
    public float Distance = 1100f;              // <camera distance>: the farthest zoom
    public float HAngleDeg = 45f;               // <camera hangle>: fixed yaw; the eye sits at hangle + 180 about the target
    public float VAngleDeg = 30f;               // <camera vangle>: angle from the up axis at the farthest zoom
    public float ZoomOutHeightOffset = 60f;     // target height above the player at the farthest zoom
    public float ZoomInHeightOffset = 95f;      // ... at the nearest zoom
    public float FovDeg = 40f;                  // <fov value>: vertical field of view
}

/// <summary>The &lt;hdr&gt; bloom settings (CFieldDataCli defaults when the entry has none).</summary>
public sealed class FieldHdr
{
    public float Threshold = 0.27f;             // bright pass: max(c - threshold, 0)
    public float Offset = 30f;                  // bright pass: x / (x + offset)
    public float BloomScale = 2f;               // final: scene + bloomscale * bloom
    public float GlowScale = 0.1f;              // glow-map share of the bright pass
    /// <summary>CFieldDataCli m_bHdr (+0x220): true in the ctor (fielddatacli.cpp:75) and set true again by &lt;hdr&gt;
    /// (fielddatacli.cpp:250-252), so no field turns it off; GraphicGates.IsHdrEnabled reads it.</summary>
    public bool Enabled = true;
}

public sealed class FieldData
{
    public const string Archive = "Xml";
    public const string Entry = "table/fielddata.xml";

    public int Id;
    public string XBlock;
    public FieldCamera Camera = new();
    public FieldHdr Hdr = new();
    /// <summary>&lt;shadow simpleshadow&gt;: the alpha of the simple ground shadows (CFieldDataCli default 0.5).</summary>
    public float SimpleShadow = 0.5f;
    /// <summary>&lt;shadow optionHighLevel&gt;: the shadow level the field forces at least (CFieldDataCli m_nShadowOptionHighLevel
    /// +0x258, default 0, fielddatacli.cpp:280); WorldService::FUN_141bbc4e0 hands it to FUN_141610450 (worldservice_1.cpp:626-633).</summary>
    public int ShadowOptionHighLevel;
    /// <summary>CFieldDataCli +0x1fc, the field's outer-glow switch: false in the ctor (fielddatacli.cpp:72) and not read from
    /// the xml; only the dev visual-effect dialog sets it (UIVisualEffectDialog::SetGlowEnabled @0x14135d4f0,
    /// uiupperhuddialog.cpp:1819-1826), so it stays false in every field. GraphicGates.IsOuterGlowEnabled reads it.</summary>
    public bool OuterGlow;
    /// <summary>&lt;property checkClimb&gt;: wall climbing allowed (CFieldData default 1).</summary>
    public bool CheckClimb = true;
    /// <summary>&lt;property checkFly&gt; (CFieldData default 0), &lt;ui useEPSkill useRidee usePet&gt; (CFieldDataCli, default 1;
    /// fielddatacli.cpp:228-230). useRidee is the map test of WorldService::FUN_141bcb210 (worldservice_1.cpp:5476).</summary>
    public bool CheckFly, UseEPSkill = true, UseRidee = true, UsePet = true;
    /// <summary>&lt;ui autoFindTaxiStation&gt; (CFieldData m_nAutoFindTaxiStation +0xb8, default 1, fielddata.cpp:549; read at
    /// fielddatacli.cpp:237): CAutoFindTaxiStation::StartAutoFind runs only where it is not 0.</summary>
    public bool AutoFindTaxiStation = true;
    /// <summary>&lt;property skillUseDisable&gt;: a comma list of skill main types, each a bit of m_nSkillUseDisableMask (fielddata.cpp:707).</summary>
    public uint SkillUseDisableMask;
    /// <summary>&lt;property capacity&gt; (CFieldData m_nCapacity, default 0; fielddata.cpp:667, GetCapacity @0x1403757c0, :870).</summary>
    public int Capacity;
    /// <summary>&lt;ui instancingGrid&gt;: the hardware-instancing cell edge (CFieldDataCli m_nInstancingGrid, default 0 = one cell);
    /// WorldService::FUN_141bc72f0 hands it to HardwareInstancingService::SetCellSize before BuildInstancings.</summary>
    public int InstancingGrid;
    /// <summary>&lt;ui bgMask&gt;: the screen mask movie the field's UIScreenMask loads ("BG_mask_shadow.swf"; CFieldDataCli
    /// m_strBgMask, fielddatacli.cpp:220, the raw string); "" when absent.</summary>
    public string BgMask = "";
    /// <summary>&lt;ui vscroll hscroll&gt;: CFieldDataCli m_nVScroll / m_nHScroll (default 1, fielddatacli.cpp:70, read at :221-222), the
    /// background movie's camera and position flags (GFxMovieBackground(bg, vscroll, hscroll), uiservice.cpp:526).</summary>
    public int VScroll = 1, HScroll = 1;
    /// <summary>&lt;ui mapEffectId&gt;: the effect xml CEffectService::vfunc_1 starts as the field's area effect (CFieldDataCli
    /// m_strMapEffectId +0x1d0, fielddatacli.cpp:238; "BG/weather/Eff_Snow.xml"); "" when absent.</summary>
    public string MapEffectId = "";
    /// <summary>&lt;property mapType&gt;: CFieldDataCli::m_nMapType (3 home, 5 dungeon, ... ; uisystemmenudialog.cpp:173).</summary>
    public int MapType;
    /// <summary>&lt;property channelGroupID&gt;: the channel group whose name (string table 23, channelGroupName) prefixes the
    /// system menu's channel rows (UISystemMenuDialog::Notify, uisystemmenudialog.cpp:309-316).</summary>
    public int ChannelGroupId;
    /// <summary>m_nMapLevel (fielddata.h +0x74): &lt;drop maplevel&gt;.</summary>
    public int MapLevel;
    /// <summary>m_nRecallOtherUserProhibit: &lt;cashCall RecallOtherUserProhibit&gt; (fielddata.cpp:740); IsRecallOtherUserAllowed is 0 when set (:811).</summary>
    public bool RecallOtherUserProhibit;
    /// <summary>m_nGlobalSplit: &lt;split globalSplit&gt; (fielddata.cpp:731).</summary>
    public bool GlobalSplit;
    /// <summary>m_nCheckPartyObserve: &lt;property checkPartyObserve&gt; (fielddata.cpp:693).</summary>
    public bool CheckPartyObserve;
    /// <summary>m_bMinimapPing: &lt;property minimapPing&gt; (fielddata.cpp:704), false when absent (the ctor, :557);
    /// UIMinimapDialog::IsPingAllowed (uiminimapentity.cpp:2521).</summary>
    public bool MinimapPing;
    /// <summary>m_bPopupMenu: &lt;property popupMenu&gt; (fielddata.cpp:705), true when absent (the ctor, :557).</summary>
    public bool PopupMenu = true;
    /// <summary>m_bGemSystem: &lt;property gemSystem&gt; (fielddata.cpp:706), true when absent (the ctor, :557).</summary>
    public bool GemSystem = true;
    /// <summary>m_nPkMode: &lt;property pkMode&gt; (fielddata.cpp:688), CFieldData::GetPkMode.</summary>
    public int PkMode;
    /// <summary>m_nContinentCode: &lt;property continentCode&gt; (fielddata.cpp:678), the MapContinentTable row of GetShadowContinent.</summary>
    public int ContinentCode;
    /// <summary>m_nRequireQuest: &lt;property requireQuest&gt; (fielddata.cpp:703), the quest a portal into the field needs rewarded
    /// (PortalAction FUN_140956370, actionkeyobject.cpp:1117).</summary>
    public int RequireQuest;
    /// <summary>m_nTutorialType: &lt;property tutorialType&gt; (fielddata.cpp:702, XmlGetTutorialType @0x140375ad0 reads a plain int, 0 when absent);
    /// CFieldData::HasTutorialType (fielddata.cpp:790) is m_nTutorialType != 0, behind WorldService::HasTutorial (worldservice_1.cpp:5448).</summary>
    public int TutorialType;
    public bool HasTutorialType => TutorialType != 0;

    /// <summary>The map properties of the death state (CFieldData ctor/load, fielddata.cpp:544-556, :665-699, :729): revivalreturnid
    /// (the field's own id when 0 or missing), deathPenalty (1), autoRevivalType and autoRevivalTime (0), doNotRevivalHere (0),
    /// infinityMeratRevival (1).</summary>
    public int RevivalReturnId, DeathPenalty = 1, AutoRevivalType, AutoRevivalTime;
    public bool DoNotRevivalHere, InfiniteMeratRevival = true;
    /// <summary>CFieldData::HasAutoRevivalType1 / 2 (fielddata.cpp:827, :830): the type with a time above 0.</summary>
    public bool HasAutoRevivalType1 => AutoRevivalType == 1 && AutoRevivalTime > 0;
    public bool HasAutoRevivalType2 => AutoRevivalType == 2 && AutoRevivalTime > 0;

    /// <summary>The entry limits and the other C16 properties of CFieldData::vfunc_1 (@0x140374530, fielddata.cpp:651-750),
    /// with the ctor's defaults (all 0, fielddata.cpp:543-556): &lt;property enterAdminLevel&gt; (int, :668),
    /// enterMinLevel / enterMaxLevel (shorts read as an int and cut, XmlQueryInt @0x140876760, :673-674),
    /// recoveryFullHP (:676), limitContent (unsigned: QueryUnsignedAttribute, tinyxml.cpp:607, :697) and exploreType
    /// (XmlGetExploreType @0x140375a40: an int, kept on a failure, :701).</summary>
    public int EnterAdminLevel, RecoveryFullHP, ExploreType;
    public short EnterMinLevel, EnterMaxLevel;
    public uint LimitContent;
    /// <summary>&lt;drop dropRank&gt; (unsigned, fielddata.cpp:724) and globalDropBoxID (XmlTokenList into a vector of
    /// unsigned, :725: the value split at ',', empty tokens skipped, each token atoi'd, xmlattr.h:83).</summary>
    public uint DropRank;
    public uint[] GlobalDropBoxIds = Array.Empty<uint>();
    /// <summary>&lt;cashCall&gt; cashTaxiNotDeparture, cashTaxiNotDestination, cashCallMedicProhibit, cashCallMarketProhibit
    /// (ints, fielddata.cpp:736-739; the field keeps each as a set or clear flag here, as RecallOtherUserProhibit).</summary>
    public bool CashTaxiNotDeparture, CashTaxiNotDestination, CashCallMedicProhibit, CashCallMarketProhibit;

    /// <summary>CFieldData::HasLimitContent @0x140375660 (fielddata.cpp:836): any bit of the mask in limitContent.</summary>
    public bool HasLimitContent(uint mask) => (LimitContent & mask) != 0;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, XDocument> docs = new();

    /// <summary>The parsed table, once per pack set: a pack entry never changes, the callers only read the tree, and FieldData results are built fresh from it.</summary>
    static XDocument Doc(PackSet packs) => docs.GetValue(packs, p => Table(p) ?? new XDocument(new XElement("ms2")));

    /// <summary>
    /// The field table of the data: table/fielddata.xml, or where the pack has none (GMS2) one row per map/&lt;id&gt;.xml
    /// (<see cref="PerFileTable"/>); null when the data has neither. Read the file once through <see cref="Document"/>.
    /// </summary>
    static XDocument Table(PackSet packs) => PerFileTable.Choose(packs.Exists(Archive, Entry),
        () => PackXml.Parse(packs.Read(Archive, Entry)), () => PerFileTable.Rows(packs, "map/", "fieldData"));

    /// <summary>Whether the data has a field table, of either layout.</summary>
    public static bool Exists(PackSet packs) => Doc(packs).Root!.HasElements;

    /// <summary>The parsed table shared per pack set (read once; callers must not change it).</summary>
    public static XDocument Document(PackSet packs) => Doc(packs);

    /// <summary>
    /// The table's entries by id and by default-environment xblock, built once per pack set. The client keeps the loaded
    /// fields in a map by id (CFieldData::LoadFieldData @0x140373d50, fielddata.cpp:591 s_fields.SetAt(id, field)) and
    /// looks one up by key (FindField @0x140374140, fielddata.cpp:610 s_fields.Lookup); entries without a selected
    /// environment are skipped (:583), and a later entry of the same id replaces the earlier one. The client has no
    /// xblock lookup; ours keeps the first entry of a name (case-insensitive), as the old scan did.
    /// </summary>
    sealed class Index
    {
        public readonly Dictionary<int, XElement> ById = new();
        public readonly Dictionary<string, XElement> ByXBlock = new(StringComparer.OrdinalIgnoreCase);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Index> indexes = new();

    static Index IndexOf(PackSet packs) => indexes.GetValue(packs, p =>
    {
        var ix = new Index();
        foreach (var fd in Doc(p).Root.Elements("fieldData"))
        {
            var xb = (string)DefaultEnvironment(fd)?.Element("xblock")?.Attribute("name");
            if (xb == null) continue;
            if ((int?)fd.Attribute("id") is int id) ix.ById[id] = fd;
            ix.ByXBlock.TryAdd(xb, fd);
        }
        return ix;
    });

    /// <summary>
    /// The first &lt;environment&gt; without a locale of a fieldData element, or null (also for a null element).
    /// STILL-OPEN: the client picks the environment by locale and feature: @0x14028f8d0 (commonutil.cpp:1064) keeps the
    /// available ones (IsElementAvailable) and folds them with CFeatureManager::SelectFeatureElement (:1078; ported as
    /// Ms2.Ui FeatureElements), so the higher feature level or the client's locale wins. Taking the first one without a
    /// locale gives a different environment for 9 of the table's entries.
    /// </summary>
    public static XElement DefaultEnvironment(XElement fd)
    {
        if (fd == null) return null;
        foreach (var e in fd.Elements("environment"))
            if (e.Attribute("locale") == null) return e;
        return null;
    }

    /// <summary>The shared fieldData element whose default environment uses the given xblock (case-insensitive), or null.
    /// Read only, as <see cref="Document"/>.</summary>
    public static XElement EntryByXBlock(PackSet packs, string xblockName) =>
        xblockName != null && IndexOf(packs).ByXBlock.TryGetValue(xblockName, out var fd) ? fd : null;

    /// <summary>The entry whose default environment uses the given xblock (case-insensitive), or null.</summary>
    public static FieldData FindByXBlock(PackSet packs, string xblockName) =>
        EntryByXBlock(packs, xblockName) is { } fd ? FromEntry(fd) : null;

    /// <summary>The entry of a field (map) id, or null when the table lacks it or the entry has no default xblock.</summary>
    public static FieldData FindById(PackSet packs, int fieldId) =>
        IndexOf(packs).ById.TryGetValue(fieldId, out var fd) ? FromEntry(fd) : null;

    /// <summary>One fieldData row of the table, read as CFieldData / CFieldDataCli load it; null without a default xblock.</summary>
    public static FieldData FromEntry(XElement fd)
    {
        var env = DefaultEnvironment(fd);      // the first without a locale; the client's pick is STILL-OPEN, see there
        var xb = (string)env?.Element("xblock")?.Attribute("name");
        if (xb == null) return null;
        var o = new FieldData { Id = (int?)fd.Attribute("id") ?? 0, XBlock = xb };
        var cam = env.Element("camera");
        if (cam != null)
        {
            Read(cam, "distance", ref o.Camera.Distance);
            Read(cam, "hangle", ref o.Camera.HAngleDeg);
            Read(cam, "vangle", ref o.Camera.VAngleDeg);
            Read(cam, "zoomOutHeightOffset", ref o.Camera.ZoomOutHeightOffset);
            Read(cam, "zoomInHeightOffset", ref o.Camera.ZoomInHeightOffset);
        }
        var prop = env.Element("property") ?? fd.Element("property");
        // every int property: XmlGetAttr of an int is QueryIntAttribute (tinyxmlhelper.cpp:151, sscanf "%d"), so a value with no
        // leading number keeps the ctor's default (CFieldData fielddata.cpp:543-556, CFieldDataCli fielddatacli.cpp:67-79)
        var ui = env.Element("ui");
        o.CheckClimb = QueryInt(prop, "checkClimb", 1) != 0;
        o.CheckFly = QueryInt(prop, "checkFly", 0) != 0;
        if ((string)prop?.Attribute("skillUseDisable") is { } sud)
            foreach (var t in sud.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bit)) o.SkillUseDisableMask |= 1u << bit;
        o.UseEPSkill = QueryInt(ui, "useEPSkill", 1) != 0;
        o.UseRidee = QueryInt(ui, "useRidee", 1) != 0;
        o.UsePet = QueryInt(ui, "usePet", 1) != 0;
        o.AutoFindTaxiStation = QueryInt(ui, "autoFindTaxiStation", 1) != 0;
        o.MapType = QueryInt(prop, "mapType", 0);
        o.Capacity = QueryInt(prop, "capacity", 0);
        o.PkMode = QueryInt(prop, "pkMode", 0); o.ContinentCode = QueryInt(prop, "continentCode", 0);
        o.RequireQuest = QueryInt(prop, "requireQuest", 0); o.TutorialType = QueryInt(prop, "tutorialType", 0);   // XmlGetTutorialType: XmlQuery int (fielddata.cpp:918)
        o.ChannelGroupId = QueryInt(prop, "channelGroupID", 0);
        o.RecallOtherUserProhibit = QueryInt(env.Element("cashCall"), "RecallOtherUserProhibit", 0) != 0;
        o.GlobalSplit = QueryInt(env.Element("split"), "globalSplit", 0) != 0;
        o.CheckPartyObserve = QueryInt(prop, "checkPartyObserve", 0) != 0;
        o.MinimapPing = BoolAttr(prop, "minimapPing", false);
        // the death state's properties (GMS2 writes deathPenalty="" on 140 fields: the client keeps 1)
        o.RevivalReturnId = QueryInt(prop, "revivalreturnid", 0); if (o.RevivalReturnId == 0) o.RevivalReturnId = o.Id;
        o.DeathPenalty = QueryInt(prop, "deathPenalty", 1);
        o.AutoRevivalType = QueryInt(prop, "autoRevivalType", 0); o.AutoRevivalTime = QueryInt(prop, "autoRevivalTime", 0);
        o.DoNotRevivalHere = QueryInt(prop, "doNotRevivalHere", 0) != 0;
        o.InfiniteMeratRevival = QueryInt(prop, "infinityMeratRevival", 1) != 0;
        o.PopupMenu = BoolAttr(prop, "popupMenu", true);
        o.GemSystem = BoolAttr(prop, "gemSystem", true);
        o.MapLevel = QueryInt(env.Element("drop"), "maplevel", 0);
        o.EnterAdminLevel = QueryInt(prop, "enterAdminLevel", 0);
        o.EnterMinLevel = (short)QueryInt(prop, "enterMinLevel", 0); o.EnterMaxLevel = (short)QueryInt(prop, "enterMaxLevel", 0);
        o.RecoveryFullHP = QueryInt(prop, "recoveryFullHP", 0);
        o.LimitContent = QueryUnsigned(prop, "limitContent", 0);
        o.ExploreType = QueryInt(prop, "exploreType", 0);
        o.DropRank = QueryUnsigned(env.Element("drop"), "dropRank", 0);
        if ((string)env.Element("drop")?.Attribute("globalDropBoxID") is { } gd)
            o.GlobalDropBoxIds = gd.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => (uint)ReservedWords.Atoi(t)).ToArray();
        var cash = env.Element("cashCall");
        o.CashTaxiNotDeparture = QueryInt(cash, "cashTaxiNotDeparture", 0) != 0;
        o.CashTaxiNotDestination = QueryInt(cash, "cashTaxiNotDestination", 0) != 0;
        o.CashCallMedicProhibit = QueryInt(cash, "cashCallMedicProhibit", 0) != 0;
        o.CashCallMarketProhibit = QueryInt(cash, "cashCallMarketProhibit", 0) != 0;
        if ((string)env.Element("ui")?.Attribute("bgMask") is { } bm) o.BgMask = bm;
        o.VScroll = QueryInt(ui, "vscroll", 1);
        o.HScroll = QueryInt(ui, "hscroll", 1);
        if ((string)env.Element("ui")?.Attribute("mapEffectId") is { } me) o.MapEffectId = me;
        o.InstancingGrid = QueryInt(ui, "instancingGrid", 0);
        var fov = env.Element("fov");
        if (fov != null) Read(fov, "value", ref o.Camera.FovDeg);
        var sh = env.Element("shadow");
        if (sh != null)
        {
            Read(sh, "simpleshadow", ref o.SimpleShadow);
            o.ShadowOptionHighLevel = QueryInt(sh, "optionHighLevel", 0);      // FUN_140a56ad0: XmlQuery int, kept on a failure
        }
        var hdr = env.Element("hdr");
        if (hdr != null)
        {
            Read(hdr, "threshold", ref o.Hdr.Threshold);
            Read(hdr, "offset", ref o.Hdr.Offset);
            Read(hdr, "bloomscale", ref o.Hdr.BloomScale);
            Read(hdr, "glowscale", ref o.Hdr.GlowScale);
            o.Hdr.Enabled = true;
        }
        return o;
    }

    /// <summary>The xblock name of a field id (its default environment), or null.</summary>
    public static string XBlockOf(PackSet packs, int fieldId) =>
        IndexOf(packs).ById.TryGetValue(fieldId, out var fd) ? (string)DefaultEnvironment(fd).Element("xblock").Attribute("name") : null;

    /// <summary>TiXmlElement::QueryIntAttribute (sscanf "%d", tinyxml.cpp:1160): the leading signed digits after white space; no
    /// attribute, or a value with no leading number, keeps <paramref name="fallback"/>.</summary>
    static int QueryInt(XElement e, string name, int fallback)
    {
        if ((string)e?.Attribute(name) is not { } s) return fallback;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        int digits = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        if (i == digits) return fallback;
        return int.TryParse(s.AsSpan(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
    }

    /// <summary>XmlGetAttr of an unsigned: TiXmlElement::QueryUnsignedAttribute (tinyxml.cpp:607) stores the int reading cast
    /// to unsigned even when it fails (0 for a value with no leading number); no attribute keeps <paramref name="fallback"/>.</summary>
    static uint QueryUnsigned(XElement e, string name, uint fallback) =>
        e?.Attribute(name) == null ? fallback : unchecked((uint)QueryInt(e, name, 0));

    /// <summary>XmlGetAttr of a bool (tinyxmlhelper.cpp:141, TiXmlElement::QueryBoolAttribute, tinyxml.cpp:618): "true",
    /// "yes" or "1" in any case is true, "false", "no" or "0" false; any other value or none keeps
    /// <paramref name="fallback"/>.</summary>
    static bool BoolAttr(XElement e, string name, bool fallback) => ((string)e?.Attribute(name))?.ToLowerInvariant() switch
    {
        "true" or "yes" or "1" => true,
        "false" or "no" or "0" => false,
        _ => fallback,
    };

    static void Read(XElement e, string name, ref float v)
    {
        var a = (string)e.Attribute(name);
        if (a != null && float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v = f;
    }
}

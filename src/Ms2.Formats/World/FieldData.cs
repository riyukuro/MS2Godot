using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

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
    /// <summary>&lt;property checkClimb&gt;: wall climbing allowed (CFieldData default 1).</summary>
    public bool CheckClimb = true;
    /// <summary>&lt;property checkFly&gt; (CFieldData default 0), &lt;ui useEPSkill usePet&gt; (CFieldDataCli, default 1; fielddatacli.cpp:228).</summary>
    public bool CheckFly, UseEPSkill = true, UsePet = true;
    /// <summary>&lt;property skillUseDisable&gt;: a comma list of skill main types, each a bit of m_nSkillUseDisableMask (fielddata.cpp:707).</summary>
    public uint SkillUseDisableMask;
    /// <summary>&lt;ui instancingGrid&gt;: the hardware-instancing cell edge (CFieldDataCli m_nInstancingGrid, default 0 = one cell);
    /// WorldService::FUN_141bc72f0 hands it to HardwareInstancingService::SetCellSize before BuildInstancings.</summary>
    public int InstancingGrid;
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

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, XDocument> docs = new();

    /// <summary>The parsed table, once per pack set: a pack entry never changes, the callers only read the tree, and FieldData results are built fresh from it.</summary>
    static XDocument Doc(PackSet packs) => docs.GetValue(packs, p => XDocument.Parse(System.Text.Encoding.UTF8.GetString(p.Read(Archive, Entry))));

    /// <summary>The entry whose default environment uses the given xblock (case-insensitive), or null.</summary>
    public static FieldData FindByXBlock(PackSet packs, string xblockName)
    {
        var doc = Doc(packs);
        foreach (var fd in doc.Root.Elements("fieldData"))
        {
            var env = fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
            var xb = (string)env?.Element("xblock")?.Attribute("name");
            if (xb == null || !xb.Equals(xblockName, StringComparison.OrdinalIgnoreCase)) continue;
            return FromEntry(fd);
        }
        return null;
    }

    /// <summary>The entry of a field (map) id, or null when the table lacks it or the entry has no default xblock.</summary>
    public static FieldData FindById(PackSet packs, int fieldId)
    {
        var fd = Doc(packs).Root.Elements("fieldData").FirstOrDefault(f => (int?)f.Attribute("id") == fieldId);
        return fd == null ? null : FromEntry(fd);
    }

    static FieldData FromEntry(XElement fd)
    {
        var env = fd.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
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
        if ((string)prop?.Attribute("checkClimb") is { } cc) o.CheckClimb = cc != "0";
        if ((string)prop?.Attribute("checkFly") is { } cf) o.CheckFly = cf != "0";
        if ((string)prop?.Attribute("skillUseDisable") is { } sud)
            foreach (var t in sud.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bit)) o.SkillUseDisableMask |= 1u << bit;
        if ((string)env.Element("ui")?.Attribute("useEPSkill") is { } ue) o.UseEPSkill = ue != "0";
        if ((string)env.Element("ui")?.Attribute("usePet") is { } up) o.UsePet = up != "0";
        if ((string)prop?.Attribute("mapType") is { } mt && int.TryParse(mt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mti)) o.MapType = mti;
        if ((string)prop?.Attribute("channelGroupID") is { } cg && int.TryParse(cg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cgi)) o.ChannelGroupId = cgi;
        o.RecallOtherUserProhibit = Flag(env.Element("cashCall"), "RecallOtherUserProhibit");
        o.GlobalSplit = Flag(env.Element("split"), "globalSplit");
        o.CheckPartyObserve = Flag(prop, "checkPartyObserve");
        if ((string)env.Element("drop")?.Attribute("maplevel") is { } ml && int.TryParse(ml, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mli)) o.MapLevel = mli;
        if ((string)env.Element("ui")?.Attribute("instancingGrid") is { } ig && int.TryParse(ig, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)) o.InstancingGrid = g;
        var fov = env.Element("fov");
        if (fov != null) Read(fov, "value", ref o.Camera.FovDeg);
        var sh = env.Element("shadow");
        if (sh != null) Read(sh, "simpleshadow", ref o.SimpleShadow);
        var hdr = env.Element("hdr");
        if (hdr != null)
        {
            Read(hdr, "threshold", ref o.Hdr.Threshold);
            Read(hdr, "offset", ref o.Hdr.Offset);
            Read(hdr, "bloomscale", ref o.Hdr.BloomScale);
            Read(hdr, "glowscale", ref o.Hdr.GlowScale);
        }
        return o;
    }

    /// <summary>The xblock name of a field id (its default environment), or null.</summary>
    public static string XBlockOf(PackSet packs, int fieldId)
    {
        var doc = Doc(packs);
        var fd = doc.Root.Elements("fieldData").FirstOrDefault(f => (int?)f.Attribute("id") == fieldId);
        var env = fd?.Elements("environment").FirstOrDefault(e => e.Attribute("locale") == null);
        return (string)env?.Element("xblock")?.Attribute("name");
    }

    /// <summary>XmlGetAttr of an int flag: nonzero when the element has the attribute with a nonzero number.</summary>
    static bool Flag(XElement e, string name) =>
        (string)e?.Attribute(name) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n != 0;

    static void Read(XElement e, string name, ref float v)
    {
        var a = (string)e.Attribute(name);
        if (a != null && float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v = f;
    }
}

using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace Ms2.Formats.World;

/// <summary>
/// EffectSkipMgr's view distances (effectskipmgr.cpp): Table/effectOption.xml's &lt;option N&gt; of the graphic quality, per
/// row (an effect part kind) and caster (the object relation) the distance from the camera's actor past which the part is
/// skipped; 0 never skips. Read once (LoadOption, :181).
/// </summary>
public sealed class EffectSkipTable
{
    /// <summary>The rows LoadOption fills (effectskipmgr.cpp:200-206); the table has 0x17 rows of 8 casters.</summary>
    public const int RowNif = 1, RowSound = 2, RowSwoosh = 7, RowRandom = 0xb, RowVibrate = 0xc, RowChaosLight = 0xe, RowLightning = 0x10;
    const int Rows = 0x17, Casters = 8;

    readonly float[] dist = new float[Rows * Casters];

    /// <summary>The row's distance for the caster (Unk_SkipRow::m_dist, 0 when not read).</summary>
    public float ViewDistance(int row, int caster) => dist[row * Casters + caster];

    /// <summary>
    /// EffectSkipMgr::LoadOption @0x1411b89e0: &lt;ms2&gt;'s first &lt;option&gt; child named by the graphic quality + 1
    /// written in base 8 (_itoa(n, sz, 8)), its &lt;nif&gt;, &lt;sound&gt;, &lt;swoosh&gt;, &lt;random&gt;, &lt;vibrate&gt;,
    /// &lt;chaoslight&gt; and &lt;lightning&gt; children into their rows. No such option leaves every distance 0.
    /// </summary>
    public static EffectSkipTable Parse(string xml, int graphicQuality)
    {
        var t = new EffectSkipTable();
        var root = XDocument.Parse(xml).Root;
        if (root == null || root.Name.LocalName != "ms2") return t;
        var option = root.Element("option" + Convert.ToString(graphicQuality + 1, 8));
        if (option == null) return t;
        t.Load(option.Element("nif"), RowNif);
        t.Load(option.Element("sound"), RowSound);
        t.Load(option.Element("swoosh"), RowSwoosh);
        t.Load(option.Element("random"), RowRandom);
        t.Load(option.Element("vibrate"), RowVibrate);
        t.Load(option.Element("chaoslight"), RowChaosLight);
        t.Load(option.Element("lightning"), RowLightning);
        return t;
    }

    /// <summary>EffectSkipMgr::LoadElements @0x1411b92f0: from the first &lt;element&gt; on, every sibling element; a known
    /// caster takes the viewDistance (0 without it).</summary>
    void Load(XElement node, int row)
    {
        var first = node?.Element("element");
        if (first == null) return;
        for (XNode n = first; n != null; n = n.NextNode)
        {
            if (n is not XElement e) continue;
            float view = 0;
            if ((string)e.Attribute("viewDistance") is { } s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) view = v;
            if (ParseCaster((string)e.Attribute("caster") ?? "", out int caster)) dist[row * Casters + caster] = view;
        }
    }

    /// <summary>EffectSkipMgr::ParseCaster @0x1411b9180: myPC 1, PC 3, monster 5, bossMonster 6, background 7 (case sensitive).</summary>
    public static bool ParseCaster(string name, out int caster)
    {
        caster = name switch { "myPC" => 1, "PC" => 3, "monster" => 5, "bossMonster" => 6, "background" => 7, _ => 0 };
        return caster != 0;
    }

    static bool IsNearlyEqual(float a, float b) => MathF.Abs(a - b) <= 1e-4f;

    /// <summary>
    /// EffectSkipMgr::IsOutOfView @0x1411b86e0 with m_n14 0: caster 0 is never skipped, nor is anything without the camera's
    /// actor (<paramref name="viewer"/> null). The distance is <paramref name="viewDistance"/>, or the row's for the caster
    /// when it is -1; out of view when it is not 0 and the part is not nearer than it.
    /// </summary>
    public bool IsOutOfView(int row, int caster, Vector3 at, Vector3? viewer, float viewDistance = -1)
    {
        if (caster == 0 || viewer is not { } eye) return false;
        float d = Vector3.Distance(at, eye);
        float view = IsNearlyEqual(viewDistance, -1) ? ViewDistance(row, caster) : viewDistance;
        return !(IsNearlyEqual(view, 0) || d < view);
    }

    /// <summary>CEffectService::IsEffectTypeVisible @0x1411a91d0 (effectservice.cpp:2199): the status option
    /// DisplayShowOnlyMyPet (+0x12) hides relation 4, DisplayHideMyPet (+0x13) relation 2.</summary>
    public static bool IsEffectTypeVisible(int kind, bool showOnlyMyPet, bool hideMyPet) =>
        !(showOnlyMyPet && kind == 4) && !(hideMyPet && kind == 2);

    /// <summary>The constant VolumeOtherPc's Define (constantsblocks.cpp:794), its value when the constants do not set it.</summary>
    public const float VolumeOtherPcDefine = 0.9f;

    /// <summary>
    /// CEffectService::FUN_1411a5110 @0x1411a5110 (effectservice.cpp:1321), a sound event at a place: hidden by
    /// <see cref="IsEffectTypeVisible"/>, or out of view on <see cref="RowSound"/> for the relation (a null table skips
    /// nothing), returns -1; else the volume, <paramref name="volume"/> times VolumeOtherPc for relation 3.
    /// </summary>
    public static float SoundEventVolume(EffectSkipTable skip, int kind, Vector3 at, Vector3? viewer, float volume,
        float volumeOtherPc, bool showOnlyMyPet, bool hideMyPet)
    {
        if (!IsEffectTypeVisible(kind, showOnlyMyPet, hideMyPet)) return -1;
        if (skip != null && skip.IsOutOfView(RowSound, kind, at, viewer)) return -1;
        return (kind == 3 ? volumeOtherPc : 1f) * volume;
    }
}

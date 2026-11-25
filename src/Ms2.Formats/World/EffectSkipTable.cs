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

    /// <summary>
    /// CreateEffect @0x14119eae0 for a soundEvent part (effectservice.cpp:287-331): no test with offSkip; else
    /// <see cref="IsOutOfView"/> on <see cref="RowSound"/> for the caster <paramref name="kind"/>, at the part's
    /// forceHearDistance when it is not 0 and the caster is a monster (5) or a boss (6), else at the row's distance. Out of
    /// view, the effect is not made and nothing plays. A null table skips nothing.
    /// STILL-OPEN: the second test at the started effect's root (:337-345) is taken as the same place; an owner of class
    /// 0x6200044 swaps the target object for the local player's (:307-311).
    /// </summary>
    public static bool SoundEventOutOfView(EffectSkipTable skip, EffectPart part, int kind, Vector3 at, Vector3? viewer)
    {
        if (skip == null || part.OffSkip) return false;
        float view = !IsNearlyEqual(part.ForceHearDistance, 0) && (kind == 5 || kind == 6) ? part.ForceHearDistance : -1;
        return skip.IsOutOfView(RowSound, kind, at, viewer, view);
    }

    /// <summary>The effect param kinds (effectservice.h): CreateEffect's view tests for the screen kinds (:322-330) and the rows
    /// above.</summary>
    public const int TypeNif = 1, TypeSoundEvent = 2, TypeMonochrome = 4, TypeSwoosh = 7, TypeStatus = 8, TypeLight = 9, TypeFog = 10,
        TypeRandom = 11, TypeVibrate = 12, TypeRibbon = 13, TypeTextureOverride = 15, TypeLightning = 16, TypeBeam = 21, TypeAfterImage = 22;

    static readonly string[] TypeNames =
    {
        "nif", "soundEvent", "soundMusic", "monochrome", "radialBlur", "weather", "swoosh", "status", "light", "fog", "random",
        "vibrate", "ribbon", "chaoslight", "textureoverride", "lightning", "cameraMove", "screenMasking", "gaussianBlur",
        "postShimmer", "beam", "afterImage",
    };

    /// <summary>ParseEffectType @0x1411a0f80 (effectdatamanager.cpp): the type name (CompareNoCase) to its kind, nif 1 to
    /// afterImage 22; empty or unknown is 0.</summary>
    public static int ParseEffectType(string type)
    {
        if (string.IsNullOrEmpty(type)) return 0;
        for (int i = 0; i < TypeNames.Length; i++)
            if (string.Equals(type, TypeNames[i], StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    /// <summary>
    /// EffectSkipMgr::ShouldSkipByView @0x1411b8870 (effectskipmgr.cpp:157-176): an effect's params (the xml's top level
    /// parts) at <paramref name="at"/> for the caster kind. A part without offSkip is tested on its kind's row, a nif part with
    /// its own viewDistance; one part out of view skips the whole effect. Its only callers are CRegionSkillObject::UpdateEffects'
    /// two retries (regionskillobject.cpp:328, :370).
    /// </summary>
    public bool ShouldSkipByView(IReadOnlyList<EffectPart> parts, int caster, Vector3 at, Vector3? viewer)
    {
        if (parts == null) return false;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (!p.TopLevel || p.OffSkip) continue;
            if (IsOutOfView(p.TypeId, caster, at, viewer, p.TypeId == TypeNif ? p.ViewDistance : -1)) return true;
        }
        return false;
    }

    /// <summary>
    /// The view tests one part of an effect meets as it is made, at its world place <paramref name="at"/>:
    /// <list type="bullet">
    /// <item>A nif part: CNifEffect::vfunc_3 (nifeffectparam.cpp:757-766), row 1 with the part's viewDistance, unless offSkip
    /// or the target's no-cull flag (m_b30: the buffs' keep effects set it, no region skill target does).</item>
    /// <item>A soundEvent part: <see cref="SoundEventOutOfView"/>.</item>
    /// </list>
    /// True when the part is refused, and so not made. CreateEffect's other screen kinds (monochrome, radialBlur, vibrate,
    /// cameraMove, screenMasking, gaussianBlur, postShimmer) are not played by the callers.
    /// </summary>
    public bool PartOutOfView(EffectPart part, int caster, Vector3 at, Vector3? viewer, bool noCull = false)
    {
        if (part.OffSkip) return false;
        if (part.TypeId == TypeNif) return !noCull && IsOutOfView(RowNif, caster, at, viewer, part.ViewDistance);
        if (part.TypeId == TypeSoundEvent) return SoundEventOutOfView(this, part, caster, at, viewer);
        return false;
    }

    /// <summary>
    /// CreateEffect's second test for a random part (effectservice.cpp:337-345): once its child is made, CRandomEffect::vfunc_8
    /// (randomeffectparam.cpp:199-203) gives the child's place, tested on <see cref="RowRandom"/> for the caster unless the random
    /// part has offSkip; out of view, the random effect and its child go (vfunc_7).
    /// </summary>
    public bool RandomOutOfView(EffectPart random, int caster, Vector3 at, Vector3? viewer) =>
        !random.OffSkip && IsOutOfView(RowRandom, caster, at, viewer);

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

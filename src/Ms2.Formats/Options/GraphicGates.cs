using Ms2.Formats.World;

namespace Ms2.Formats.Options;

/// <summary>
/// The field-plus-option gates of CGameOptionStruct (client/ms2/gameoptionstruct.cpp) that switch the post-processing chain,
/// and the light scale that goes with the HDR switch (MS2LightService, client/ms2/ms2lightservice.cpp).
/// </summary>
public static class GraphicGates
{
    /// <summary>IsHdrEnabled @0x14125e0b0 (gameoptionstruct.cpp:2031-2037): with a field loaded whose m_bHdr is off, false;
    /// otherwise the displayHdr option. The client's HDR switch (SetSoftParticlesOption @0x1409a4c60, applyeffect.cpp:473-494)
    /// turns BloomRenderStep (step 3) and GlowRenderClick (click 8, when step 3 is active) on or off with it.</summary>
    public static bool IsHdrEnabled(FieldData field, GameOption.Graphic g)
    {
        if (field != null && !field.Hdr.Enabled) return false;
        return g.DisplayHdr;
    }

    /// <summary>IsOuterGlowEnabled @0x14125e160 (gameoptionstruct.cpp:2049-2055): with a field loaded whose +0x1fc flag is off,
    /// false; otherwise the outer-glow option. ApplyEffect::ApplyOuterGlowOption (applyeffect.cpp:343-345) passes it to
    /// SetOuterGlowEnabled @0x1409a4e70. The field flag is only set by the dev dialog, so this is false in every field.
    /// Nothing outside the tests calls it yet: the outer-glow pass (SetOuterGlowEnabled) is not ported.</summary>
    public static bool IsOuterGlowEnabled(FieldData field, GameOption.Graphic g)
    {
        if (field != null && !field.OuterGlow) return false;
        return g.OuterGlow;
    }

    /// <summary>IsFogEnabled @0x14125e020 (gameoptionstruct.cpp:2022-2028): with a field loaded whose m_bFog (+0x1fd, set by
    /// CFieldDataCli::LoadXBlock only when the mapxblock has a fog element, fielddatacli.cpp:400-408) is off, false; otherwise
    /// the graphic option's fog flag (+0x18), which its reset sets true (gameoptionstruct.cpp:1921) and no load or dialog
    /// changes, so true by default here. ApplyEffect::ApplyFieldFog (applyeffect.cpp:497-514) passes it to SetFogRange: the
    /// field's depth fog on or off.</summary>
    public static bool IsFogEnabled(bool fieldFog, bool optionFog = true) => fieldFog && optionFog;

    /// <summary>The light dimmer scale for the HDR state: SetHdr @0x14125e300 (gameoptionstruct.cpp:2081-2090) calls
    /// MS2LightService::ScaleLightIntensity @0x1414c0820 (ms2lightservice.cpp:395-403) with 1.0 when HDR is on and the bloom step
    /// is active, 1.1 otherwise; HandleEntityEnterWorld @0x1414c08e0 (ms2lightservice.cpp:407-433) gives each entering light
    /// its stored Dimmer times 1.1 under the same test. Every light model (ambient, directional, point, spot) is scaled.</summary>
    public static float LightDimmerScale(bool hdrOn) => hdrOn ? 1.0f : 1.1f;
}

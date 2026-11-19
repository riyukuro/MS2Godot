using Godot;

namespace Ms2Godot;

/// <summary>Shader parameter names, each allocated once: a string literal passed to SetShaderParameter makes a new finalizable StringName on every call.</summary>
static class ShaderParam
{
    public static readonly StringName
        AlphaRef = "alpha_ref",
        AlphaTest = "alpha_test",
        FadeAlpha = "fade_alpha",
        FadeVcSwap = "fade_vc_swap",
        BaseTex = "base_tex",
        Color0 = "color0",
        Color1 = "color1",
        Color2 = "color2",
        ColorBoost = "color_boost",
        CtrlTex = "ctrl_tex",
        DarkTex = "dark_tex",
        DarkUv2 = "dark_uv2",
        DecalTex = "decal_tex",
        Emissive = "emissive",
        EnvTex = "env_tex", EnvU = "env_u", EnvV = "env_v",
        Glass = "glass",
        Fill = "fill",
        FlipCount = "flip_count",
        FlipPeriod = "flip_period",
        FlipSlot = "flip_slot",
        FlipTex = "flip_tex",
        FresnelBoost = "fresnel_boost",
        FresnelExponent = "fresnel_exponent",
        GlossTex = "gloss_tex",
        GlowTex = "glow_tex", GlowScale = "glow_scale",
        HairTex = "hair_tex",
        HasCtrl = "has_ctrl",
        HasDark = "has_dark",
        HasDecal = "has_decal",
        HasEnv = "has_env",
        HasGloss = "has_gloss",
        HasGlow = "has_glow",
        HasHair = "has_hair",
        HasNormal = "has_normal",
        HasTex = "has_tex",
        HasVcolor = "has_vcolor",
        MatAmbient = "mat_ambient",
        MatDiffuse = "mat_diffuse",
        MatSpecular = "mat_specular",
        NormalTex = "normal_tex",
        ShadowTex = "shadow_tex",
        Skin = "skin",
        SpecPower = "spec_power",
        SpecularOn = "specular_on",
        VcolorAmbDiff = "vcolor_amb_diff";

    /// <summary>"color0".."color2", the character shader's three recolour parameters.</summary>
    public static readonly StringName[] Color = { "color0", "color1", "color2" };
}

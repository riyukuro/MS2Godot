using Godot;

namespace Ms2Godot;

/// <summary>Shader parameter names, each allocated once: a string literal passed to SetShaderParameter makes a new finalizable StringName on every call.</summary>
static class ShaderParam
{
    public static readonly StringName
        AlphaRef = "alpha_ref",
        AlphaTest = "alpha_test",
        FadeAlpha = "fade_alpha",
        MatAlpha = "mat_alpha",
        AlphaAnimCount = "alpha_anim_count",
        AlphaAnimType = "alpha_anim_type",
        AlphaAnimTime = "alpha_anim_time",
        AlphaAnimT = "alpha_anim_t",
        AlphaAnimV = "alpha_anim_v",
        AlphaAnimIn = "alpha_anim_in",
        AlphaAnimOut = "alpha_anim_out",
        FadeVcSwap = "fade_vc_swap",
        FrozenTime = "frozen_time",
        BaseTex = "base_tex", BaseUv2 = "base_uv2",
        Color0 = "color0",
        Color1 = "color1",
        Color2 = "color2",
        ColorBoost = "color_boost",
        CtrlTex = "ctrl_tex",
        DarkTex = "dark_tex",
        DarkUv2 = "dark_uv2",
        DecalTex = "decal_tex",
        DetailTex = "detail_tex", DetailUv2 = "detail_uv2",
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
        GlowTex = "glow_tex", GlowUv2 = "glow_uv2", GlowScale = "glow_scale",
        HairTex = "hair_tex",
        HasCtrl = "has_ctrl",
        HasDark = "has_dark",
        HasDecal = "has_decal",
        HasDetail = "has_detail",
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

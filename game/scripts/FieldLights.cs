using System;
using Godot;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// The field's point and spot lights (FieldLightSet: the xblock's PointLight / SpotLight entities) for the shaders: a float
/// texture of their values (ms2_lights.gdshaderinc's layout) and their count, as global shader parameters. The shaders
/// evaluate the client's light fragments from it; each field draw masks out the lights the client does not attach to it
/// (FieldBuilder, ms2_light_off), and characters take every light. No Godot light nodes.
/// <para>Colours are times the dimmer as the light entity gives it, times the HDR light scale: MS2LightService::
/// HandleEntityEnterWorld @0x1414c08e0 (ms2lightservice.cpp:407-435) raises every entering light's dimmer by 1.1 unless HDR
/// and the post-processing step 3 are on, and SetHdr (gameoptionstruct.cpp:2081-2090) rescales every stored light through
/// ScaleLightIntensity @0x1414c0820 (ms2lightservice.cpp:395-403; GraphicGates.LightDimmerScale). Effect lights are light
/// entities too (CLightEffect adds an "EffectPointLight", lighteffectparam.cpp:363-377), so they take the scale as well
/// (FieldLightSet.PutColourRows). A rescale only rewrites colours: OnPropertyUpdate (ms2lightservice.cpp:218-232) relights
/// on Position and Rotation only, so the receivers stay as picked. STILL-OPEN: the client's first pick at field enter may
/// see the scaled dimmer (FieldLightSet.PriorityFor's intensity term); here it is made with the entities' own dimmers.</para>
/// <para>No shadow maps: a light with CastShadows gets an NiShadowGenerator at the map-object shadow level
/// (LightService::UpdateShadowCaster, LightService.cpp:889-918; MS2LightService::UpdateLightShadow,
/// ms2lightservice.cpp:356-385). STILL-OPEN: those point and spot shadows (31 light entities set CastShadows).</para>
/// </summary>
public static class FieldLights
{
    /// <summary>Rows per light in the texture.</summary>
    const int Rows = 6;
    /// <summary>The light mask is a float instance uniform: 24 bits are exact.</summary>
    const int MaxLights = 24;

    static FieldLightSet current;
    /// <summary>The HDR light scale every light's dimmer is times (GraphicGates.LightDimmerScale).</summary>
    static float scale = 1;
    static float[] data;
    static byte[] bytes;
    static Image image;
    static ImageTexture texture;

    /// <summary>Sets the field's lights, each dimmer times <paramref name="dimmerScale"/>; returns how many. The texture is as
    /// wide as the light mask (<see cref="MaxLights"/>), so an effect light that starts or ends only rewrites its column
    /// (<see cref="Refresh"/>): nothing is built per frame.</summary>
    public static int Add(FieldLightSet set, float dimmerScale)
    {
        scale = dimmerScale;
        int n = Math.Min(set?.Lights.Count ?? 0, MaxLights);
        if (set != null && set.Lights.Count > MaxLights) GD.PushWarning($"field lights: {set.Lights.Count} lights, using the first {MaxLights}");
        current = set;
        data = new float[MaxLights * Rows * 4];
        bytes = new byte[data.Length * 4];
        image = null; texture = null;
        for (int i = 0; i < n; i++) PutLight(i, set.Lights[i]);
        Publish(n);
        return n;
    }

    /// <summary>Drops the field's light set (a field change: the next field's Add comes only after its build). Effect lights
    /// started until then get null and are tried again (CombatLayer, RegionSkills), so none lands in the old field's set.
    /// <paramref name="set"/>: only when it is still the current one (a field leaving after the next one's Add keeps that).</summary>
    public static void Clear(FieldLightSet set = null)
    {
        if (set == null || current == set) current = null;
    }

    /// <summary>Starts an effect light at a client-space place (CLightEffect, FieldLightSet.StartEffect); null when the field has
    /// no free slot. One texture write, at the start.</summary>
    public static FieldLight Start(EffectLightParam p, Ms2.Formats.Model.Vec3 position)
    {
        if (current == null) return null;
        var l = current.StartEffect(p, position);
        if (l != null) Refresh();
        return l;
    }

    /// <summary>Ends an effect light: its slot goes dark and free. One texture write, at the end.</summary>
    public static void End(FieldLight l)
    {
        if (current == null || l == null) return;
        current.EndEffect(l);
        Refresh();
    }

    /// <summary>An effect light's dimmer changed (CLightEffect::vfunc_10, lighteffectparam.cpp:451-456: its own or 0). One
    /// texture write.</summary>
    public static void SetDimmer(FieldLight l, float dimmer)
    {
        if (current == null || l == null || l.Dimmer == dimmer) return;
        l.Dimmer = dimmer;
        Refresh();
    }

    /// <summary>ScaleLightIntensity @0x1414c0820 (ms2lightservice.cpp:395-403): every light's own dimmer times
    /// <paramref name="dimmerScale"/>. Rewrites the field lights' columns and the effect columns into the same buffers.</summary>
    public static void Rescale(float dimmerScale)
    {
        scale = dimmerScale;
        if (current == null) return;
        int n = Math.Min(current.Lights.Count, MaxLights);
        for (int i = 0; i < n; i++) PutLight(i, current.Lights[i]);
        Refresh();
    }

    /// <summary>Rewrites the effect columns and the count: the field's lights keep theirs.</summary>
    static void Refresh()
    {
        int n = Math.Min(current.Lights.Count, MaxLights), top = n;
        for (int i = 0; i < current.Effects.Length; i++)
        {
            var l = current.Effects[i];
            if (l == null) { ClearLight(n + i); continue; }
            PutLight(n + i, l);
            top = n + i + 1;
        }
        Publish(top);
    }

    static void PutLight(int i, FieldLight l)
    {
        var pos = FieldBuilder.ToGodotPoint(l.Position);
        var dir = FieldBuilder.ToGodotPoint(l.Direction).Normalized();
        float dim = l.Dimmer * scale;
        bool ambient = l.Ambient[0] * dim != 0 || l.Ambient[1] * dim != 0 || l.Ambient[2] * dim != 0;
        const int w = MaxLights;
        Put(data, w, i, 0, pos.X, pos.Y, pos.Z, (l.Spot ? 1 : 0) + (ambient ? 2 : 0));
        FieldLightSet.PutColourRows(data, w, i, l, scale);
        Put(data, w, i, 4, dir.X, dir.Y, dir.Z, l.OuterCos);
        Put(data, w, i, 5, l.InnerCos, l.Exponent, 0, 0);
    }

    /// <summary>A free slot below the highest used one: no colour, so it adds nothing.</summary>
    static void ClearLight(int i)
    {
        for (int row = 0; row < Rows; row++) Put(data, MaxLights, i, row, 0, 0, 0, 0);
        Put(data, MaxLights, i, 1, 0, 0, 0, 1);
    }

    static void Publish(int count)
    {
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        if (image == null)
        {
            image = Image.CreateFromData(MaxLights, Rows, false, Image.Format.Rgbaf, bytes);
            texture = ImageTexture.CreateFromImage(image);
            RenderingServer.GlobalShaderParameterSet("ms2_lights", texture);
        }
        else
        {
            image.SetData(MaxLights, Rows, false, Image.Format.Rgbaf, bytes);
            texture.Update(image);
        }
        RenderingServer.GlobalShaderParameterSet("ms2_light_count", (float)count);
    }

    static void Put(float[] data, int w, int light, int row, float x, float y, float z, float a)
    {
        int o = (row * w + light) * 4;
        data[o] = x; data[o + 1] = y; data[o + 2] = z; data[o + 3] = a;
    }
}

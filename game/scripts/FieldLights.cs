using System;
using Godot;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// The field's point and spot lights (FieldLightSet: the xblock's PointLight / SpotLight entities) for the shaders: a float
/// texture of their values (ms2_lights.gdshaderinc's layout) and their count, as global shader parameters. The shaders
/// evaluate the client's light fragments from it; each field draw masks out the lights the client does not attach to it
/// (FieldBuilder, ms2_light_off), and characters take every light. No Godot light nodes.
/// <para>Colours are times the dimmer as the light entity gives it. MS2LightService::HandleEntityEnterWorld
/// (ms2lightservice.cpp:407-435) raises every light's dimmer by 1.1 unless HDR and the post-processing step 3 are on, and
/// SetHdr (gameoptionstruct.cpp:2081-2090) scales them back to 1.0 with HDR on; this client always runs the bloom (HDR), as
/// Field.BuildEnvironment does for the sun, so the dimmer is used as it is.</para>
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

    public static int Add(FieldLightSet set)
    {
        int n = Math.Min(set?.Lights.Count ?? 0, MaxLights);
        if (set != null && set.Lights.Count > MaxLights) GD.PushWarning($"field lights: {set.Lights.Count} lights, using the first {MaxLights}");
        int w = Math.Max(n, 1);
        var data = new float[w * Rows * 4];
        for (int i = 0; i < n; i++)
        {
            var l = set.Lights[i];
            var pos = FieldBuilder.ToGodotPoint(l.Position);
            var dir = FieldBuilder.ToGodotPoint(l.Direction).Normalized();
            float dim = l.Dimmer;
            bool ambient = l.Ambient[0] * dim != 0 || l.Ambient[1] * dim != 0 || l.Ambient[2] * dim != 0;
            Put(data, w, i, 0, pos.X, pos.Y, pos.Z, (l.Spot ? 1 : 0) + (ambient ? 2 : 0));
            Put(data, w, i, 1, l.Diffuse[0] * dim, l.Diffuse[1] * dim, l.Diffuse[2] * dim, l.Constant);
            Put(data, w, i, 2, l.Ambient[0] * dim, l.Ambient[1] * dim, l.Ambient[2] * dim, l.Linear);
            Put(data, w, i, 3, l.Specular[0] * dim, l.Specular[1] * dim, l.Specular[2] * dim, l.Quadratic);
            Put(data, w, i, 4, dir.X, dir.Y, dir.Z, l.OuterCos);
            Put(data, w, i, 5, l.InnerCos, l.Exponent, 0, 0);
        }
        var bytes = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        var img = Image.CreateFromData(w, Rows, false, Image.Format.Rgbaf, bytes);
        RenderingServer.GlobalShaderParameterSet("ms2_lights", ImageTexture.CreateFromImage(img));
        RenderingServer.GlobalShaderParameterSet("ms2_light_count", (float)n);
        return n;
    }

    static void Put(float[] data, int w, int light, int row, float x, float y, float z, float a)
    {
        int o = (row * w + light) * 4;
        data[o] = x; data[o + 1] = y; data[o + 2] = z; data[o + 3] = a;
    }
}

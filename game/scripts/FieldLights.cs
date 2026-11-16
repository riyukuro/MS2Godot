using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// The field's point and spot lights (entities MS2PointLight / MS2SpotLight of the xblock). The client attaches each as a
/// Gamebryo light effect to every scene object (characters included) and lights them with the fixed-function formula
/// colour * Dimmer / (Constant + Linear d + Quadratic d^2) out to Range, N.L, and for spots a cone term; the entity's
/// +X axis is its direction. The shaders evaluate exactly that (ms2_lights.gdshaderinc) from a float texture built here,
/// so no Godot light nodes (and no shadows, as the client casts none from them).
/// </summary>
public static class FieldLights
{
    const int MaxLights = 64;

    public static int Add(Node parent, FieldMap map)
    {
        var rows = new List<float[]>();
        foreach (var e in map.Entities)
        {
            bool spot = e.ModelName.StartsWith("MS2SpotLight", StringComparison.Ordinal);
            if (!spot && !e.ModelName.StartsWith("MS2PointLight", StringComparison.Ordinal)) continue;
            if (!e.Bool("IsVisible", true)) continue;
            float range = (float)e.Number("Range", 10000), dim = (float)e.Number("Dimmer", 1);
            if (range <= 0 || dim <= 0) continue;
            var dc = e.Numbers("DiffuseColor");
            float r = 1, g = 1, b = 1;
            if (dc != null && dc.Length >= 3) { r = (float)dc[0]; g = (float)dc[1]; b = (float)dc[2]; }
            var pos = FieldBuilder.ToGodotPoint(e.Position);
            var o = e.Transform;
            var dir = FieldBuilder.ToGodotPoint(o.Apply(new Vec3(1, 0, 0)) - o.Apply(new Vec3(0, 0, 0))).Normalized();
            float outer = (float)e.Number("OuterSpotAngle", 45), inner = (float)e.Number("InnerSpotAngle", outer);
            rows.Add(new[]
            {
                pos.X, pos.Y, pos.Z, range,
                r * dim, g * dim, b * dim, spot ? 1f : 0f,
                dir.X, dir.Y, dir.Z, MathF.Cos(Mathf.DegToRad(outer)),
                (float)e.Number("ConstantAttenuation", 1), (float)e.Number("LinearAttenuation"), (float)e.Number("QuadraticAttenuation"), MathF.Cos(Mathf.DegToRad(inner)),
                (float)Math.Max(e.Number("SpotExponent", 1), 0), 0, 0, 0,
            });
        }
        if (rows.Count > MaxLights) { GD.PushWarning($"field lights: {rows.Count} lights, using the first {MaxLights}"); rows.RemoveRange(MaxLights, rows.Count - MaxLights); }

        int n = rows.Count;
        var data = new byte[Math.Max(n, 1) * 5 * 16];
        for (int i = 0; i < n; i++)
            for (int row = 0; row < 5; row++)
                for (int k = 0; k < 4; k++)
                    BitConverter.TryWriteBytes(data.AsSpan(((row * Math.Max(n, 1) + i) * 4 + k) * 4, 4), rows[i][row * 4 + k]);
        var img = Image.CreateFromData(Math.Max(n, 1), 5, false, Image.Format.Rgbaf, data);
        RenderingServer.GlobalShaderParameterSet("ms2_lights", ImageTexture.CreateFromImage(img));
        RenderingServer.GlobalShaderParameterSet("ms2_light_count", (float)n);
        return n;
    }
}

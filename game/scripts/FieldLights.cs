using System;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// The field's point and spot lights (entities MS2PointLight / MS2SpotLight of the xblock). Gamebryo lights have
/// intensity Dimmer / (Constant + Linear d + Quadratic d^2) out to Range and shine along the entity's +X axis; here
/// they become OmniLight3D / SpotLight3D whose range is Range and whose falloff curve is fitted to that formula.
/// The client casts no shadows from them.
/// </summary>
public static class FieldLights
{
    public static int Add(Node parent, FieldMap map)
    {
        int n = 0;
        foreach (var e in map.Entities)
        {
            bool spot = e.ModelName.StartsWith("MS2SpotLight", StringComparison.Ordinal);
            if (!spot && !e.ModelName.StartsWith("MS2PointLight", StringComparison.Ordinal)) continue;
            if (!e.Bool("IsVisible", true)) continue;
            float range = (float)e.Number("Range", 10000);
            float dim = (float)e.Number("Dimmer", 1);
            double c = e.Number("ConstantAttenuation", 1), l = e.Number("LinearAttenuation"), q = e.Number("QuadraticAttenuation");
            if (range <= 0 || dim <= 0) continue;
            var dc = e.Numbers("DiffuseColor");
            var col = dc != null && dc.Length >= 3 ? new Color((float)dc[0], (float)dc[1], (float)dc[2]) : Colors.White;

            // Fit: Godot falls as (1 - d/range)^a. Match the Gamebryo ratio at half range for the exponent, and
            // the absolute level at a quarter range for the energy.
            double G(double d) => dim / Math.Max(c + l * d + q * d * d, 1e-4);
            double half = Math.Clamp(G(range / 2) / G(0), 1e-3, 0.99);
            float a = (float)Math.Clamp(Math.Log(half) / Math.Log(0.5), 0.5, 8);
            float energy = (float)Math.Min(G(range / 4) / Math.Pow(0.75, a), 16);

            Light3D light;
            if (spot)
            {
                float outer = (float)e.Number("OuterSpotAngle", 45);
                light = new SpotLight3D { SpotRange = range, SpotAngle = outer, SpotAttenuation = a, SpotAngleAttenuation = (float)Math.Max(e.Number("SpotExponent", 1), 0.1) };
            }
            else
                light = new OmniLight3D { OmniRange = range, OmniAttenuation = a };
            light.LightColor = col;
            light.LightEnergy = energy;
            light.ShadowEnabled = false;
            light.Name = e.Name ?? e.ModelName;
            parent.AddChild(light);

            var pos = FieldBuilder.ToGodotPoint(e.Position);
            if (spot)
            {
                var o = e.Transform;
                var dir = FieldBuilder.ToGodotPoint(o.Apply(new Vec3(1, 0, 0)) - o.Apply(new Vec3(0, 0, 0))).Normalized();
                light.LookAtFromPosition(pos, pos + dir, Mathf.Abs(dir.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
            }
            else light.Position = pos;
            n++;
        }
        return n;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/billboard_morph_aabb.tscn) of <see cref="ModelBuilder.BillboardAabb"/> on a mesh that is
/// both billboarded and morphed (kritias/indoor/ks_in_struc_controller_a01.nif): every vertex, morphed by its targets'
/// weight curves sampled over their keys (the Bezier segments as field_uv's key_segment draws them) and turned any way
/// about its billboard node, stays inside the bounds. Also prints how far the morph reaches past the rest-only radius.
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class BillboardMorphAabbCheck : Node
{
    const string Nif = "kritias/indoor/ks_in_struc_controller_a01.nif";

    public override void _Ready()
    {
        var fails = new List<string>();
        try { Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"billboard morph aabb: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    static float Weight(MorphAnim a, int t, float time)
    {
        if (a.Weights[t] is not { } k) return a.Static[t];
        if (time <= k.Times[0]) return k.Values[0];
        int n = k.Times.Length;
        if (time >= k.Times[n - 1]) return k.Values[n - 1];
        int j = 0;
        while (time > k.Times[j + 1]) j++;
        float u = (time - k.Times[j]) / Math.Max(k.Times[j + 1] - k.Times[j], 1e-6f);
        float p = k.Values[j], q = k.Values[j + 1];
        if (k.KeyType != 2) return p + (q - p) * u;
        float dp = k.OutTans[j], dq = k.InTans[j + 1];
        float A = 3f * (q - p) - (dq + 2f * dp), B = dp + dq - 2f * (q - p);
        return p + (dp + (A + B * u) * u) * u;
    }

    void Run(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var md = ModelData.Load(NifFile.Load(ModelBuilder.SharedSpec(Settings.NifXml), packs.Read("Resource/Model/Map", Nif)));
        var meshes = md.Meshes.Where(m => m.Billboard >= 0 && m.Morph != null).ToList();
        if (meshes.Count == 0) { fails.Add($"{Nif}: no billboarded morphed mesh"); return; }
        foreach (var m in meshes)
        {
            var a = m.Morph;
            var box = ModelBuilder.BillboardAabb(m);
            var c = box.Position + box.Size / 2;
            float e = box.Size.X / 2;
            float t0 = float.MaxValue, t1 = float.MinValue;
            foreach (var k in a.Weights) if (k != null) { t0 = Math.Min(t0, k.Times[0]); t1 = Math.Max(t1, k.Times[^1]); }
            if (t0 > t1) { t0 = 0; t1 = 0; }
            float reach = 0, rest = 0;
            for (int i = 0; i + 2 < m.Positions.Length; i += 3)
            {
                var v0 = new Vector3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]);
                rest = Math.Max(rest, (v0 - c).Length());
                for (int s = 0; s <= 4000; s++)
                {
                    float time = t0 + (t1 - t0) * s / 4000f;
                    var v = v0;
                    for (int t = 0; t < a.Deltas.Length; t++)
                        v += new Vector3(a.Deltas[t][i], a.Deltas[t][i + 1], a.Deltas[t][i + 2]) * Weight(a, t, time);
                    if (float.IsFinite(v.X + v.Y + v.Z)) reach = Math.Max(reach, (v - c).Length());
                }
            }
            GD.Print($"  {m.Name}: bounds radius {e:0.###}, morphed reach {reach:0.###}, rest reach {rest:0.###}, targets {a.Deltas.Length}, " +
                     $"ranges {string.Join(" ", Enumerable.Range(0, a.Deltas.Length).Select(t => ModelBuilder.MorphRange(a, t)))}");
            if (reach > e * 1.0001f + 1e-4f) fails.Add($"{m.Name}: a morphed vertex reaches {reach} past the bounds radius {e}");
        }
    }
}

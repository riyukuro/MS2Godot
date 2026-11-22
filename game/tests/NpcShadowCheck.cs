using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/npc_shadow.tscn) of which meshes of an npc model cast a shadow, on the treasure chest
/// object npc 11004215 (the Tria plaza chest): its model holds the chest body and large blended effect quads and planes
/// (a glow quad wider than the chest, coloured light planes, material alpha 0.2 to 2.3).
/// The client's shadow map culling (ShadowCullingProcess::Process @0x141753590, unit_141753550.cpp:86-139, with
/// FUN_141753a00 :150-157) drops every object whose own NiAlphaProperty blends unless it has an NiMaterialProperty with
/// alpha within 1e-6 of 1, so the quads cast nothing and only the chest body casts.
/// What the player should see: the chest's own shadow on the ground, no wide dark wedge from the effect quads.
/// The model is built as NpcLayer builds an npc (ModelBuilder.Build, which runs Attached on each mesh); a mesh casts when
/// it or its "_shadow" twin is set to cast. Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class NpcShadowCheck : Node3D
{
    const int ChestNpc = 11004215;

    readonly List<string> fails = new();

    public override void _Ready()
    {
        try { Check(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"npc shadow check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Check()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var row = new NpcTable(packs).Find(ChestNpc) ?? throw new InvalidOperationException($"no npc {ChestNpc}");
        // the model entry as NpcLayer.ModelFor finds it: the row's kfm, then the kfm's NIF next to it
        string kfmEntry = packs.List(NpcLayer.NpcArchive).FirstOrDefault(e =>
            e.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(e).Equals(row.Kfm, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"npc {ChestNpc}: no kfm {row.Kfm}");
        string folder = kfmEntry[..kfmEntry.LastIndexOf('/')];
        var kfm = KfmFile.Load(packs.Read(NpcLayer.NpcArchive, kfmEntry));
        string nif = kfm.Nif.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant();
        var md = builder.LoadModel(NpcLayer.NpcArchive, $"{folder}/{nif}");
        var visual = builder.Build(md, row.Kfm);
        AddChild(visual);

        // the built meshes by name, in the model's order (Build names each after its ModelMesh; Godot turns an @ in a node name into _)
        var built = new Dictionary<string, Queue<MeshInstance3D>>();
        foreach (var n in visual.FindChildren("*", nameof(MeshInstance3D), true, false))
            if (n is MeshInstance3D mi && !mi.Name.ToString().EndsWith("_shadow") && !mi.Name.ToString().EndsWith("_glow"))
                (built.TryGetValue(mi.Name, out var q) ? q : built[mi.Name] = new Queue<MeshInstance3D>()).Enqueue(mi);

        int dropped = 0, droppedCasting = 0, kept = 0, keptCasting = 0, matched = 0;
        foreach (var m in md.Meshes)
        {
            // a node name drops the characters Godot does not allow (Plane@#0 is built as Plane_#0)
            if (!built.TryGetValue(m.Name.Replace('@', '_').ValidateNodeName(), out var q) || q.Count == 0) { fails.Add($"{m.Name}: not built; built {string.Join(", ", built.Keys)}"); continue; }
            var mi = q.Dequeue();
            matched++;
            bool casts = mi.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off
                         || mi.GetChildren().OfType<MeshInstance3D>().Any(t => t.Name.ToString().EndsWith("_shadow") && t.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off);
            bool culled = m.AlphaBlendOn && !(m.Diffuse != null && MathF.Abs(m.MaterialAlpha - 1f) < 1e-6f);
            if (m.NoShadow) continue;
            if (culled) { dropped++; if (casts) { droppedCasting++; fails.Add($"{m.Name}: blended, material alpha {m.MaterialAlpha}, casts"); } }
            else { kept++; if (casts) keptCasting++; }
        }
        GD.Print($"npc shadow check: npc {ChestNpc}, {md.Meshes.Count} meshes ({matched} built); blended without material alpha 1: {dropped}, " +
                 $"{droppedCasting} casting; the others: {kept}, {keptCasting} casting");
        if (matched != md.Meshes.Count) fails.Add($"built {matched} of {md.Meshes.Count} meshes");
        if (dropped == 0) fails.Add("no blended mesh without material alpha 1: the model no longer holds the effect quads");
        if (keptCasting == 0) fails.Add("the chest body casts nothing");
    }
}

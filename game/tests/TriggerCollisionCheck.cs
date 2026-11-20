using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.Movement;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/trigger_collision.tscn) of a trigger mesh's collision on Dancing Sands (02000347), whose
/// MS2TriggerMesh objects 6001-6010 are floor tiles with PhysX meshes of their own (3001-3050 have none: they stand on
/// GeneratePhysX rock cubes, so CPhysXHelper::HasPhysicsProps is false for them and the packet leaves their floor alone).
/// A Trigger packet that hides a mesh puts its colliders in
/// group 5 (FUN_140b60e40, unit_140b602f0_gamerankingitemtail.cpp:109-121), which a PC's moves (groups 0x399) do not meet,
/// and one that shows it puts them back in the entity's PropCollisionGroup. A bare controller is dropped onto each mesh
/// that has a collider under its origin: it must stand on the shown mesh and fall past the hidden one. The field's merged
/// chunk bodies kept a hidden mesh solid. Each switch costs one layer set on the packet, none per frame.
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class TriggerCollisionCheck : Node
{
    const int FieldId = 2000347;
    const int FirstId = 6001, LastId = 6010;
    const float Above = 40f;        // the drop starts this far over the shown mesh's top
    const float Step = -5f;         // per move
    const int Moves = 40;           // 200 down: past the hidden mesh, stopped by the shown one

    FieldTriggers triggers;
    readonly List<(int Id, Vector3 Top)> probes = new();
    readonly List<string> fails = new();
    int frame;

    public override void _Ready()
    {
        try { Build(); }
        catch (Exception e) { Fail(e.ToString()); }
    }

    void Build()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var fd = FieldData.FindById(packs, FieldId) ?? throw new InvalidOperationException($"no field {FieldId}");
        var map = FieldMap.Load(packs, new FlatLibrary(packs), fd.XBlock);
        var fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
        foreach (var _ in fb.BuildSteps(map)) { }
        AddChild(fb.World);
        // the field's construction (Field.LoadField); the colliders argument is read by name so the check also builds
        // against a FieldTriggers without it
        var ctor = typeof(FieldTriggers).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var colliders = typeof(FieldBuilder).GetField("TriggerColliders")?.GetValue(fb);
        var args = new object[ctor.GetParameters().Length];
        args[0] = map.Entities;
        args[1] = fb.TriggerMeshDraws;
        if (args.Length > 2) args[2] = colliders;
        triggers = (FieldTriggers)ctor.Invoke(args);

        var world = fb.World.GlobalTransform;
        foreach (var e in map.Entities)
        {
            if (TriggerObjects.KindOf(e.HasMixin) != TriggerObjectKind.Mesh) continue;
            int id = (int)e.Number("TriggerObjectID");
            if (id < FirstId || id > LastId) continue;
            probes.Add((id, world * ModelBuilder.ToGodot(e.Transform).Origin));
        }
        if (probes.Count == 0) Fail($"no trigger mesh {FirstId}-{LastId} on {fd.XBlock}");
    }

    static byte[] Packet(IEnumerable<int> ids, bool show)
    {
        // TriggerObjects.Read: type 2 (the field entry's states), count, then per mesh object id, show, flag, fade, model
        // name (empty), skin scale
        var list = ids.ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TriggerObjects.Load);
        w.Write(list.Count);
        foreach (int id in list)
        {
            w.Write(id);
            w.Write(show);
            w.Write((byte)0);
            w.Write(0);
            w.Write((short)0);
            w.Write(1f);
        }
        return ms.ToArray();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (triggers == null || fails.Count > 0 && frame == 0) { Done(); return; }
        if (++frame < 3) return;                     // the bodies are in the space
        try { Check(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        Done();
    }

    void Check()
    {
        var space = GetViewport().World3D.DirectSpaceState;
        var ray = new PhysicsRayQueryParameters3D { CollisionMask = MoveConfig.PcGroups };
        // the shown tops: a ray down each origin's column, as the field loads (the meshes start shown)
        var tops = new List<(int Id, Vector3 Top)>();
        foreach (var (id, at) in probes)
        {
            ray.From = at + new Vector3(0, 300, 0);
            ray.To = at - new Vector3(0, 300, 0);
            var hit = space.IntersectRay(ray);
            if (hit.Count > 0) tops.Add((id, (Vector3)hit["position"]));
        }
        if (tops.Count == 0) { fails.Add("no trigger mesh has a collider under its origin"); return; }

        var ids = tops.Select(t => t.Id).Distinct().ToList();
        int applied = triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Packet(ids, false)));
        var hidden = tops.Select(t => Drop(t.Top)).ToList();
        triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(Packet(ids, true)));
        var shown = tops.Select(t => Drop(t.Top)).ToList();

        int stood = 0, fell = 0;
        for (int i = 0; i < tops.Count; i++)
        {
            float top = tops[i].Top.Y;
            if (Mathf.Abs(shown[i] - top) < 3f) stood++;
            if (hidden[i] < top - 20f) fell++;
        }
        GD.Print($"trigger collision check: {tops.Count} meshes of {probes.Count} with a collider under the origin, {applied} events; " +
                 $"shown: {stood} stood on top; hidden: {fell} fell past (first: top {tops[0].Top.Y:F1}, hidden feet {hidden[0]:F1}, shown feet {shown[0]:F1})");
        if (stood != tops.Count) fails.Add($"shown: {tops.Count - stood} of {tops.Count} did not stand on the mesh");
        if (fell != tops.Count) fails.Add($"hidden: {tops.Count - fell} of {tops.Count} stood on the hidden mesh");
    }

    /// <summary>A PC body's controller dropped from over <paramref name="top"/>; its feet height after the moves.</summary>
    float Drop(Vector3 top)
    {
        var body = new CharacterBody3D { CollisionLayer = MoveConfig.PcLayer, CollisionMask = MoveConfig.PcGroups };
        var cs = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = MoveConfig.Radius, Height = MoveConfig.Height },
            Position = new Vector3(0, MoveConfig.Height / 2, 0),
        };
        body.AddChild(cs);
        AddChild(body);
        body.GlobalPosition = top + new Vector3(0, Above, 0);
        var nx = new NxController(body, cs);
        for (int i = 0; i < Moves; i++) nx.Move(new Vector3(0, Step, 0));
        float feet = body.GlobalPosition.Y;
        RemoveChild(body);
        body.Free();
        return feet;
    }

    void Fail(string why) => fails.Add(why);

    void Done()
    {
        GD.Print($"trigger collision check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}

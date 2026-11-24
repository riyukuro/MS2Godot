using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/breakable_actors.tscn) of the breakable actors (CBreakableObject::SetState with m_bHasAnim,
/// breakableobject.cpp:270-343; FieldNpcs.BreakableActors, world/FieldBreakables) on 02000088_bf, a field of breakable
/// actors whose kfm is a map model.
/// What the player should see: every breakable actor drawn at the load, playing its idle; a broken one (state 3) plays a
/// revive sequence and stays; a hidden one (state 4) goes from view once the 500 ms fade has run, not at once; back to
/// idle (state 2) it is drawn again and its idle starts again. Each breakable with NxCollision stands in a box the pc can't
/// walk into (MakeNxBoxController, breakableobject.cpp:656-705): a ray from above meets it; it goes when broken or hidden
/// and is back at state 2.
/// Quits with 0 when it holds, else 1. Needs MS2_DATA and MS2_NIFXML (or local.cfg).
/// </summary>
public partial class BreakableActorsCheck : Node
{
    const string XBlock = "02000088_bf";
    readonly List<string> fails = new();
    FieldNpcs fnpcs;
    BreakableObjects markers;
    FieldBreakables draws;
    Node3D colliders;
    BreakableObjects.Marker target;
    FieldNpcs.TriggerActor actor;
    int frame, hideFrame = -1;

    public override void _Ready()
    {
        try { Build(); }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    void Build()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), XBlock);
        var fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
        foreach (var _ in fb.BuildSteps(map)) { }
        AddChild(fb.World);
        var people = new Node3D { Name = "Characters" };
        AddChild(people);
        markers = new BreakableObjects { Pick = _ => 0 };
        markers.AttachField(map.Entities);
        fnpcs = new FieldNpcs(packs, AssetIndex.For(packs), builder);
        fnpcs.PlaceActors(people, map);
        colliders = fb.Colliders;
        draws = new FieldBreakables(markers, fb.BreakableMeshDraws, fb.BreakableFadeDraws, colliders);   // markers attached first
        AddChild(draws.Fader);
        draws.BindActors(fnpcs.BreakableActors);
    }

    void State(byte state) =>
        markers.Apply(new BreakableUpdate(1, new[] { new BreakableEntry(target.Name, state, 0, 0, 0) }));

    public override void _PhysicsProcess(double delta)
    {
        if (markers == null || fails.Count > 0) { Done(); return; }
        frame++;
        try
        {
            if (frame == 3) Load();
            else if (hideFrame > 0 && frame == hideFrame + 6)
            {
                if (!actor.Visible) fails.Add($"{target.Name}: hidden at once by state 4, before the 500 ms fade ran");
            }
            else if (hideFrame > 0 && frame == hideFrame + 60) { Back(); Done(); }
        }
        catch (Exception e) { fails.Add(e.ToString()); }
        if (fails.Count > 0) Done();
    }

    void Load()
    {
        var withAnim = markers.Sorted.Where(m => m.Entity != null && m.HasAnim).ToList();
        int drawn = withAnim.Count(m => fnpcs.BreakableActors.ContainsKey(m.Entity));
        GD.Print($"breakable actors check: {XBlock} {markers.Count} breakables, {withAnim.Count} actors ({drawn} drawn)");
        if (withAnim.Count == 0) { fails.Add("the field has no breakable actor"); return; }
        if (drawn != withAnim.Count) fails.Add($"{withAnim.Count - drawn} breakable actors not drawn");
        foreach (var m in withAnim.Where(m => fnpcs.BreakableActors.ContainsKey(m.Entity)))
        {
            var a = fnpcs.BreakableActors[m.Entity];
            if (!a.Visible) fails.Add($"{m.Name}: hidden at the load");
            if (!string.IsNullOrEmpty(m.IdleAnim) && a.Sequence != m.IdleAnim) fails.Add($"{m.Name}: plays {a.Sequence ?? "nothing"} at the load, not {m.IdleAnim}");
        }
        CheckBoxes();
        target = withAnim.FirstOrDefault(m => fnpcs.BreakableActors.ContainsKey(m.Entity) && m.ReviveAnims.Length > 0)
                 ?? withAnim.FirstOrDefault(m => fnpcs.BreakableActors.ContainsKey(m.Entity));
        if (target == null) return;
        actor = fnpcs.BreakableActors[target.Entity];
        State(3);
        if (draws.Box(target) != null) fails.Add($"{target.Name}: the box is still up in state 3");
        string revive = target.ReviveAnims.Length > 0 ? target.ReviveAnims[0] : null;
        if (!actor.Visible) fails.Add($"{target.Name}: state 3 not drawn");
        if (revive != null && actor.Sequence != revive)
            fails.Add($"{target.Name}: state 3 plays {actor.Sequence}, not {revive}");
        State(4);
        if (draws.Box(target) != null) fails.Add($"{target.Name}: the box is up in state 4");
        hideFrame = frame;
    }

    void Back()
    {
        if (actor.Visible) fails.Add($"{target.Name}: still drawn 1 s after state 4");
        State(2);
        if (target.Physics && draws.Box(target) == null) fails.Add($"{target.Name}: no box back at state 2");
        if (!actor.Visible) fails.Add($"{target.Name}: state 2 not drawn again");
        if (!string.IsNullOrEmpty(target.IdleAnim) && actor.Sequence != target.IdleAnim) fails.Add($"{target.Name}: state 2 plays {actor.Sequence}, not {target.IdleAnim}");
        GD.Print($"breakable actors check: {target.Name} (idle {target.IdleAnim}, revive {string.Join("/", target.ReviveAnims)}) through states 3, 4 and 2");
    }

    /// <summary>Every NxCollision marker's box: the size, the centre and the layer, and a ray down from above meets it first.</summary>
    void CheckBoxes()
    {
        var withBox = markers.Sorted.Where(m => m.Entity != null && m.Physics).ToList();
        GD.Print($"breakable actors check: {withBox.Count} breakables with NxCollision, {draws.Boxes} boxes");
        if (withBox.Count == 0) { fails.Add("no breakable with NxCollision"); return; }
        if (draws.Boxes != withBox.Count) fails.Add($"{draws.Boxes} boxes for {withBox.Count} NxCollision breakables");
        var space = colliders.GetWorld3D().DirectSpaceState;
        int hit = 0;
        foreach (var m in withBox)
        {
            var body = draws.Box(m);
            if (body == null) { fails.Add($"{m.Name}: no box"); continue; }
            var c = m.BoxCenter;
            var at = new Vector3((float)c.X, (float)c.Y, (float)c.Z);
            var shape = (BoxShape3D)((CollisionShape3D)body.GetChild(0)).Shape;
            if (body.Position.DistanceTo(at) > 0.01f || shape.Size != new Vector3((float)m.Size.X, (float)m.Size.Y, (float)m.Size.Z)
                || body.CollisionLayer != 1u << m.Group)
                fails.Add($"{m.Name}: box at {body.Position} size {shape.Size} layer {body.CollisionLayer}, not {at} {m.Size} group {m.Group}");
            var q = PhysicsRayQueryParameters3D.Create(colliders.ToGlobal(at + new Vector3(0, 0, (float)m.Size.Z)), colliders.ToGlobal(at), 1u << m.Group);
            var r = space.IntersectRay(q);
            if (r.Count > 0 && (ulong)r["collider_id"] == body.GetInstanceId()) hit++;
        }
        GD.Print($"breakable actors check: a ray from above meets {hit} of {withBox.Count} boxes first");
        if (hit == 0) fails.Add("no ray from above meets a breakable's box");
    }

    bool done;

    void Done()
    {
        if (done) return;
        done = true;
        GD.Print($"breakable actors check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
        SetPhysicsProcess(false);
    }
}

using System.Collections.Generic;
using Godot;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The drawn side of the field's breakable objects (CBreakableObject::SetState @0x140ade420, breakableobject.cpp:270-343):
/// the server's Breakable state (OnBreakable, scenestategame.cpp:3667, applied by <see cref="BreakableObjects.Apply"/>)
/// shows the entity for states 2 (idle), 3 (broken), 5 (regen) and 6 (damage), and state 4 hides it with
/// FUN_140ae07d0(0): a 500 ms fade, then the entity is invisible. Each breakable entity's instances (drawn and
/// shadow-only, FieldBuilder.BreakableMeshDraws) form one <see cref="TriggerFade.Target"/>; the fade runs in a
/// <see cref="TriggerFade"/> of its own, so its ids do not meet the trigger meshes'. A show drops a running fade.
/// Nothing runs per frame unless a fade does (the fader's process is off otherwise).
/// An actor breakable (the Actor mixin, m_bHasAnim; FieldNpcs.BreakableActors, any archive) plays the sequence its state asks
/// for (<see cref="BreakableObjects.Marker.Play"/>: state 2 the idle forced, 3 a revive entry not forced; Unk_BoAnimService::
/// PlayAnimation) and is shown at once or hidden once the hide's 500 ms have run.
/// The physics box (MakeNxBoxController @0x140ae0210, breakableobject.cpp:656-705; <see cref="BreakableObjects.Marker.HasBox"/>):
/// one static box of the marker's ShapeDimensions at its position, its bottom at the position's z, turned by the rotation's
/// z, on its PropCollisionGroup's layer (bit n = group n, as the field's colliders); a broken or hidden marker's box goes
/// (ReleasePhysicsActor, :736-752).
/// A moving marker (<see cref="BreakableObjects.Marker.Step"/>, CBreakableObject::Update :359-445) raises
/// <see cref="BreakableObjects.Moved"/> when its place or turn changed: each instance is moved from its rest transform by the
/// marker's offset from PosBase and its turn from RotBase about the entity's position (SetPosition / SetRotation on the entity),
/// a hidden one keeps its zero basis at the new place, and the box goes to the new <see cref="BreakableObjects.Marker.BoxCenter"/>
/// keeping its turn (setGlobalPosition only, :399-405). The entity's own collision stays at rest, as the client's does:
/// MS2PropService::OnPropertyUpdate (ms2propservice.cpp:162-245) re-poses a prop's static actors only for a Rotation change and
/// skips a shape whose groups mask is all zero, and the NIF's shapes keep the zero mask they load with (Gamebryo
/// PropService::SetCollisionGroup, PropService.cpp:1471-1499, sets the group only; the masks come from CPhysXHelper at run time).
/// A fade running meanwhile moves its copies (<see cref="TriggerFade.Moved"/>).
/// STILL-OPEN: the camera fade's copies (world/FieldFade) of a moving instance stay where they began; an actor's 500 ms alpha fade (FUN_140ae1e80 on its root: it stays opaque until hidden); the dead effect
/// (<see cref="BreakableObjects.Marker.Effect"/>, FUN_140ae0a90 on the entity's root node, kind 1).
/// </summary>
public sealed class FieldBreakables
{
    readonly Dictionary<FieldEntity, (int Id, TriggerFade.Target Target)> targets = new();
    readonly BreakableObjects markers;
    readonly Node3D colliders;
    readonly Dictionary<BreakableObjects.Marker, StaticBody3D> boxes = new();
    readonly Dictionary<FieldEntity, Transform3D[]> rest = new();   // a moved target's rest transforms, by All's order
    IReadOnlyDictionary<FieldEntity, FieldNpcs.TriggerActor> actors = new Dictionary<FieldEntity, FieldNpcs.TriggerActor>();

    /// <summary>The node that runs the hide fades, in the tree with the field.</summary>
    public readonly TriggerFade Fader = new() { Name = "BreakableFade" };

    public int Count => targets.Count;

    /// <summary>The boxes up now.</summary>
    public int Boxes => boxes.Count;

    public FieldBreakables(BreakableObjects markers, IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws,
                           IReadOnlyDictionary<FieldEntity, List<FieldBuilder.FadeDraw>> fadeDraws, Node3D colliders = null)
    {
        this.markers = markers;
        this.colliders = colliders;
        int id = 0;
        foreach (var (e, draws) in meshDraws)
        {
            if (draws.Count == 0) continue;
            var t = new TriggerFade.Target();
            foreach (var (mm, i) in draws) t.All.Add((mm, i, mm.GetInstanceTransform(i)));
            if (fadeDraws.TryGetValue(e, out var drawn)) t.Draws.AddRange(drawn);
            targets[e] = (id++, t);
        }
        if (markers == null) return;
        markers.StateSet += OnState;
        markers.BoxChanged += SyncBox;
        markers.Moved += OnMoved;
        foreach (var m in markers.Sorted) SyncBox(m);   // the markers attached before the draws
    }

    /// <summary>The field's breakable actors: each drawn as its marker stands now (shown unless hidden), then by its states.</summary>
    public void BindActors(IReadOnlyDictionary<FieldEntity, FieldNpcs.TriggerActor> breakableActors)
    {
        actors = breakableActors ?? actors;
        if (markers == null) return;
        foreach (var (e, a) in actors)
            if (markers.Find(e.Raw?.Id) is { } m) a.Visible = m.Visible;
    }

    /// <summary>Whether the breakable entity's actor is shown (null: it has no drawn actor).</summary>
    public bool? ActorShown(FieldEntity e) => e != null && actors.TryGetValue(e, out var a) ? a.Visible : null;

    /// <summary>The sequence the breakable entity's actor plays (null: none, or no drawn actor).</summary>
    public string ActorSequence(FieldEntity e) => e != null && actors.TryGetValue(e, out var a) ? a.Sequence : null;

    /// <summary>Stops following the session's states (the field leaves).</summary>
    public void Detach()
    {
        if (markers == null) return;
        markers.StateSet -= OnState;
        markers.BoxChanged -= SyncBox;
        markers.Moved -= OnMoved;
    }

    /// <summary>The marker's box (null: none up).</summary>
    public StaticBody3D Box(BreakableObjects.Marker m) => m != null && boxes.TryGetValue(m, out var b) ? b : null;

    /// <summary>Makes or releases the marker's box as <see cref="BreakableObjects.Marker.HasBox"/> says.</summary>
    void SyncBox(BreakableObjects.Marker m)
    {
        if (colliders == null || m == null) return;
        if (!m.HasBox)
        {
            if (boxes.Remove(m, out var old) && GodotObject.IsInstanceValid(old))
            {
                old.QueueFree();
                Movement.SceneChange.Report();                 // ReleasePhysicsActor's FUN_141bc8e00 (:747)
            }
            return;
        }
        if (boxes.ContainsKey(m)) return;
        var c = m.BoxCenter;
        var body = new StaticBody3D
        {
            Name = "BreakableBox_" + m.Name,
            CollisionLayer = 1u << m.Group,
            CollisionMask = 0,
            Transform = new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad((float)m.Rotation.Z)),
                                        new Vector3((float)c.X, (float)c.Y, (float)c.Z)),
        };
        body.SetMeta(FieldBuilder.GroupMeta, m.Group);
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3((float)m.Size.X, (float)m.Size.Y, (float)m.Size.Z) } };
        shape.SetMeta(FieldBuilder.BreakableMeta, m.Name);   // the box actor's entity is the marker's
        body.AddChild(shape);
        colliders.AddChild(body);
        ColliderGroups.Set(body.GetInstanceId(), m.Group);
        boxes[m] = body;
    }

    /// <summary>The marker's place or turn changed: its instances and its box follow.</summary>
    void OnMoved(BreakableObjects.Marker m)
    {
        if (boxes.TryGetValue(m, out var body) && GodotObject.IsInstanceValid(body))
        {
            var c = m.BoxCenter;
            body.Position = new Vector3((float)c.X, (float)c.Y, (float)c.Z);
        }
        if (m.Entity == null || !targets.TryGetValue(m.Entity, out var t)) return;
        var all = t.Target.All;
        if (!rest.TryGetValue(m.Entity, out var at))
        {
            at = new Transform3D[all.Count];
            for (int i = 0; i < at.Length; i++) at[i] = all[i].Xf;
            rest[m.Entity] = at;
        }
        var move = Displacement(m);
        for (int i = 0; i < at.Length; i++)
        {
            var (mm, index, _) = all[i];
            all[i] = (mm, index, move * at[i]);
        }
        Fader.Moved(t.Id, t.Target);
    }

    /// <summary>
    /// The field-space transform from the marker's rest pose to its pose now: the turn's change about field z at the rest
    /// position, then the offset (the instances sit under the field's Z-up root, as its boxes do).
    /// </summary>
    internal static Transform3D Displacement(BreakableObjects.Marker m)
    {
        var p0 = new Vector3((float)m.PosBase.X, (float)m.PosBase.Y, (float)m.PosBase.Z);
        var p = new Vector3((float)m.Position.X, (float)m.Position.Y, (float)m.Position.Z);
        float turn = (float)(m.Rotation.Z - m.RotBase.Z);
        var basis = turn == 0f ? Basis.Identity : new Basis(Vector3.Back, Mathf.DegToRad(turn));
        return new Transform3D(basis, p - basis * p0);
    }

    /// <summary>Whether the breakable entity is drawn (null: no drawn instances).</summary>
    public bool? Shown(FieldEntity e) => e != null && targets.TryGetValue(e, out var t) ? t.Target.Shown : null;

    void OnState(string name, int state)
    {
        if (markers.Find(name) is not { Entity: { } e } m) return;
        if (actors.TryGetValue(e, out var a)) OnActorState(m, a, state);
        if (!targets.TryGetValue(e, out var t)) return;
        switch (state)
        {
            case 4: Fader.FadeEntity(t.Id, t.Target, false, 0.5f); break;
            case 2: case 3: case 5: case 6: Fader.Stop(t.Id, t.Target, true); break;
        }
    }

    void OnActorState(BreakableObjects.Marker m, FieldNpcs.TriggerActor a, int state)
    {
        if (m.Play != null) { a.PlayAnimation(m.Play, m.PlayForce); m.Played(); }
        if (state is 2 or 3 or 5 or 6) { a.Visible = true; return; }
        if (state != 4 || !Fader.IsInsideTree()) return;
        // FUN_140ae07d0(0): the fade timer's 500 ms, then IsVisible false (Update), unless another state came in between
        Fader.GetTree().CreateTimer(0.5).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(a.Root) && m.State == 4) a.Visible = false;
        };
    }
}

using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The drawn side of the field's interact objects (WorldService m_678, <see cref="LiveInteractObjects"/>): what a state change
/// does to the object's entity (CInteractMeshObject::vfunc_3, @0x1413fef20, interactobject.cpp:781-819, decided by
/// <see cref="LiveInteractObjects.MeshDraw"/>) and its state effect (CInteractObject::UpdateStateEffect, :627-634, decided by
/// <see cref="LiveInteractObjects.StateEffect"/>).
/// <list type="bullet">
/// <item>IsVisible (0xa0b6b179): an MS2InteractMesh's drawn instances (FieldBuilder.InteractMeshDraws) or an MS2InteractActor's
/// actor (FieldNpcs.InteractActors) shown or hidden at once.</item>
/// <item>The asset (FUN_1413ff690, :909-921): an MS2InteractActor plays the sequence (Unk_AnimSvc::PlayAnimation, 1.0f, false);
/// the same name again leaves it where it is, as the trigger actors' does. A watch tower (CInteractWatchTowerObject) applies it
/// only while the player is near (<see cref="LiveInteractObjects.WatchTowerAsset"/>) and never sets IsVisible or the box.</item>
/// <item>SetCollision (:925-931): MakeNxBoxController (:848-889) makes one static box of the row's ShapeDimension at the object's
/// position, its bottom at the position's z, turned by the rotation's z, its shapes in group 0 (layer 1 here); ReleaseActor
/// (:892-906) removes it. A second make while the box is up does nothing.</item>
/// <item>The state effect: a kept effect at the object's position + 1 z with its rotation and scale 1 (FUN_1411a49c0, object
/// kind 0), removed at once (FUN_1411a6060) before another shows or when it clears.</item>
/// </list>
/// The objects registered before the draws are bound (the field load's attach) are applied in <see cref="Bind"/>.
/// Nothing runs per frame.
/// STILL-OPEN: an MS2InteractMesh's asset swap (SetAssetIdProperty NifAsset): the field's batched model is not rebuilt, so a
/// mesh in state 1 or 2 keeps drawing its NifAsset where the row's reactableAssetName differs; a spawned object (InteractObject
/// mode 9) has no drawn entity here; EnableHighlight / DisableDynamicUpdate (the scene graph's update list) have no draw effect.
/// </summary>
public sealed class FieldInteractDraws
{
    readonly Dictionary<FieldEntity, (int Id, TriggerFade.Target Target)> meshes = new();
    readonly Dictionary<LiveInteractObject, StaticBody3D> boxes = new();
    readonly Dictionary<LiveInteractObject, int> effectHandles = new();   // m_nEffect, the state effect
    readonly Dictionary<LiveInteractObject, int> particles = new();       // m_nParticle, the success effect
    readonly Node3D colliders;
    IReadOnlyDictionary<FieldEntity, FieldNpcs.TriggerActor> actors = new Dictionary<FieldEntity, FieldNpcs.TriggerActor>();
    LiveInteractObjects model;
    IRegionSkillEffects effects;

    /// <summary>The node that shows and hides the meshes' instances, in the tree with the field.</summary>
    public readonly TriggerFade Fader = new() { Name = "InteractFade" };

    public int Meshes => meshes.Count;
    public int Boxes => boxes.Count;

    public FieldInteractDraws(IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws, Node3D colliders)
    {
        this.colliders = colliders;
        int id = 0;
        foreach (var (e, draws) in meshDraws)
        {
            if (draws.Count == 0) continue;
            var t = new TriggerFade.Target();
            foreach (var (mm, i) in draws) t.All.Add((mm, i, mm.GetInstanceTransform(i)));
            meshes[e] = (id++, t);
        }
    }

    /// <summary>The effects go through the field's effect service (as the trigger objects' do).</summary>
    public void Bind(IRegionSkillEffects service) => effects = service;

    /// <summary>The session's objects and the field's interact actors: every object registered so far is applied; the session
    /// then calls <see cref="Apply"/> for each state change.</summary>
    public void Bind(LiveInteractObjects objects, IReadOnlyDictionary<FieldEntity, FieldNpcs.TriggerActor> interactActors)
    {
        model = objects;
        actors = interactActors ?? actors;
        if (model == null) return;
        for (int i = 0; i < model.All.Count; i++) Apply(model.All[i]);
    }

    /// <summary>The field leaves: no more state changes come in, and the kept effects go.</summary>
    public void Detach()
    {
        if (effects != null)
        {
            foreach (var h in effectHandles.Values) if (h != 0) effects.FadeOut(h, 0);
            foreach (var h in particles.Values) if (h != 0) effects.FadeOut(h, 0);
        }
        effectHandles.Clear();
        particles.Clear();
        model = null;
    }

    /// <summary>Whether the entity's instances are drawn (null: it has none here).</summary>
    public bool? Shown(FieldEntity e) => e != null && meshes.TryGetValue(e, out var t) ? t.Target.Shown : null;

    /// <summary>Whether the object's entity is drawn: its mesh instances or its actor (null: neither is drawn here).</summary>
    public bool? Drawn(LiveInteractObject o) =>
        o?.Entity == null ? null : Shown(o.Entity) ?? (actors.TryGetValue(o.Entity, out var a) ? a.Visible : null);

    /// <summary>The object's box, or null.</summary>
    public StaticBody3D Box(LiveInteractObject o) => o != null && boxes.TryGetValue(o, out var b) ? b : null;

    /// <summary>One object's state, as the client's vfunc_3 does it (see the class summary).</summary>
    public void Apply(LiveInteractObject o)
    {
        if (model == null || o == null) return;
        if (LiveInteractObjects.MeshDraw(o, out bool visible, out string asset, out int collision))
        {
            SetVisible(o, visible);
            if (asset != null && o.AssetType == 2 && o.Entity != null && actors.TryGetValue(o.Entity, out var actor))
                actor.PlayAnimation(asset);
            if (collision > 0) MakeBox(o);
            else if (collision == 0) ReleaseBox(o);
        }
        else if (LiveInteractObjects.WatchTowerAsset(o) is { } towerAsset && o.AssetType == 2 && o.Entity != null && actors.TryGetValue(o.Entity, out var tower))
            tower.PlayAnimation(towerAsset);
        if (model.StateEffect(o, out string effect)) ShowEffect(o, effect);
    }

    /// <summary>The object's remove (InteractObject mode 10): its entity goes (hidden here), its state effect is removed and its
    /// box released (WorldService::FUN_141bc5d60 and the objects' dtors, interactobject.cpp:344-351).</summary>
    public void Remove(LiveInteractObject o)
    {
        if (o == null) return;
        SetVisible(o, false);
        ReleaseBox(o);
        if (effects != null && effectHandles.Remove(o, out int h) && h != 0) effects.FadeOut(h, 0);
        if (effects != null && particles.Remove(o, out int p) && p != 0) effects.FadeOut(p, 0);
        o.ShownState = 3;
    }

    /// <summary>
    /// <paramref name="kept"/>: CInteractObject::vfunc_12 (@0x1413fe5d0, interactobject.cpp:647-665), the row's success effect
    /// kept as m_nParticle at the object's position + 1 z with its rotation, object kind 1 (the local player its actor); a new
    /// one takes the handle without removing the old, as the client's does, and the object's release removes it. Otherwise
    /// the remove's one-shot (scenestategame.cpp:6415-6426) at the object's position, identity rotation, object kind 0.
    /// </summary>
    public void Effect(LiveInteractObject o, string effect, bool kept)
    {
        if (effects == null || o == null || string.IsNullOrEmpty(effect)) return;
        if (!kept) { effects.Insert(effect, new Xform(o.Position, Xform.Identity.R, 1), false, 0); return; }
        int h = effects.Spawn(effect, StateAt(o), false, 0, 1);
        if (h != 0) particles[o] = h;
    }

    /// <summary>The object's position + 1 z with its rotation (FromEulerAnglesXYZ of the degrees) and scale 1.</summary>
    static Xform StateAt(LiveInteractObject o)
    {
        var r = o.Rotation;
        const double d2r = Math.PI / 180;
        var q = Quat.FromAxis(0, r.X * d2r) * Quat.FromAxis(1, r.Y * d2r) * Quat.FromAxis(2, r.Z * d2r);
        return new Xform(new Vec3(o.Position.X, o.Position.Y, o.Position.Z + 1), q.ToMatrix(), 1);
    }

    void SetVisible(LiveInteractObject o, bool visible)
    {
        if (o.Entity == null) return;
        if (meshes.TryGetValue(o.Entity, out var t)) Fader.Stop(t.Id, t.Target, visible);
        if (actors.TryGetValue(o.Entity, out var a)) a.Visible = visible;
    }

    /// <summary>CInteractMeshObject::MakeNxBoxController (@0x1413ff170, interactobject.cpp:848-889).</summary>
    void MakeBox(LiveInteractObject o)
    {
        if (colliders == null || o.Row == null || boxes.ContainsKey(o)) return;
        var d = o.Row.ShapeDimension;
        var size = new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        var p = o.Position;
        var body = new StaticBody3D
        {
            Name = "InteractBox_" + o.Name,
            CollisionLayer = 1u,
            CollisionMask = 0,
            Transform = new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad((float)o.Rotation.Z)),
                                        new Vector3((float)p.X, (float)p.Y, (float)p.Z + size.Z / 2)),
        };
        body.SetMeta(FieldBuilder.GroupMeta, 0);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        colliders.AddChild(body);
        ColliderGroups.Set(body.GetInstanceId(), 0);
        boxes[o] = body;
    }

    /// <summary>CInteractMeshObject::ReleaseActor (@0x1413ff5f0, interactobject.cpp:892-906).</summary>
    void ReleaseBox(LiveInteractObject o)
    {
        if (!boxes.Remove(o, out var body)) return;
        if (GodotObject.IsInstanceValid(body)) body.QueueFree();
    }

    /// <summary>ClearStateEffect then ShowStateEffect's spawn (interactobject.cpp:451-482).</summary>
    void ShowEffect(LiveInteractObject o, string effect)
    {
        if (effects == null) return;
        if (effectHandles.Remove(o, out int h) && h != 0) effects.FadeOut(h, 0);
        if (effect.Length == 0) return;
        int handle = effects.Spawn(effect, StateAt(o), false, 0);
        if (handle != 0) effectHandles[o] = handle;
    }
}

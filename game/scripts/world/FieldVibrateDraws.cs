using System.Collections.Generic;
using Godot;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The drawn side of the field's vibrate objects (CEntityVibrator, entityvibrator.cpp): what the session's
/// <see cref="FieldVibrates"/> does to an entity, done to its instances and its own collider body (FieldBuilder.VibrateMeshDraws,
/// VibrateFadeDraws and VibrateColliders).
/// <list type="bullet">
/// <item>CollisionSet: CPhysXHelper::SetEntityCollisionGroup(entity, 5) on a break and a drop (:345, :581), (entity, 0) on the
/// return (:287, :485); the player's moves meet group 0 and not group 5 (MoveConfig.PcGroups).</item>
/// <item>Faded: WorldService::FadeEntity, a fade of the entity's drawn instances (<see cref="TriggerFade"/>).</item>
/// <item>VisibleSet: IsVisible (0xa0b6b179), the instances shown or hidden at once.</item>
/// <item>Moved: the entity's Position set to the rest position plus the vibrator's offset: each shown instance moved by it.</item>
/// <item>EffectPlayed: CEffectService::InsertEffect of the entity's XmlFilePath at the entity, object kind 7 (background,
/// entityvibrator.cpp:190); an empty name plays nothing.</item>
/// </list>
/// Nothing runs per frame: the session's vibrators call in from the world tick, and the fader runs only while a fade does.
/// The collider body stays at the rest position, as the client's does: the vibrator sets only the entity's Position while it
/// shakes or drops, and MS2PropService::OnPropertyUpdate (ms2propservice.cpp:163-245) moves a prop's static actors only for
/// a Rotation change, never one in group 5 (a broken or dropping object's); a Position change only keeps the new place for
/// the next time the prop is added (PropService::PropData::UpdateProp, NiPhysXProp::SetXform).
/// No fade starts away from rest in the ported paths: a HideVibrate puts the running vibrator back at rest before StartFall
/// (FUN_141bc92e0, worldservice_1.cpp:5067), a broken object does not shake, and the return's fade-in is at once, before its
/// drop; so a fade copy stands at the rest position.
/// STILL-OPEN: with the debris actor (FieldVibrates' STILL-OPEN) the entity fades out where the flight left it, and an object
/// whose collision group stays (m_a8) has its actors moved by the flight's Rotation updates.
/// </summary>
public sealed class FieldVibrateDraws
{
    readonly Dictionary<FieldEntity, (int Id, TriggerFade.Target Target)> targets = new();
    readonly Dictionary<FieldEntity, FieldBuilder.TriggerCollider> colliders;
    readonly FieldVibrates model;
    IRegionSkillEffects effects;

    /// <summary>The node that runs the fades, in the tree with the field.</summary>
    public readonly TriggerFade Fader = new() { Name = "VibrateFade" };

    public int Count => targets.Count;

    public FieldVibrateDraws(FieldVibrates model, IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws,
                             IReadOnlyDictionary<FieldEntity, List<FieldBuilder.FadeDraw>> fadeDraws,
                             Dictionary<FieldEntity, FieldBuilder.TriggerCollider> colliders)
    {
        this.model = model;
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
        if (model == null) return;
        model.CollisionSet = OnCollision;
        model.Faded = OnFaded;
        model.VisibleSet = OnVisible;
        model.Moved = OnMoved;
        model.EffectPlayed = OnEffect;
    }

    /// <summary>The effects go through the field's effect service (as the trigger objects' do).</summary>
    public void Bind(IRegionSkillEffects service) => effects = service;

    /// <summary>The field leaves: the session's vibrators stop calling in.</summary>
    public void Detach()
    {
        if (model == null || model.Moved != OnMoved) return;
        model.CollisionSet = null; model.Faded = null; model.VisibleSet = null; model.Moved = null; model.EffectPlayed = null;
    }

    /// <summary>Whether the entity is drawn (null: no drawn instances).</summary>
    public bool? Shown(FieldEntity e) => e != null && targets.TryGetValue(e, out var t) ? t.Target.Shown : null;

    /// <summary>The collision group the entity's own body is in (null: it has none).</summary>
    public int? Group(FieldEntity e) =>
        e != null && colliders != null && colliders.TryGetValue(e, out var c) && c.Body.HasMeta(FieldBuilder.GroupMeta) ? (int)c.Body.GetMeta(FieldBuilder.GroupMeta) : null;

    void OnCollision(FieldVibrates.Entry entry, bool on)
    {
        if (colliders != null && colliders.TryGetValue(entry.Entity, out var c)) c.SetGroup(on ? 0 : 5);
    }

    void OnFaded(FieldVibrates.Entry entry, bool show, float seconds)
    {
        if (!targets.TryGetValue(entry.Entity, out var t)) return;
        if (seconds <= 0) Fader.Stop(t.Id, t.Target, show);
        else Fader.FadeEntity(t.Id, t.Target, show, seconds);
    }

    void OnVisible(FieldVibrates.Entry entry, bool visible)
    {
        if (targets.TryGetValue(entry.Entity, out var t)) Fader.Stop(t.Id, t.Target, visible);
    }

    void OnMoved(FieldVibrates.Entry entry)
    {
        if (!targets.TryGetValue(entry.Entity, out var t) || !t.Target.Shown) return;
        var o = entry.Offset;
        var off = new Vector3(o.X, o.Z, -o.Y);
        foreach (var (mm, i, xf) in t.Target.All) mm.SetInstanceTransform(i, new Transform3D(xf.Basis, xf.Origin + off));
    }

    void OnEffect(FieldVibrates.Entry entry, string path)
    {
        if (effects == null || string.IsNullOrEmpty(path)) return;
        effects.Insert(path, entry.Entity.Transform, false, 0, Ms2.Audio.SoundMusicEffectState.CasterBackground);
    }
}

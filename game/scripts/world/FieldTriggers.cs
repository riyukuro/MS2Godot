using Godot;
using Maple2.PacketLib.Tools;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2Godot.Movement;

namespace Ms2Godot.World;

/// <summary>
/// The field's trigger objects as CTriggerService holds them: every entity with the MS2TriggerObject mixin, keyed by its
/// TriggerObjectID (property 0x6c37a861; CTriggerService::FUN_141861620, triggerservice.cpp:324), with the kind
/// TriggerObject::Create gives it. The Trigger packet's object events (<see cref="TriggerObjects"/>) are applied here;
/// the server sends the field entry's states with type 2 (the map's trigger scripts' first states, e.g. a SetMesh that
/// hides a field's lava blocks) and later changes with type 3.
/// A mesh event (TriggerObjectMesh::vfunc_2, triggeractioncli.cpp:643) shows or hides the entity
/// (WorldService::FadeEntity); hidden, its drawn and shadow instances get a zero basis, as FieldFade hides a faded copy.
/// The same event moves the entity's colliders (FUN_140b60e40, unit_140b602f0_gamerankingitemtail.cpp:109-121, called
/// at triggeractioncli.cpp:679 whatever the fade gave): an entity with PhysX props (CPhysXHelper::HasPhysicsProps,
/// physxhelper.cpp:281: a GeneratePhysX box or NIF PhysX meshes) goes to its PropCollisionGroup (0xada5c5d2) when shown and
/// to group 5 when hidden (CPhysXHelper::SetEntityCollisionGroup @0x141689ec0, physxhelper.cpp:368: the cube actor, else
/// every actor of its props). A PC moves with groups 0x399 (pc.cpp:804-805) and its ground rays use 0x389, neither with
/// bit 5, so a hidden mesh no longer stops the player. An entity without PhysX props keeps its floor (FUN_140b60e40 returns
/// first). FieldBuilder gives a trigger mesh with colliders a body of its own (<see cref="FieldBuilder.TriggerCollider"/>);
/// the switch sets that body's layer, on the packet only.
/// A rope / ladder event (TriggerObjectRope::vfunc_2 @0x140de7670, triggeractioncli.cpp:738-763) fades the entity in the
/// same way (FadeEntity, :744), sets its Climbable flag (0x4df0c0de, :745) to the event's second bool, and switches its
/// colliders by the show bool (FUN_140b60e40, :746). The flag is what PC::FUN_141659b80 (pc.cpp:3209), StateLadder::UpdateClimb
/// (stateladder.cpp:374) and StateRope::FUN_1417e1e60 (statemanager.cpp:1440) read: the field's <see cref="Climbables"/>
/// entry of the entity, bound with <see cref="Bind(Climbables)"/>. A hidden rope with the flag set stays climbable, as in the client.
/// SetEntityCollisionGroup then runs WorldService::FUN_141bc8e00 (worldservice_1.cpp:4995-5009): each PC's
/// controller gets ReportSceneChanged; NxController keeps no touched-shape state across moves for it to drop.
/// An actor event (TriggerObjectActor::vfunc_2 @0x140de74b0, triggeractioncli.cpp:716-727) sets the entity's IsVisible
/// flag (0xa0b6b179, FUN_140b60e10 at :720) to the event's bool and plays the named sequence on it (Unk_AnimSvc::PlayAnimation,
/// :723), which an unknown name leaves as it was; bound with <see cref="Bind(IEnumerable{IActor})"/>.
/// An effect event starts or removes the entity's effect through the field's effect service (<see cref="ApplyEffect"/>),
/// bound with <see cref="Bind(IRegionSkillEffects)"/>; the effects go with the field, as the object's dtor removes its handle.
/// The fade (FadeEntity, Fade tenths of a second) runs in <see cref="TriggerFade"/>; a mesh without drawn instances to
/// fade switches at once. The collision switch does not wait for it.
/// STILL-OPEN: the model swap a shown mesh with a model
/// name gets (property 0x770e5fc6 and skin scale 0x2d7cad2d); whether the server's npc ground drops a hidden mesh
/// (NpcGroundSpace keeps it; the client casts no npc ground: OnFieldAddNpc @0x141718cf0, scenestategame.cpp:3533-3584,
/// and CNpc::ApplySyncBlock, npcactorandvibrate.cpp:913,965, place an npc at the packet's position + 4 z, so the
/// server decides); the minimap mark (UIMinimapDialog::SetTriggerVisible) and the show/hide effects (0x6dd85c78 /
/// 0x6454a3bb) of a type 3 event, those of a rope / ladder event (picked by its flag, :750-753), and an actor event's
/// minimap mark (:724-726); the actor sequence's blend (PlayAnimation's 1.0f and false go to the client's own
/// NiActorManager::SetTargetAnimation, whose body is not in the recomp); the cube, sound and camera events (read, not applied); a trigger mesh drawn as an animated prop (FieldProps); a hidden
/// mesh that BGTransparencyMgr fades back in (FieldFade restores its transform).
/// </summary>
public sealed class FieldTriggers
{
    readonly Dictionary<int, TriggerObjectKind> kinds = new();
    readonly Dictionary<int, TriggerFade.Target> meshes = new();
    readonly Dictionary<int, FieldBuilder.TriggerCollider> colliders = new();
    readonly Dictionary<int, FieldEntity> entities = new();
    readonly Dictionary<int, Climbables.Climb> climbs = new();
    readonly Dictionary<int, int> effectHandles = new();
    readonly Dictionary<int, IActor> actors = new();
    IRegionSkillEffects effects;

    /// <summary>A drawn trigger actor: its entity, whether it is drawn (IsVisible) and the sequence it plays.</summary>
    public interface IActor
    {
        FieldEntity Entity { get; }
        bool Visible { get; set; }
        /// <summary>Plays the sequence of that name; false (nothing changes) when the actor has none.</summary>
        bool PlayAnimation(string sequence);
    }

    /// <summary>The node that runs the fades, in the tree with the field (null when built without fade draws).</summary>
    public readonly TriggerFade Fader;

    /// <summary>The group FUN_140b60e40 puts a hidden trigger mesh's colliders in.</summary>
    public const int HiddenGroup = 5;

    public FieldTriggers(IEnumerable<FieldEntity> entities, IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws,
                         IReadOnlyDictionary<FieldEntity, FieldBuilder.TriggerCollider> meshColliders = null,
                         IReadOnlyDictionary<FieldEntity, List<FieldBuilder.FadeDraw>> fadeDraws = null)
    {
        if (fadeDraws != null) Fader = new TriggerFade();
        foreach (var e in entities)
        {
            if (TriggerObjects.KindOf(e.HasMixin) is not { } kind) continue;
            int id = (int)e.Number("TriggerObjectID");
            kinds[id] = kind;     // CAtlMap::SetAt: a later entity with the same id replaces the earlier
            this.entities[id] = e;
            bool fades = kind is TriggerObjectKind.Mesh or TriggerObjectKind.Rope;
            if (fades && meshDraws.TryGetValue(e, out var draws) && draws.Count > 0)
            {
                var t = meshes[id] = new TriggerFade.Target();
                foreach (var (mm, i) in draws) t.All.Add((mm, i, mm.GetInstanceTransform(i)));
                if (fadeDraws != null && fadeDraws.TryGetValue(e, out var drawn)) t.Draws.AddRange(drawn);
            }
            else meshes.Remove(id);
            if (fades && meshColliders != null && meshColliders.TryGetValue(e, out var tc)) colliders[id] = tc;
            else colliders.Remove(id);
        }
    }

    /// <summary>Binds the rope / ladder objects to the field's climbables: the entry of each object's entity.</summary>
    public void Bind(Climbables field)
    {
        climbs.Clear();
        foreach (var c in field.All)
            if (c.Entity != null && TriggerObjects.KindOf(c.Entity.HasMixin) == TriggerObjectKind.Rope
                && entities.TryGetValue((int)c.Entity.Number("TriggerObjectID"), out var e) && e == c.Entity)
                climbs[(int)e.Number("TriggerObjectID")] = c;
    }

    /// <summary>Binds the field's effect service (CEffectService), which the effect objects start and remove their effects with.</summary>
    public void Bind(IRegionSkillEffects service) => effects = service;

    /// <summary>Binds the drawn trigger actors: each to the actor object of its entity.</summary>
    public void Bind(IEnumerable<IActor> drawn)
    {
        actors.Clear();
        foreach (var a in drawn)
            if (a.Entity != null && entities.TryGetValue((int)a.Entity.Number("TriggerObjectID"), out var e) && e == a.Entity
                && kinds[(int)e.Number("TriggerObjectID")] == TriggerObjectKind.Actor)
                actors[(int)e.Number("TriggerObjectID")] = a;
    }

    /// <summary>The drawn actor of the actor object with this id, or null.</summary>
    public IActor Actor(int id) => actors.TryGetValue(id, out var a) ? a : null;

    /// <summary>The handle of the effect the effect object last started (m_nHandle; 0: none).</summary>
    public int EffectHandle(int id) => effectHandles.TryGetValue(id, out int h) ? h : 0;

    /// <summary>The kind of the object with this id, or null when the field holds none.</summary>
    public TriggerObjectKind? KindOf(int id) => kinds.TryGetValue(id, out var k) ? k : null;

    /// <summary>Whether the mesh object is shown (null: no such drawn mesh); a fading one changes when its fade ends.</summary>
    public bool? MeshShown(int id) => meshes.TryGetValue(id, out var t) ? t.Shown : null;

    /// <summary>Reads one Trigger packet body and applies its object events; the count applied.</summary>
    public int Apply(ByteReader r)
    {
        int n = 0;
        foreach (var ev in TriggerObjects.Read(r, KindOf))
        {
            if (!ev.Known) { GD.PushWarning($"Trigger Object Sync Error. ObjectID:{ev.Id}"); continue; }
            if (ev.Kind == TriggerObjectKind.Effect) { if (ApplyEffect(ev)) n++; continue; }
            if (ev.Kind == TriggerObjectKind.Actor)
            {
                if (!actors.TryGetValue(ev.Id, out var a)) continue;
                a.Visible = ev.Show;            // FUN_140b60e10(entity, bOn), triggeractioncli.cpp:720
                a.PlayAnimation(ev.Text);       // PlayAnimation(id, strAnim, 1.0f, false), :721-723
                n++;
                continue;
            }
            if (ev.Kind is not (TriggerObjectKind.Mesh or TriggerObjectKind.Rope)) continue;
            bool drawn = meshes.TryGetValue(ev.Id, out var t);
            // WorldService::FadeEntity(id, bShow, nFade * 0.1f), triggeractioncli.cpp:650-651 (rope: :743-744)
            if (drawn && Fader != null && t.Draws.Count > 0) Fader.FadeEntity(ev.Id, t, ev.Show, ev.Fade * 0.1f);
            else if (drawn) TriggerFade.Switch(t, ev.Show);
            // SetCachedBoolProperty(entity, 0x4df0c0de, bFlag), triggeractioncli.cpp:745
            bool climbable = false;
            if (ev.Kind == TriggerObjectKind.Rope && climbs.TryGetValue(ev.Id, out var c)) { c.Usable = ev.Flag2; climbable = true; }
            // FUN_140b60e40(entity, bShow), triggeractioncli.cpp:679 (rope: :746): after the fade and model swap
            bool collides = colliders.TryGetValue(ev.Id, out var tc);
            if (collides) tc.SetGroup(ev.Show ? tc.Group : HiddenGroup);
            if (drawn || collides || climbable) n++;
        }
        return n;
    }

    /// <summary>
    /// TriggerObjectEffect::vfunc_2 (@0x140de7a00, triggeractioncli.cpp:785-808): removes the effect the object started
    /// before (CEffectService::FUN_1411a6060, :790-791), then, when on, starts the entity's XmlFilePath (0xdd2ac78a,
    /// GetEffectName) through FUN_1411a49c0 at <see cref="EffectAt"/> and keeps its handle (:792-807). An off event leaves
    /// the old handle, whose next removal finds nothing. The u8 and i32 the reader takes are not used.
    /// Not applied (false) when the field has no effect service bound.
    /// </summary>
    bool ApplyEffect(TriggerObjectEvent ev)
    {
        if (effects == null || !entities.TryGetValue(ev.Id, out var e)) return false;
        if (effectHandles.TryGetValue(ev.Id, out int h) && h != 0) effects.FadeOut(h, 0);
        if (ev.Show) effectHandles[ev.Id] = effects.Spawn(e.Get("XmlFilePath") ?? "", EffectAt(e), false, 0);
        return true;
    }

    /// <summary>
    /// The transform an effect object starts its effect at (triggeractioncli.cpp:794-801): GetPosition, GetScale (1 when
    /// unset) and NiMatrix3::MakeZRotation of GetAngle(entity).z. The client passes the Rotation property's z, which is in
    /// degrees, to MakeZRotation unconverted (0x140de7b1d-0x140de7b2b load it straight into the call), so it is taken as
    /// radians here too. MakeZRotation(t) is Gamebryo's clockwise matrix: rows (cos t, sin t, 0), (-sin t, cos t, 0), (0, 0, 1).
    /// </summary>
    public static Xform EffectAt(FieldEntity e)
    {
        float t = (float)e.RotationDeg.Z;
        double c = MathF.Cos(t), s = MathF.Sin(t);
        return new Xform(e.Position, new[] { c, s, 0, -s, c, 0, 0, 0, 1 }, (float)e.Number("Scale", 1));
    }
}

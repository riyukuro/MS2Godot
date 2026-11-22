using Godot;
using Maple2.PacketLib.Tools;
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
/// The fade (FadeEntity, Fade tenths of a second) runs in <see cref="TriggerFade"/>; a mesh without drawn instances to
/// fade switches at once. The collision switch does not wait for it.
/// STILL-OPEN: the model swap a shown mesh with a model
/// name gets (property 0x770e5fc6 and skin scale 0x2d7cad2d); whether the server's npc ground drops a hidden mesh
/// (NpcGroundSpace keeps it; the client casts no npc ground: OnFieldAddNpc @0x141718cf0, scenestategame.cpp:3533-3584,
/// and CNpc::ApplySyncBlock, npcactorandvibrate.cpp:913,965, place an npc at the packet's position + 4 z, so the
/// server decides); the minimap mark (UIMinimapDialog::SetTriggerVisible) and the show/hide effects (0x6dd85c78 /
/// 0x6454a3bb) of a type 3 event, and those of a rope / ladder event (picked by its flag, :750-753); the actor, effect, cube,
/// sound and camera events (read, not applied); a trigger mesh drawn as an animated prop (FieldProps); a hidden
/// mesh that BGTransparencyMgr fades back in (FieldFade restores its transform).
/// </summary>
public sealed class FieldTriggers
{
    readonly Dictionary<int, TriggerObjectKind> kinds = new();
    readonly Dictionary<int, TriggerFade.Target> meshes = new();
    readonly Dictionary<int, FieldBuilder.TriggerCollider> colliders = new();
    readonly Dictionary<int, FieldEntity> entities = new();
    readonly Dictionary<int, Climbables.Climb> climbs = new();

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
}

using Godot;
using Maple2.PacketLib.Tools;
using Ms2.Formats.World;
using Ms2.Net.Packets;

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
/// SetEntityCollisionGroup then runs WorldService::FUN_141bc8e00 (worldservice_1.cpp:4995-5009): each PC's
/// controller gets ReportSceneChanged; NxController keeps no touched-shape state across moves for it to drop.
/// STILL-OPEN: the fade over Fade tenths of a second (shown/hidden at once here); the model swap a shown mesh with a model
/// name gets (property 0x770e5fc6 and skin scale 0x2d7cad2d); whether the server's npc ground drops a hidden mesh
/// (NpcGroundSpace keeps it; the client casts no npc ground: OnFieldAddNpc @0x141718cf0, scenestategame.cpp:3533-3584,
/// and CNpc::ApplySyncBlock, npcactorandvibrate.cpp:913,965, place an npc at the packet's position + 4 z, so the
/// server decides); the minimap mark (UIMinimapDialog::SetTriggerVisible) and the show/hide effects (0x6dd85c78 /
/// 0x6454a3bb) of a type 3 event; the other kinds' events (read, not applied); a trigger mesh drawn as an animated prop (FieldProps); a hidden
/// mesh that BGTransparencyMgr fades back in (FieldFade restores its transform).
/// </summary>
public sealed class FieldTriggers
{
    readonly Dictionary<int, TriggerObjectKind> kinds = new();
    readonly Dictionary<int, List<(MultiMesh Mm, int Index, Transform3D Xf)>> meshes = new();
    readonly Dictionary<int, FieldBuilder.TriggerCollider> colliders = new();

    /// <summary>The group FUN_140b60e40 puts a hidden trigger mesh's colliders in.</summary>
    public const int HiddenGroup = 5;

    public FieldTriggers(IEnumerable<FieldEntity> entities, IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws,
                         IReadOnlyDictionary<FieldEntity, FieldBuilder.TriggerCollider> meshColliders = null)
    {
        foreach (var e in entities)
        {
            if (TriggerObjects.KindOf(e.HasMixin) is not { } kind) continue;
            int id = (int)e.Number("TriggerObjectID");
            kinds[id] = kind;     // CAtlMap::SetAt: a later entity with the same id replaces the earlier
            if (kind == TriggerObjectKind.Mesh && meshDraws.TryGetValue(e, out var draws) && draws.Count > 0)
            {
                if (!meshes.TryGetValue(id, out var list)) meshes[id] = list = new();
                else list.Clear();
                foreach (var (mm, i) in draws) list.Add((mm, i, mm.GetInstanceTransform(i)));
            }
            else meshes.Remove(id);
            if (kind == TriggerObjectKind.Mesh && meshColliders != null && meshColliders.TryGetValue(e, out var tc)) colliders[id] = tc;
            else colliders.Remove(id);
        }
    }

    /// <summary>The kind of the object with this id, or null when the field holds none.</summary>
    public TriggerObjectKind? KindOf(int id) => kinds.TryGetValue(id, out var k) ? k : null;

    /// <summary>Whether the mesh object's instances are shown (null: no such drawn mesh).</summary>
    public bool? MeshShown(int id) =>
        meshes.TryGetValue(id, out var l) && l.Count > 0 ? l[0].Mm.GetInstanceTransform(l[0].Index).Basis != new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero) : null;

    /// <summary>Reads one Trigger packet body and applies its object events; the count applied.</summary>
    public int Apply(ByteReader r)
    {
        int n = 0;
        foreach (var ev in TriggerObjects.Read(r, KindOf))
        {
            if (!ev.Known) { GD.PushWarning($"Trigger Object Sync Error. ObjectID:{ev.Id}"); continue; }
            if (ev.Kind != TriggerObjectKind.Mesh) continue;
            bool drawn = meshes.TryGetValue(ev.Id, out var list);
            if (drawn)
                foreach (var (mm, i, xf) in list)
                    mm.SetInstanceTransform(i, ev.Show ? xf : new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), xf.Origin));
            // FUN_140b60e40(entity, bShow), triggeractioncli.cpp:679: after the fade and model swap
            bool collides = colliders.TryGetValue(ev.Id, out var tc);
            if (collides) tc.SetGroup(ev.Show ? tc.Group : HiddenGroup);
            if (drawn || collides) n++;
        }
        return n;
    }
}

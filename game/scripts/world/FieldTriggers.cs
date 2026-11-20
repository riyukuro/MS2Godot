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
/// STILL-OPEN: the fade over Fade tenths of a second (shown/hidden at once here); the model swap a shown mesh with a model
/// name gets (property 0x770e5fc6 and skin scale 0x2d7cad2d); the collision group the client sets (FUN_140b60e40,
/// unit_140b602f0_gamerankingitemtail.cpp:109: group 5 hidden, the entity's 0xada5c5d2 group shown; movement/ground's);
/// the minimap mark (UIMinimapDialog::SetTriggerVisible) and the show/hide effects (0x6dd85c78 / 0x6454a3bb) of a type 3
/// event; the other kinds' events (read, not applied); a trigger mesh drawn as an animated prop (FieldProps); a hidden
/// mesh that BGTransparencyMgr fades back in (FieldFade restores its transform).
/// </summary>
public sealed class FieldTriggers
{
    readonly Dictionary<int, TriggerObjectKind> kinds = new();
    readonly Dictionary<int, List<(MultiMesh Mm, int Index, Transform3D Xf)>> meshes = new();

    public FieldTriggers(IEnumerable<FieldEntity> entities, IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws)
    {
        foreach (var e in entities)
        {
            if (TriggerObjects.KindOf(e.HasMixin) is not { } kind) continue;
            int id = (int)e.Number("TriggerObjectID");
            kinds[id] = kind;     // CAtlMap::SetAt: a later entity with the same id replaces the earlier
            if (kind == TriggerObjectKind.Mesh && meshDraws.TryGetValue(e, out var draws))
            {
                if (!meshes.TryGetValue(id, out var list)) meshes[id] = list = new();
                else list.Clear();
                foreach (var (mm, i) in draws) list.Add((mm, i, mm.GetInstanceTransform(i)));
            }
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
            if (ev.Kind != TriggerObjectKind.Mesh || !meshes.TryGetValue(ev.Id, out var list)) continue;
            foreach (var (mm, i, xf) in list)
                mm.SetInstanceTransform(i, ev.Show ? xf : new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), xf.Origin));
            n++;
        }
        return n;
    }
}

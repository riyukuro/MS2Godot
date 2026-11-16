using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot.World;

/// <summary>
/// Characters of a field that need no server:
/// - Actor entities (model MS2Actor, Actor mixin): placed by the client itself as part of the field's entity set. KfmAsset names
///   the npc model (an Npc pack kfm) and InitialSequence the sequence it plays (Idle_A, Sit_Chair_Idle_A, ...), looped.
/// - Npc spawn points (SpawnPointNPC mixin): the client does not place these; the server spawns the npcs (FieldAddNpc) at field
///   creation when IsSpawnOnFieldCreate is set. The offline sandbox stands in for that: one npc per spawn point, the npc id from
///   the NpcList property (or the flat model name, NNNNNNNN_), its model from the npc table, idle from its normal action list.
/// </summary>
public sealed class FieldNpcs
{
    public const string NpcArchive = "Resource/Model/Npc";

    readonly PackSet packs;
    readonly AssetIndex assets;
    readonly ModelBuilder builder;
    readonly Dictionary<string, string> kfmByName = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (ModelData md, KfmFile kfm, string folder)> models = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string, string), Godot.Animation> anims = new();
    public int Actors, Npcs, Missing, Masked;

    /// <summary>A server stand-in npc: its row, node and where its head is.</summary>
    public sealed class Placed
    {
        public NpcRow Row;
        public Node3D Root, Body;
        public float Height;          // top of the model above the root, game units
        public Vector3 Position => Root.GlobalPosition;
    }
    public readonly List<Placed> Spawned = new();

    public FieldNpcs(PackSet packs, AssetIndex assets, ModelBuilder builder)
    {
        this.packs = packs; this.assets = assets; this.builder = builder;
        if (!builder.TextureArchives.Contains(NpcArchive)) builder.TextureArchives = builder.TextureArchives.Append(NpcArchive).ToArray();
    }

    (ModelData md, KfmFile kfm, string folder)? Model(string kfmEntry)
    {
        if (kfmEntry == null) return null;
        if (models.TryGetValue(kfmEntry, out var m)) return m.md == null ? null : m;
        m = default;
        try
        {
            string folder = kfmEntry[..kfmEntry.LastIndexOf('/')];
            var kfm = KfmFile.Load(packs.Read(NpcArchive, kfmEntry));
            string nif = kfm.Nif.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant();
            nif = $"{folder}/{Path.GetFileName(nif)}";
            if (packs.Exists(NpcArchive, nif)) m = (builder.LoadModel(NpcArchive, nif), kfm, folder);
        }
        catch (Exception ex) { GD.PushWarning($"npc model {kfmEntry}: {ex.Message}"); }
        models[kfmEntry] = m;
        return m.md == null ? null : m;
    }

    Godot.Animation Anim((ModelData md, KfmFile kfm, string folder) m, string seqName)
    {
        var seq = m.kfm.ByName(seqName);
        if (seq == null) return null;
        var slot = (m.folder, seq.Name);
        if (anims.TryGetValue(slot, out var a)) return a;
        a = null;
        string entry = $"{m.folder}/{Path.GetFileName(seq.KfPath.Replace('\\', '/')).ToLowerInvariant()}";
        try
        {
            if (packs.Exists(NpcArchive, entry))
            {
                a = ModelBuilder.BuildAnimation(KfAnimation.Load(NifFile.Load(builder.Spec, packs.Read(NpcArchive, entry))), m.md);
                a.LoopMode = Godot.Animation.LoopModeEnum.Linear;
            }
        }
        catch (Exception ex) { GD.PushWarning($"kf {entry}: {ex.Message}"); }
        return anims[slot] = a;
    }

    Node3D Place(Node3D parent, (ModelData md, KfmFile kfm, string folder) m, string name, FieldEntity e, float scale, IEnumerable<string> sequences)
    {
        var body = builder.Build(m.md, name.ValidateNodeName());
        var root = new Node3D { Name = body.Name };
        body.Name = "Body";
        root.AddChild(body);
        root.Position = FieldBuilder.ToGodotPoint(e.Position);
        // the model faces MS2 -Y; the entity's Z rotation turns it (as the player spawn: yaw about Godot +Y)
        body.Rotation = new Vector3(0, Mathf.DegToRad((float)e.RotationDeg.Z), 0);
        body.Scale = new Vector3(scale, scale, scale);
        parent.AddChild(root);
        var player = body.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        foreach (var s in sequences)
        {
            var a = Anim(m, s);
            if (a == null) continue;
            player.GetAnimationLibrary("").AddAnimation("idle", a);
            player.Play("idle");
            break;
        }
        return root;
    }

    /// <summary>The field's actor entities (client placed).</summary>
    public void PlaceActors(Node3D parent, FieldMap map)
    {
        foreach (var e in map.Entities)
        {
            if (e.ModelName != "MS2Actor" && !e.HasMixin("Actor")) continue;
            if (!e.Bool("IsVisible", true)) continue;
            var (archive, entry) = assets.Resolve(e.Get("KfmAsset"));
            var m = archive == NpcArchive ? Model(entry) : null;
            if (m == null) { Missing++; continue; }
            var seq = e.Get("InitialSequence");
            Place(parent, m.Value, e.Name ?? "Actor", e, (float)e.Scale, new[] { seq, "Idle_A" }.Where(s => !string.IsNullOrEmpty(s)));
            Actors++;
        }
    }

    /// <summary>Server stand-in: the npcs of the spawn points that spawn on field creation.</summary>
    public void SpawnNpcs(Node3D parent, FieldMap map, NpcTable table, int fieldId = 0, QuestMaskTable masks = null)
    {
        if (kfmByName.Count == 0)
            foreach (var x in packs.List(NpcArchive))
                if (x.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)) kfmByName.TryAdd(Path.GetFileNameWithoutExtension(x), x);
        foreach (var e in map.Entities)
        {
            if (!e.HasMixin("SpawnPointNPC") || !e.Bool("IsSpawnOnFieldCreate", true) || !e.Bool("IsVisible", true)) continue;
            foreach (int id in NpcIds(e))
            {
                var row = table.Find(id);
                // CQuestMaskTable::CheckNpc: a masked npc shows only within its quest stretch; a new character has no quests
                var mask = masks?.Npc(fieldId, id);
                if (mask != null && !QuestMaskTable.Check(mask, _ => 0)) { Masked++; break; }
                var m = row?.Kfm != null && kfmByName.TryGetValue(row.Kfm, out var ke) ? Model(ke) : null;
                if (m == null) { Missing++; continue; }
                var idles = row.IdleActions.Where(a => a.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)).Append("Idle_A");
                var root = Place(parent, m.Value, $"Npc_{id}", e, row.Scale, idles);
                var body = root.GetNode<Node3D>("Body");
                Spawned.Add(new Placed { Row = row, Root = root, Body = body, Height = ModelTop(body) });
                Npcs++;
                break;      // one body per point (NpcCount is how many the server keeps alive there)
            }
        }
    }

    /// <summary>Highest point of the model's meshes (bind pose) above the root.</summary>
    static float ModelTop(Node3D body)
    {
        float top = 0;
        foreach (var mi in body.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            var a = mi.Mesh.GetAabb();
            var xf = RelativeTo(mi, body.GetParent<Node3D>());
            for (int i = 0; i < 8; i++) top = Math.Max(top, (xf * a.GetEndpoint(i)).Y);
        }
        return top;
    }

    static Transform3D RelativeTo(Node3D n, Node3D ancestor)
    {
        var xf = Transform3D.Identity;
        for (Node p = n; p != null && p != ancestor; p = p.GetParent())
            if (p is Node3D q) xf = q.Transform * xf;
        return xf;
    }

    /// <summary>NpcList keys (npc id -> count); without an override, the flat model name carries the id (11000169_).</summary>
    public static IEnumerable<int> NpcIds(FieldEntity e)
    {
        if (e.Raw.Sets.TryGetValue("NpcList", out var sets))
            foreach (var (index, _) in sets)
                if (int.TryParse(index, out int id) && id > 0) yield return id;
        var mn = e.ModelName.TrimEnd('_');
        if (int.TryParse(mn, out int fromModel)) yield return fromModel;
    }
}

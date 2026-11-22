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

    /// <summary>
    /// The client raises a server npc's packet z by 4 before placing it: CSceneStateGame::OnFieldAddNpc (@0x141718cf0,
    /// kPos.z += 4.0f) and CNpc::ApplySyncBlock (@0x1410918e0, ptPos.z + 4.0f), then vfunc_19 sets the entity and its
    /// controller's feet there (PCBase::vfunc_19). Pets get the same in OnFieldAddPet (@0x141719a60). The stand-in spawns
    /// below are the server's npcs, so they stand at the spawn point's z + 4; field actors are client-side and do not.
    /// </summary>
    public const float PacketLift = 4f;

    /// <summary>The ground cast of <see cref="SpawnNpcs"/>: from this far above the spawn point (half a cube, which covers the
    /// stairs' +15) down to this far below it (Tria's deepest case, guard_crossbow_05, is 151 below its point); and how far
    /// under the ground the official server puts an npc.</summary>
    public const float GroundProbeUp = 75f, GroundProbeDown = 1500f, GroundBelow = 1f;

    readonly PackSet packs;
    readonly AssetIndex assets;
    readonly ModelBuilder builder;
    readonly Dictionary<string, string> kfmByName = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (ModelData md, KfmFile kfm, string folder, string archive)> models = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string, string, bool), Godot.Animation> anims = new();
    public int Actors, Npcs, Missing, Masked;

    /// <summary>A server stand-in npc: its row, node and where its head is.</summary>
    public sealed class Placed
    {
        public NpcRow Row;
        public Node3D Root, Body;
        public float Height;          // top of the model above the root, game units
        public bool Mob;              // a region spawn monster (not talkable)
        public int ObjectId;          // a live npc's FieldAddNpc object id (the talk's npc id on the wire); 0 offline
        public Vector3 Position => Root.GlobalPosition;
    }
    public readonly List<Placed> Spawned = new();

    public FieldNpcs(PackSet packs, AssetIndex assets, ModelBuilder builder)
    {
        this.packs = packs; this.assets = assets; this.builder = builder;
        BlobShadow.Load(packs);
        if (!builder.TextureArchives.Contains(NpcArchive)) builder.TextureArchives = builder.TextureArchives.Append(NpcArchive).ToArray();
    }

    (ModelData md, KfmFile kfm, string folder, string archive)? Model(string archive, string kfmEntry)
    {
        if (kfmEntry == null) return null;
        string key = archive + "/" + kfmEntry;
        if (models.TryGetValue(key, out var m)) return m.md == null ? null : m;
        m = default;
        try
        {
            string folder = kfmEntry[..kfmEntry.LastIndexOf('/')];
            var kfm = KfmFile.Load(packs.Read(archive, kfmEntry));
            string nif = kfm.Nif.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant();
            nif = $"{folder}/{Path.GetFileName(nif)}";
            if (packs.Exists(archive, nif)) m = (builder.LoadModel(archive, nif), kfm, folder, archive);
        }
        catch (Exception ex) { GD.PushWarning($"actor model {kfmEntry}: {ex.Message}"); }
        models[key] = m;
        return m.md == null ? null : m;
    }

    /// <summary>The kfm's sequence of that name as an animation, or null. An npc's loops; with <paramref name="byCycle"/>
    /// (a trigger actor's) it loops as the kf's cycle type says, as ReactorLayer plays a reactor's.</summary>
    Godot.Animation Anim((ModelData md, KfmFile kfm, string folder, string archive) m, string seqName, bool byCycle = false)
    {
        var seq = byCycle ? m.kfm.Sequences.FirstOrDefault(s => s.Name == seqName) : m.kfm.ByName(seqName);
        if (seq == null) return null;
        var slot = (m.archive + "/" + m.folder, seq.Name, byCycle);
        if (anims.TryGetValue(slot, out var a)) return a;
        a = null;
        string entry = $"{m.folder}/{Path.GetFileName(seq.KfPath.Replace('\\', '/')).ToLowerInvariant()}";
        try
        {
            if (packs.Exists(m.archive, entry))
            {
                var kf = KfAnimation.Load(NifFile.Load(builder.Spec, packs.Read(m.archive, entry)));
                a = ModelBuilder.BuildAnimation(kf, m.md);
                a.LoopMode = !byCycle ? Godot.Animation.LoopModeEnum.Linear
                    : kf.Cycle switch { 1 => Godot.Animation.LoopModeEnum.Pingpong, 2 => Godot.Animation.LoopModeEnum.None, _ => Godot.Animation.LoopModeEnum.Linear };
            }
        }
        catch (Exception ex) { GD.PushWarning($"kf {entry}: {ex.Message}"); }
        return anims[slot] = a;
    }

    Node3D Place(Node3D parent, (ModelData md, KfmFile kfm, string folder, string archive) m, string name, FieldEntity e, float scale, IEnumerable<string> sequences, float? yawDeg = null, float shadowRadius = 0, float lift = 0, string shadowKind = null)
    {
        var body = builder.Build(m.md, name.ValidateNodeName());
        var root = new Node3D { Name = body.Name };
        body.Name = "Body";
        root.AddChild(body);
        root.Position = FieldBuilder.ToGodotPoint(e.Position) + new Vector3(0, lift, 0);
        // the model faces MS2 -Y; the entity's Z rotation turns it (as the player spawn: yaw about Godot +Y)
        body.Rotation = new Vector3(0, Mathf.DegToRad(yawDeg ?? (float)e.RotationDeg.Z), 0);
        body.Scale = new Vector3(scale, scale, scale);
        // the npc's CObjectShadow (FieldShadows.NpcKind), sized by the model's shadowScale and lifted 1
        if (shadowKind != null) FieldShadows.Character(root, body, shadowKind, shadowRadius, 1f);
        parent.AddChild(root);
        // the npc keeps animating while off screen (observed, not cited): no per-npc off-screen pause was found in the client.
        // The update gate found is COptimizer::IsUpdateEnabled (optimizer.cpp:639), a global switch (both m_nUpdateOn flags)
        // read by the ground-effect and magic code, not a visibility test.
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

    /// <summary>
    /// A trigger actor (an Actor entity with the MS2TriggerObject mixin, TriggerObjectActor): its kfm may be a map model
    /// (02000241's quest doors: Resource/Model/Map/.../co_quest_door_a01_ani.kfm). It is drawn even when its IsVisible
    /// property (0xa0b6b179) is unset, hidden, as the Trigger packet's actor events set that flag and play a sequence
    /// (<see cref="FieldTriggers"/>). Its sequences loop as their kf's cycle type says.
    /// </summary>
    public sealed class TriggerActor : FieldTriggers.IActor
    {
        readonly FieldNpcs owner;
        readonly (ModelData md, KfmFile kfm, string folder, string archive) model;
        readonly AnimationPlayer player;
        public FieldEntity Entity { get; }
        public readonly Node3D Root;

        /// <summary>The sequence it plays (m_currentSequenceID's name), null before the first.</summary>
        public string Sequence { get; private set; }

        internal TriggerActor(FieldNpcs owner, (ModelData, KfmFile, string, string) model, FieldEntity e, Node3D root)
        {
            this.owner = owner; this.model = model; Entity = e; Root = root;
            player = root.GetNodeOrNull<AnimationPlayer>("Body/Model/AnimationPlayer");
        }

        public bool Visible { get => Root.Visible; set => Root.Visible = value; }

        /// <summary>
        /// Unk_AnimSvc::PlayAnimation (animationmanager.cpp:389-405): an unknown sequence name changes nothing (false);
        /// a known one becomes the current sequence and the target animation. The client asks with bForce false
        /// (triggeractioncli.cpp:723), and Gamebryo's NiActorManager::SetTargetAnimation does not restart the sequence that
        /// is already the target, so the same name again leaves it where it is (a finished "Opened" stays open).
        /// </summary>
        public bool PlayAnimation(string sequence)
        {
            if (sequence != null && sequence == Sequence) return true;
            var a = player == null || string.IsNullOrEmpty(sequence) ? null : owner.Anim(model, sequence, byCycle: true);
            if (a == null) return false;
            var name = new StringName(sequence.ValidateNodeName());
            var lib = player.GetAnimationLibrary("");
            if (!lib.HasAnimation(name)) lib.AddAnimation(name, a);
            player.Play(name);
            Sequence = sequence;
            return true;
        }
    }

    /// <summary>The trigger actors <see cref="PlaceActors"/> drew.</summary>
    public readonly List<TriggerActor> TriggerActors = new();

    /// <summary>The field's actor entities (client placed).</summary>
    public void PlaceActors(Node3D parent, FieldMap map)
    {
        foreach (var e in map.Entities)
        {
            if (e.ModelName != "MS2Actor" && !e.HasMixin("Actor")) continue;
            bool trigger = Ms2.Net.Packets.TriggerObjects.KindOf(e.HasMixin) == Ms2.Net.Packets.TriggerObjectKind.Actor;
            bool visible = e.Bool("IsVisible", true);
            if (!visible && !trigger) continue;
            var (archive, entry) = assets.Resolve(e.Get("KfmAsset"));
            var m = archive == NpcArchive || (trigger && archive != null) ? Model(archive, entry) : null;
            if (m == null) { Missing++; continue; }
            var seq = e.Get("InitialSequence");
            if (trigger)
            {
                var root = Place(parent, m.Value, e.Name ?? "Actor", e, (float)e.Scale, Array.Empty<string>());
                var t = new TriggerActor(this, m.Value, e, root) { Visible = visible };
                t.PlayAnimation(seq);
                TriggerActors.Add(t);
            }
            else Place(parent, m.Value, e.Name ?? "Actor", e, (float)e.Scale, new[] { seq, "Idle_A" }.Where(s => !string.IsNullOrEmpty(s)));
            Actors++;
        }
    }

    /// <summary>Server stand-in: the npcs of the spawn points that spawn on field creation.</summary>
    /// <remarks>
    /// The client places a FieldAddNpc at the packet's z + PacketLift, with no snap of its own (OnFieldAddNpc @0x141718cf0;
    /// GameObject::vfunc_19 stores the point, no sweep or fall for npcs), so the packet's z is the server's choice. The
    /// official server sends an npc at the ground under its spawn point, 1 unit down, not at the spawn point's z; this was
    /// checked against the official server's packets for Tria (02000001). Most spawn points sit on their block top, so
    /// they go 1 down. The stair spear guards 11000451_spear_03_Left/Right have their point 15 below the stair surface
    /// and go to 1 under it, so the ground is used upward too. 11000452_guard_crossbow_05 has its point at 4200, the top of
    /// the block one cube east, and goes to 1 under its own 4050 floor. The stand-in casts down on the server's ground
    /// (NpcGroundSpace) from GroundProbeUp above the point and uses the first hit - 1, which gives all three cases. With
    /// no hit within GroundProbeDown the point's own z is used. Entities with DisableCollision set count as ground: the
    /// official server stands npcs on such a floor, which the client's world leaves out (see FieldBuilder.BuildSteps).
    /// Fluids do not count.
    /// STILL-OPEN: what ground source the official server uses (only its result is known), why a few points stand on a
    /// surface other than the first one (a box the point starts inside, a vibrate prop passed through), and whether
    /// region spawn monsters get the same.
    /// </remarks>
    public void SpawnNpcs(Node3D parent, FieldMap map, NpcTable table, int fieldId = 0, QuestMaskTable masks = null)
    {
        if (kfmByName.Count == 0)
            foreach (var x in packs.List(NpcArchive))
                if (x.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)) kfmByName.TryAdd(Path.GetFileNameWithoutExtension(x), x);
        var world = parent.GetWorld3D();
        foreach (var e in map.Entities)
        {
            if (!e.HasMixin("SpawnPointNPC") || !e.Bool("IsSpawnOnFieldCreate", true) || !e.Bool("IsVisible", true)) continue;
            foreach (int id in NpcIds(e))
            {
                var row = table.Find(id);
                // CQuestMaskTable::CheckNpc: a masked npc shows only within its quest stretch; a new character has no quests
                if (masks != null && !masks.CheckNpc(fieldId, id, QuestMaskTable.NoQuests)) { Masked++; break; }
                var m = row?.Kfm != null && kfmByName.TryGetValue(row.Kfm, out var ke) ? Model(NpcArchive, ke) : null;
                if (m == null) { Missing++; continue; }
                var idles = row.IdleActions.Where(a => a.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)).Append("Idle_A");
                var root = Place(parent, m.Value, $"Npc_{id}", e, row.Scale, idles, null, row.ShadowScale, GroundOffset(world, e) + PacketLift, FieldShadows.NpcKind(row));
                var body = root.GetNode<Node3D>("Body");
                Spawned.Add(new Placed { Row = row, Root = root, Body = body, Height = ModelTop(body) });
                Npcs++;
                break;      // one body per point (NpcCount is how many the server keeps alive there)
            }
        }
    }

    /// <summary>The server's z change for a spawn point: the first ground under it (NpcGroundSpace) - GroundBelow, or 0
    /// when the cast hits nothing.</summary>
    static float GroundOffset(World3D world, FieldEntity e)
    {
        var at = FieldBuilder.ToGodotPoint(e.Position);
        return NpcGroundSpace.Hit(world, at + Vector3.Up * GroundProbeUp, at + Vector3.Down * GroundProbeDown) is Vector3 ground
            ? ground.Y - GroundBelow - at.Y : 0;
    }

    public int Mobs;

    /// <summary>
    /// Server stand-in: monsters of the field's region spawns (MS2RegionSpawn entities). The field's mapspawntag row for the
    /// entity's SpawnPointID names tags; the npcs those tags pick (npcregionspawndata, FindSpawnResult) are drawn by weight, one
    /// per point, idle in place. Facing is the entity's Z rotation only when UseRotAsSpawnDir is set, else 0. Fields with no rows (towns) get none.
    /// </summary>
    public void SpawnMobs(Node3D parent, FieldMap map, NpcTable table, RegionSpawnTable spawns, int fieldId)
    {
        if (fieldId == 0 || !spawns.HasRegions(fieldId)) return;
        if (kfmByName.Count == 0)
            foreach (var x in packs.List(NpcArchive))
                if (x.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)) kfmByName.TryAdd(Path.GetFileNameWithoutExtension(x), x);
        foreach (var e in map.Entities)
        {
            if (!e.HasMixin("MS2RegionSpawnBase") || !int.TryParse(e.Get("SpawnPointID"), out int spawnId)) continue;
            var region = spawns.Find(fieldId, spawnId);
            if (region == null || region.Population <= 0) continue;
            var cands = spawns.Candidates(region).Where(c => table.Find(c.NpcId)?.Kfm != null).ToList();
            if (cands.Count == 0) continue;
            // the server draws by weight at runtime; the stand-in draws once per point, repeatably
            var rng = new Random(fieldId * 100003 + spawnId);
            int pick = rng.Next(cands.Sum(c => Math.Max(1, c.Weight)));
            int id = cands[^1].NpcId;
            foreach (var c in cands) { pick -= Math.Max(1, c.Weight); if (pick < 0) { id = c.NpcId; break; } }
            var row = table.Find(id);
            var m = kfmByName.TryGetValue(row.Kfm, out var ke) ? Model(NpcArchive, ke) : null;
            if (m == null) { Missing++; GD.Print($"mob {id} ({row.Kfm}): no model"); continue; }
            var idles = row.IdleActions.Where(a => a.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)).Append("Idle_A");
            float yaw = e.Bool("UseRotAsSpawnDir") ? (float)e.RotationDeg.Z : 0;
            var root = Place(parent, m.Value, $"Mob_{id}_{spawnId}", e, row.Scale, idles, yaw, row.ShadowScale, PacketLift, FieldShadows.NpcKind(row));
            var body = root.GetNode<Node3D>("Body");
            Spawned.Add(new Placed { Row = row, Root = root, Body = body, Height = ModelTop(body), Mob = true });
            Mobs++;
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

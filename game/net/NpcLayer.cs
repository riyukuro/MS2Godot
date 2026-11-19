using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// Npcs and mobs of a replayed field. A FieldAddNpc names an npc id; the npc row (Xml pack npcdata) names the model (kfm), the model folder in the
/// Npc pack (NN/NN/NNNNNNNN/) holds the kfm (sequence ids -> kf files), the nif and the kf animations. NpcControl blocks then move the body and
/// say which sequence id plays. Idle, walk and run style sequences loop; the others play once and fall back to idle.
/// </summary>
public sealed class NpcLayer
{
    public const string NpcArchive = "Resource/Model/Npc";

    sealed class Model
    {
        public ModelData Data; public KfmFile Kfm; public string Folder; public NpcRow Row;
        public readonly Dictionary<int, Godot.Animation> Anims = new();
    }

    sealed class Inst
    {
        public Node3D Root, Visual; public AnimationPlayer Player; public Model Model;
        public int NpcId, Life, Moves;
        public Godot.Vector3 Target; public float TargetYaw; public int Sequence = -1; public string Playing = "";
        public bool Placeholder, Dead;
        public float Height = 150;
        public ShaderMaterial HpMat;
        public World.FieldNpcs.Placed Talk;   // the npc as the talk finds it (WorldService::FindNpcById by object id)
    }

    readonly PackSet packs; readonly ModelBuilder mb; readonly NpcTable table; readonly Node3D parent;
    readonly Dictionary<int, Model> models = new();
    readonly Dictionary<int, Inst> inst = new();
    readonly Dictionary<int, int> lives = new();
    public int Spawned, NoRow, NoModel, SequencesPlayed, SequencesMissing;
    public readonly HashSet<string> MissingModels = new();
    public int Count => inst.Count;
    public CombatLayer Combat;
    /// <summary>The npcs' name tags (the movie tag); none when null.</summary>
    public LiveNameTags Tags;
    public int Deaths, NoDeathSequence;

    // kfm name (lower case) -> its entry in the Npc pack; the folder layout differs by id range (NN/NN/NNNNNNNN or NN/NNNNNNNN)
    readonly Dictionary<string, string> kfmEntries = new();

    public NpcLayer(PackSet packs, ModelBuilder mb, NpcTable table, Node3D parent)
    {
        this.packs = packs; this.mb = mb; this.table = table; this.parent = parent;
        foreach (var e in packs.List(NpcArchive))
            if (e.EndsWith(".kfm", StringComparison.OrdinalIgnoreCase)) kfmEntries[Path.GetFileNameWithoutExtension(e).ToLowerInvariant()] = e;
        if (!mb.TextureArchives.Contains(NpcArchive)) mb.TextureArchives = mb.TextureArchives.Prepend(NpcArchive).ToArray();
    }

    static Godot.Vector3 G(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

    /// <summary>
    /// Where the client puts an npc from a packet position: CSceneStateGame::OnFieldAddNpc (@0x141718cf0) and
    /// CNpc::ApplySyncBlock (@0x1410918e0) both raise the packet z by <see cref="World.FieldNpcs.PacketLift"/> before placing it.
    /// </summary>
    static Godot.Vector3 Placed(System.Numerics.Vector3 p) => G(p) + new Godot.Vector3(0, World.FieldNpcs.PacketLift, 0);

    Model ModelFor(NpcRow row)
    {
        if (row == null) return null;
        if (models.TryGetValue(row.Id, out var m)) return m;
        m = null;
        try
        {
            string kfmEntry = row.Kfm != null && kfmEntries.TryGetValue(row.Kfm.ToLowerInvariant(), out var ke) ? ke : null;
            if (kfmEntry != null)
            {
                string folder = kfmEntry[..kfmEntry.LastIndexOf('/')];
                var kfm = KfmFile.Load(packs.Read(NpcArchive, kfmEntry));
                string nif = kfm.Nif.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant();
                if (packs.Exists(NpcArchive, $"{folder}/{nif}"))
                    m = new Model { Data = mb.LoadModel(NpcArchive, $"{folder}/{nif}"), Kfm = kfm, Folder = folder, Row = row };
            }
        }
        catch (Exception ex) { GD.PushWarning($"npc {row.Id} {row.Kfm}: {ex.Message}"); }
        if (m == null) MissingModels.Add(row?.Kfm ?? "?");
        return models[row.Id] = m;
    }

    Godot.Animation Anim(Model m, int seqId)
    {
        if (m.Anims.TryGetValue(seqId, out var a)) return a;
        a = null;
        var seq = m.Kfm.ById(seqId);
        if (seq != null)
        {
            string entry = $"{m.Folder}/{Path.GetFileName(seq.KfPath.Replace('\\', '/')).ToLowerInvariant()}";
            try
            {
                if (packs.Exists(NpcArchive, entry))
                {
                    var kf = KfAnimation.Load(NifFile.Load(mb.Spec, packs.Read(NpcArchive, entry)));
                    a = ModelBuilder.BuildAnimation(kf, m.Data);
                    string n = seq.Name;
                    bool loops = n.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Walk", StringComparison.OrdinalIgnoreCase)
                              || n.StartsWith("Run", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Bore", StringComparison.OrdinalIgnoreCase) == false && n.Contains("Idle", StringComparison.OrdinalIgnoreCase);
                    a.LoopMode = loops ? Godot.Animation.LoopModeEnum.Linear : Godot.Animation.LoopModeEnum.None;
                }
            }
            catch (Exception ex) { GD.PushWarning($"kf {entry}: {ex.Message}"); }
        }
        return m.Anims[seqId] = a;
    }

    public void Add(Ms2.Net.Replay.NpcAddedEvent e)
    {
        var n = e.Npc;
        if (inst.TryGetValue(n.ObjectId, out var old)) { Tags?.Remove(old.Root); old.Root.QueueFree(); inst.Remove(n.ObjectId); }
        var row = table.Find(n.NpcId);
        if (row == null) { NoRow++; return; }
        var model = ModelFor(row);
        var root = new Node3D { Name = $"Npc{n.ObjectId}" };
        var i = new Inst { Root = root, Model = model, NpcId = n.NpcId, Target = Placed(n.Position), TargetYaw = n.Rotation.Z };
        if (model != null)
        {
            i.Visual = mb.Build(model.Data, row.Kfm);
            i.Visual.Scale = new Godot.Vector3(row.Scale, row.Scale, row.Scale);
            i.Player = i.Visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            root.AddChild(i.Visual);
            FieldShadows.Character(root, i.Visual, FieldShadows.NpcKind(row), row.ShadowScale, 1f);
            var idle = model.Kfm.ByName("Idle_A");
            if (idle != null) Play(i, idle.Id);
        }
        else
        {
            // no model in the local data: a marker the size of the npc's capsule
            i.Placeholder = true; NoModel++;
            i.Visual = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 30, Height = 120 }, Position = new Godot.Vector3(0, 60, 0) };
            root.AddChild(i.Visual);
        }
        i.Height = row.CapsuleHeight * row.Scale;
        parent.AddChild(root);
        root.Position = i.Target;
        Tags?.AddNpc(root, i.Placeholder ? null : i.Visual, row);
        if (Combat != null && n.Stats[0].Health > 0 && n.Stats[2].Health < n.Stats[0].Health)
        {
            i.HpMat = Combat.HpBar(root, i.Height);
            i.HpMat.SetShaderParameter(ShaderParam.Fill, (float)n.Stats[2].Health / n.Stats[0].Health);
        }
        if (!i.Placeholder) i.Visual.Rotation = new Godot.Vector3(0, Mathf.DegToRad(i.TargetYaw), 0);
        i.Talk = new World.FieldNpcs.Placed { Row = row, Root = root, Body = i.Visual, Height = i.Height, ObjectId = n.ObjectId };
        i.Life = lives[n.ObjectId] = lives.GetValueOrDefault(n.ObjectId, -1) + 1;
        inst[n.ObjectId] = i;
        Spawned++;
    }

    public void Remove(int id, bool dead)
    {
        if (dead || !inst.Remove(id, out var i)) return;      // a dead npc stays until it is removed
        Tags?.Remove(i.Root);
        i.Root.QueueFree();
    }

    void Play(Inst i, int seqId)
    {
        var a = Anim(i.Model, seqId);
        if (a == null) { SequencesMissing++; return; }
        string name = $"s{seqId}";
        var lib = i.Player.GetAnimationLibrary("");
        if (!lib.HasAnimation(name)) lib.AddAnimation(name, a);
        i.Player.Play(name, 0.12);
        i.Playing = name; i.Sequence = seqId; SequencesPlayed++;
        if (a.LoopMode == Godot.Animation.LoopModeEnum.None)
        {
            var idle = i.Model.Kfm.ByName("Idle_A");
            if (idle != null && idle.Id != seqId)
            {
                var ia = Anim(i.Model, idle.Id);
                if (ia != null) { string iname = $"s{idle.Id}"; if (!lib.HasAnimation(iname)) lib.AddAnimation(iname, ia); i.Player.Queue(iname); }
            }
        }
    }

    /// <summary>The live npcs that are not dead, for the talk.</summary>
    public IEnumerable<World.FieldNpcs.Placed> Talkable => inst.Values.Where(x => !x.Dead && x.Talk != null).Select(x => x.Talk);

    public (Node3D root, float height, float yaw)? Info(int id) =>
        inst.TryGetValue(id, out var i) ? (i.Root, i.Height, i.Placeholder ? 0 : i.Visual.Rotation.Y) : null;

    /// <summary>Plays a named kfm sequence (a skill's motion) on an npc.</summary>
    public void PlaySequence(int id, string name)
    {
        if (!inst.TryGetValue(id, out var i) || i.Model == null || i.Dead) return;
        var seq = i.Model.Kfm.ByName(name);
        if (seq != null) Play(i, seq.Id);
    }

    /// <summary>An hp update: draws the bar once the npc is hurt; at 0 the death sequence plays and the body stays until it is removed.</summary>
    public void SetHp(int id, long cur, long max)
    {
        if (!inst.TryGetValue(id, out var i) || max <= 0) return;
        if (i.HpMat == null && Combat != null && cur < max && cur > 0) i.HpMat = Combat.HpBar(i.Root, i.Height);
        i.HpMat?.SetShaderParameter(ShaderParam.Fill, Mathf.Clamp((float)cur / max, 0f, 1f));
        if (cur <= 0 && !i.Dead)
        {
            i.Dead = true; Deaths++;
            Tags?.Died(i.Root);
            var dead = table.Find(i.NpcId)?.SoundDead;
            if (!string.IsNullOrEmpty(dead)) FieldAudio.Instance?.PlayEventAt(dead, i.Root.GlobalPosition);
            if (i.HpMat != null) { i.Root.GetNodeOrNull("HpBar")?.QueueFree(); i.HpMat = null; }
            if (i.Model == null) return;
            KfmFile.Sequence seq = null; Godot.Animation a = null;
            foreach (var cand in new[] { "Dead_A", "Dead_B" }.Select(n => i.Model.Kfm.ByName(n)).Concat(i.Model.Kfm.Sequences.Where(q => q.Name.StartsWith("Dead", StringComparison.OrdinalIgnoreCase))))
            {
                if (cand == null) continue;
                a = Anim(i.Model, cand.Id);
                if (a != null) { seq = cand; break; }
            }
            if (a == null) { NoDeathSequence++; return; }
            a.LoopMode = Godot.Animation.LoopModeEnum.None;
            string name = $"s{seq.Id}";
            var lib = i.Player.GetAnimationLibrary("");
            if (!lib.HasAnimation(name)) lib.AddAnimation(name, a);
            i.Player.Play(name, 0.1);
            i.Playing = name; i.Sequence = seq.Id;
        }
    }

    public void Move(Ms2.Net.Replay.NpcMovedEvent e)
    {
        var b = e.Sync;
        if (!inst.TryGetValue(b.ObjectId, out var i)) return;
        if (i.Dead) { i.Target = Placed(b.Position.ToVector3()); i.Moves++; return; }       // a dead body keeps its last frame
        i.Target = Placed(b.Position.ToVector3());
        if (b.Rotates) i.TargetYaw = b.RotationZ;
        i.Moves++;
        if (i.Model != null && b.Sequence >= 0 && b.Sequence != i.Sequence) Play(i, b.Sequence);
    }

    /// <summary>Where the busiest npc is (the one that has had the most sync blocks), for a camera that follows the action.</summary>
    public Godot.Vector3? ActivePosition() => inst.Count == 0 ? null : inst.Values.MaxBy(i => i.Moves).Root.Position;

    public void Process(float dt)
    {
        foreach (var i in inst.Values)
        {
            var d = i.Target - i.Root.Position;
            if (d.Length() > 600) i.Root.Position = i.Target;
            else i.Root.Position += d * Mathf.Min(1f, dt * 10f);
            if (!i.Placeholder)
                i.Visual.Rotation = new Godot.Vector3(0, Mathf.LerpAngle(i.Visual.Rotation.Y, Mathf.DegToRad(i.TargetYaw), Mathf.Min(1f, dt * 10f)), 0);
        }
    }

    /// <summary>Check: each npc's applied position is the recorded one after the same number of blocks.</summary>
    public (int n, int bad) Verify(Dictionary<int, List<List<System.Numerics.Vector3>>> truth, Action<string> log)
    {
        int n = 0, bad = 0;
        foreach (var (id, i) in inst)
        {
            if (!truth.TryGetValue(id, out var lifeList) || i.Life >= lifeList.Count) { bad++; continue; }
            var list = lifeList[i.Life]; n++;
            var expect = Placed(list[Math.Min(i.Moves, list.Count - 1)]);
            if ((expect - i.Target).Length() > 0.01f) { bad++; log($"MISMATCH npc {id}: expected {expect} got {i.Target} after {i.Moves} blocks"); }
        }
        return (n, bad);
    }
}

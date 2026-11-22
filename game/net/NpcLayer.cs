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
        public long Exp;                          // m_nE60: the exp the own PC got for this npc, shown when its death sequence ends
        public float Height = 150;
        public long HpCur, HpMax;                 // the npc's stat pair (FieldAddNpc, then the Stat packets)
        public ShaderMaterial HpMat;                 // a replay's stand-in quad
        public World.NpcHpBar Bar;                  // a live field's movie bar
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
    /// <summary>The live field's head bars (the uihpbar movie); null in a replay, whose bars stay the stand-in quads.</summary>
    public LiveHpBars Bars;
    public int Deaths, NoDeathSequence;
    /// <summary>
    /// StateDeadNPC::EndOfSequence (@0x1417c3140, statedead.cpp:291): the death sequence of an npc that holds exp
    /// (FUN_141094e20 > 0) ended; MyPC::FUN_1415294f0 (mypc.cpp:2858) sets the npc's exp and FlushDamageText (@0x141093910)
    /// shows it on the npc (FUN_141912a80 type 0) and zeroes it. The npc and its exp.
    /// STILL-OPEN: FUN_1415294f0's CGetExp::AddEffect and CDPSMeterSystem::AddExp are not ported.
    /// </summary>
    public Action<World.FieldNpcs.Placed, long> ExpShown;

    /// <summary>OnFieldAddNpc's CQuestNavigator::NotifyAddNpc (the npc made) and OnFieldRemoveNpc's NotifyRemoveNpc (its
    /// object id, before the npc is freed).</summary>
    public Action<World.FieldNpcs.Placed> Added;
    public Action<int> Removed;

    /// <summary>The npcs of the field as the talk finds them, dead or not.</summary>
    public IEnumerable<World.FieldNpcs.Placed> All => inst.Values.Where(x => x.Talk != null).Select(x => x.Talk);

    /// <summary>OnExpUp with this npc's object id: CNpc::FUN_141094df0 (@0x141094df0) adds the gain to the npc, dead or
    /// not; false when there is no npc of the id (WorldService::FindNpcById null), and the own PC shows it.</summary>
    public bool AddExp(int id, long exp)
    {
        if (!inst.TryGetValue(id, out var i)) return false;
        i.Exp += exp;
        return true;
    }

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
        // OnFieldAddNpc for a present object id: WorldService::OnObjectCreated only SetAt's the id maps over the old
        // object (@0x141718cf0); here the old npc is freed, so its id is dropped first like OnFieldRemoveNpc's
        // NotifyRemoveNpc, or a marker keyed by it outlives the npc
        if (inst.TryGetValue(n.ObjectId, out var old)) { inst.Remove(n.ObjectId); Removed?.Invoke(n.ObjectId); Tags?.Remove(old.Root); Bars?.Remove(old.Root); old.Root.QueueFree(); }
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
        if (!i.Placeholder) i.Visual.Rotation = new Godot.Vector3(0, Mathf.DegToRad(i.TargetYaw), 0);
        i.Talk = new World.FieldNpcs.Placed { Row = row, Root = root, Body = i.Visual, Height = i.Height, ObjectId = n.ObjectId };
        if (Bars != null)
        {
            // the point is memoized per model while the npc stands still (DummyMemo, as the name tag's anchor)
            var modelNode = i.Placeholder ? null : i.Visual?.GetNodeOrNull<Node3D>(ModelPath);
            Func<Godot.Vector3> anchor = modelNode != null
                ? () => World.DummyAnchor.Point(row.EffectDummies, "Eff_UI", modelNode, "Scene Root") ?? root.GlobalPosition
                : () => World.NpcPresence.Dummy(i.Talk, "Eff_UI") ?? root.GlobalPosition;
            // CNpc::vfunc_80: the row's hpBar flag makes the entity (hidden until active and shown); CNpc::vfunc_12's
            // property tag 2 row is shown and filled from the spawn stat
            i.Bar = Bars.AddNpc(i.Talk, anchor, n.Stats[2].Health, n.Stats[0].Health);
        }
        else if (Combat != null && n.Stats[0].Health > 0 && n.Stats[2].Health < n.Stats[0].Health)
        {
            i.HpMat = Combat.HpBar(root, i.Height);
            i.HpMat.SetShaderParameter(ShaderParam.Fill, (float)n.Stats[2].Health / n.Stats[0].Health);
        }
        i.HpCur = n.Stats[2].Health; i.HpMax = n.Stats[0].Health;
        i.Life = lives[n.ObjectId] = lives.GetValueOrDefault(n.ObjectId, -1) + 1;
        inst[n.ObjectId] = i;
        Spawned++;
        Added?.Invoke(i.Talk);
        if (n.Hidden) Die(n.ObjectId, false);      // OnFieldAddNpc (scenestategame.cpp:3556-3558): vfunc_125(0)
    }

    static readonly NodePath ModelPath = "Model";

    /// <summary>OnFieldRemoveNpc drops the npc; OnFieldDeadNpc (<paramref name="dead"/>, @0x1417197c0) is
    /// <see cref="Die"/>(1), the body stays until it is removed.</summary>
    public void Remove(int id, bool dead)
    {
        if (dead) { Die(id, true); return; }
        if (!inst.Remove(id, out var i)) return;
        Removed?.Invoke(id);
        Tags?.Remove(i.Root);
        Bars?.Remove(i.Root);
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

    /// <summary>A live npc by object id (WorldService::FindObjectById), dead or not; null when there is none.</summary>
    public World.FieldNpcs.Placed Placed(int id) => inst.TryGetValue(id, out var i) ? i.Talk : null;

    /// <summary>
    /// The live npcs' body boxes for an own attack's area pick, in client space (z up): CNpc::BuildCollision
    /// (unit_14108e1e0_npcactorandvibrate.cpp:1648) gives a box only to an npc whose collision shape is "box".
    /// Dead npcs stay in (hp 0, the corpse's hitAble) for <see cref="Ms2.Ui.Skills.SkillHit.FilterTarget"/>: team is the row's
    /// friendly (CNpc::vfunc_116), the class its npc class.
    /// STILL-OPEN: the node collision boxes (m_arrE68 / m_arrE88) and the corpse's dead state 0xc (Dead stands for it).
    /// </summary>
    public void HitCandidates(List<Ms2.Ui.Skills.HitCandidate> into)
    {
        foreach (var (id, i) in inst)
        {
            var row = i.Talk?.Row ?? i.Model?.Row;
            if (row == null || row.CollisionShape != "box" || i.Root == null) continue;
            var g = i.Root.GlobalPosition;
            var p = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
            into.Add(new Ms2.Ui.Skills.HitCandidate
            {
                Id = id, Position = p,
                Body = Ms2.Ui.Skills.SkillHit.NpcBody(row.CollisionWidth, row.CollisionDepth, row.CollisionHeight, p, i.TargetYaw),
                Kind = Ms2.Ui.Skills.HitKind.Npc, Team = row.Friendly, Hp = i.Dead ? 0 : i.HpCur, NpcClass = row.Class,
                CorpseHit = i.Dead && row.CorpseHitAble, DefenceMaterial = row.DefenceMaterial,
            });
        }
    }

    /// <summary>The live npcs that are not dead, for the talk.</summary>
    public IEnumerable<World.FieldNpcs.Placed> Talkable => inst.Values.Where(x => !x.Dead && x.Talk != null).Select(x => x.Talk);

    public (Node3D root, float height, float yaw)? Info(int id) =>
        inst.TryGetValue(id, out var i) ? (i.Root, i.Height, i.Placeholder ? 0 : i.Visual.Rotation.Y) : null;

    /// <summary>The npc of the object id as StateHold reads it: its npc id, whether it is dead and its visual.</summary>
    public bool HoldInfo(int id, out int npcId, out bool dead, out Node3D visual)
    {
        if (!inst.TryGetValue(id, out var i)) { npcId = 0; dead = false; visual = null; return false; }
        npcId = i.NpcId; dead = i.Dead; visual = i.Placeholder ? null : i.Visual;
        return true;
    }

    static readonly NodePath HoldSkeletonPath = "Model/" + ModelBuilder.SkeletonName;

    /// <summary>
    /// WorldService::FindNearestActor (@0x141bc9830, worldservice_1.cpp:5143) over the field's npcs: m_0e8 holds every
    /// object OnObjectCreated registers (worldservice_1.cpp:3149), and CNpc keeps PCBase::vfunc_119, so its body's
    /// Hold_PointNN nodes count as a PC's do. Each npc not dead whose nearest node lies within <paramref name="maxDist"/>
    /// and is <see cref="RemoteLayer.Nearer"/> than the pick (<paramref name="best"/>, <paramref name="id"/>,
    /// <paramref name="node"/>) replaces it; true when the pick changed. A placeholder has no skeleton.
    /// </summary>
    public bool NearestHold(Godot.Vector3 from, float maxDist, ref float best, ref int id, ref string node)
    {
        bool changed = false;
        foreach (var (objectId, i) in inst)
        {
            if (i.Dead || i.Placeholder || i.Visual == null || !GodotObject.IsInstanceValid(i.Visual)) continue;
            float d = RemoteLayer.NearestHoldNode(i.Visual.GetNodeOrNull<Skeleton3D>(HoldSkeletonPath), null, from, out string n);
            if (d > maxDist || !RemoteLayer.Nearer(d, objectId, best, id)) continue;
            best = d;
            id = objectId;
            node = n;
            changed = true;
        }
        return changed;
    }

    /// <summary>Plays a named kfm sequence (a skill's motion) on an npc.</summary>
    public void PlaySequence(int id, string name)
    {
        if (!inst.TryGetValue(id, out var i) || i.Model == null || i.Dead) return;
        var seq = i.Model.Kfm.ByName(name);
        if (seq != null) Play(i, seq.Id);
    }

    /// <summary>PCBase::vfunc_112 on an npc (CNpc::vfunc_112 @0x141090740 returns early for a dead npc whose corpse is
    /// hittable): a hit sets the shown flag only while it is off, and only for the local player's. A dead npc's bar never
    /// draws either way.</summary>
    public void Damaged(int id, bool byLocalPlayer)
    {
        if (inst.TryGetValue(id, out var i) && !i.Dead && i.Bar is { Shown: false }) i.Bar.Shown = byLocalPlayer;
    }

    /// <summary>CNpc::vfunc_66 (@0x1410935b0): the emotion changed to battle, so a living npc's bar is made active with
    /// its current stat pair (UpdateHpBar = FUN_141992180); a living-out change does nothing to it.
    /// STILL-OPEN: the client also sets the npc's emotion from its states (StateSkill SetEmotion(1) for a battle skill,
    /// stateskill.cpp:432; the idle states 0, stateidle.cpp:388), which this follows only through the sync bit.</summary>
    public void Stance(int id, bool battle)
    {
        if (!battle || !inst.TryGetValue(id, out var i) || i.Dead || i.Bar == null) return;
        i.Bar.Activate(i.HpCur, i.HpMax);
    }

    /// <summary>An hp update (OnStat, FUN_141992230): the bar is filled, never activated; at 0 the death sequence plays
    /// and the body stays until it is removed.</summary>
    public void SetHp(int id, long cur, long max)
    {
        if (!inst.TryGetValue(id, out var i) || max <= 0) return;
        i.HpCur = cur; i.HpMax = max;
        if (i.Bar != null) i.Bar.Stat(cur, max);
        else if (i.HpMat == null && Combat != null && cur < max && cur > 0) i.HpMat = Combat.HpBar(i.Root, i.Height);
        i.HpMat?.SetShaderParameter(ShaderParam.Fill, Mathf.Clamp((float)cur / max, 0f, 1f));
        // STILL-OPEN: the client's OnStat only fills the bar; its npc dies on FieldDeadNpc or the add packet's flag
        // (Die). The hp 0 death stays for the captures without the dead packet.
        if (cur <= 0) Die(id, true);
    }

    /// <summary>Whether the npc of the object id is dead (PCBase::IsDead, m_unk_15c); false when there is none.</summary>
    public bool IsDead(int id) => inst.TryGetValue(id, out var i) && i.Dead;

    /// <summary>The npc's position in the client's z-up coordinates (vfunc_18) and its rotation z in degrees (vfunc_20, the
    /// one the moves set, not the eased visual); null when there is no npc of the id.</summary>
    public (System.Numerics.Vector3 Pos, float RotZ)? PoseOf(int id)
    {
        if (!inst.TryGetValue(id, out var i) || i.Root == null || !GodotObject.IsInstanceValid(i.Root)) return null;
        var g = i.Root.GlobalPosition;
        return (new System.Numerics.Vector3(g.X, -g.Z, g.Y), i.TargetYaw);
    }

    /// <summary>
    /// CNpc::vfunc_125 (@0x141091210): the npc dies (m_unk_15c = 1) and enters StateDeadNPC (0xc), which plays its
    /// death sequence; <paramref name="show"/> (OnFieldDeadNpc passes 1, OnFieldAddNpc's dead flag 0,
    /// scenestategame.cpp:3556-3558/3610) plays its dead sound (m_strSoundDead) too.
    /// STILL-OPEN: FUN_141099ab0, FUN_141098fb0, the corpse box (m_corpse) and the boss bgm tracker are not ported.
    /// </summary>
    public void Die(int id, bool show)
    {
        if (!inst.TryGetValue(id, out var i) || i.Dead) return;
        i.Dead = true; Deaths++;
        Tags?.Died(i.Root);
        if (i.Bar != null) { i.Bar.Dead = true; Bars?.Remove(i.Root); }      // vfunc_8: a dead npc's bar drops for good; its entry and clip go back to the pool
        var dead = show ? table.Find(i.NpcId)?.SoundDead : null;
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
        // EndOfSequence of the death sequence, once (m_bFinished); a body without a death sequence never gets there
        var player = i.Player;
        void Ended(StringName ended)
        {
            if (ended != name) return;
            player.AnimationFinished -= Ended;
            if (i.Exp <= 0) return;
            long exp = i.Exp;
            i.Exp = 0;
            ExpShown?.Invoke(i.Talk, exp);
        }
        player.AnimationFinished += Ended;
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

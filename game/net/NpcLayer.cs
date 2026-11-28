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
        /// <summary>GameObject m_fScaleAC, the scale GameObject::vfunc_9 sets the made actor to (gameobject.cpp:240): the row's
        /// m_model.m_fScale, or a FieldAddPet's scale when it is not 1 (SetScaleAC, scenestategame.cpp:9701-9702).</summary>
        public float ScaleAc = 1f;
        public Godot.Vector3 Target; public float TargetYaw; public int Sequence = -1; public string Playing = "";
        public short AnimKey;                     // m_nDE8: the last sync block's sequence key, 0 until one came (CNpc ctor, :509)
        public string OwnerEffect = "";           // m_strEC8: the owner effect (NpcNotice mode 1)
        public int OwnerActor, OwnerEffectId;     // m_nEC0: the pc the effect sits on (a boss block's owner actor); m_nEC4: the kept effect
        public Node3D OwnerNode;                  // the pc node the kept effect hangs on
        public Node3D ModelNode;                  // the body's Model node (the hp bar's Eff_UI anchor), null on a placeholder
        public string Kfm;                        // m_strED8: the kfm the actor loads (the row's, or NpcNotice mode 2's)
        public int RideOwner;                     // m_nF20: the actor the npc rides on (NpcRide mode 1, a boss's FieldAddNpc), 0 for none
        public long AccumDamage;                  // m_nEE0: the accumulated damage (NpcRide mode 0, FieldAddNpc's damaged hp bar tag)
        public Godot.Vector3 Velocity;            // m_posD40: the last block's velocity, added to the position every frame (CNpc::vfunc_53)
        public bool Eases;                        // a dead body: eased toward the block position, no velocity
        public readonly Ms2.Net.Replay.NpcJumpArc Arc = new();   // m_unkDB0: the jump arc a -2/-3 block starts
        public readonly Ms2.Ui.Skills.NpcSpawnState Spawn = new();   // StateSpawn (0x11): the hide, the spawn point's sequence, then idle
        public Func<string, float> SequenceLength;                   // GetAnimationDuration on this body, made on the first spawn
        public float AniSpeed = 1f, TurnCap;      // the Run_ speed (the row's anispeed times ScaleRate) and rotationSpeed (the turn cap, degrees per second)
        public bool Placeholder, Dead;
        public NpcRow BossRow;                    // a boss's row (CNpcData::IsBoss), whose distances drive the field's boss music; null otherwise
        public string DeadAction;                 // m_strED0: the death sequence picked at spawn (CNpc init, :539)
        public long Exp;                          // m_nE60: the exp the own PC got for this npc, shown when its death sequence ends
        public float Height = 150;
        public long HpCur, HpMax;                 // the npc's stat pair (FieldAddNpc, then the Stat packets)
        public ShaderMaterial HpMat;                 // a replay's stand-in quad
        public World.NpcHpBar Bar;                  // a live field's movie bar
        public World.FieldNpcs.Placed Talk;   // the npc as the talk finds it (WorldService::FindNpcById by object id)
        public Func<Godot.Vector3> Anchor;        // the Eff_UI point (vfunc_10), in world space
        public readonly Ms2.Ui.Skills.NpcVibrate Vibrate = new();   // m_vibrate (+0xe18)
        public Godot.Vector3 VisualBase;          // the body's place under the root, which the shake moves off and back to
        public readonly Ms2.Ui.Skills.ColorBoost Boost = new();   // PCBase m_colorBoost
        public Ms2.Ui.FaceEmotion Face;           // PCBase's face emotion (m_spD10, m_sp638) on the body, null on a placeholder
        public List<ShaderMaterial> BoostMaterials;                // the body's character-shader materials, gathered on the first boost
        public int[] AnchorBones;                 // the skeleton's bone per <nodeCollisions> row (-1: none), found on the first hit
        public byte State;                        // GetCurStateId(): the sync blocks' state (ApplySyncBlock), 0x10 on a cast, 0xc dead
        public ushort Serial;                     // m_nDAC: the last block's state serial (0 at spawn, CNpc init :473)
        public byte TalkPhase;                    // StateTalkNpc's phase (m_n10) while State is the talk state: 0x48 talking, 0x49 not
        public Ms2.Formats.World.SkillMotion Cast; // the head of PCBase m_unk_8e8: the cast record's motion (GetMotionData)
        public long CastUid; public int CastMotion = -1;   // the record's cast uid and motion index, as FUN_141673bf0 finds it
        public int CastSeq = -1;                  // the cast's sequence id as played here; -1 when the body has none
        public bool CastLast;                     // CSkillRecord::FUN_141759e40: the record's motion is the level's last
        public double CastEnded = -1;             // StateSkill m_fStartTime once the sequence ended (EndOfSequence); -1 while it plays
        public double PlayEnds;                   // the layer time the sequence Play last started is over; +inf for a looping one
        public Node3D EffectUi;                   // PCBase m_spC40: the trigger's effect nif on the Eff_UI node (FUN_141678ad0)
        public TriggerEffectNif EffectUiOf;       // its packet entry (scale, z rotation)
        public Transform3D EffectUiAt;            // the model's global transform it was placed for
        public Xform EffectUiRoot;                // the nif root's own transform, whose rotation and scale the attach replaces
    }

    /// <summary>
    /// WorldService::FUN_141bcfff0 (worldservice_1.cpp:5872): the spawn state's sequence for a spawn id, from the field's
    /// spawn points (<see cref="Ms2.Formats.World.FieldSpawnPoints.Find"/>); null when the field has no such point.
    /// </summary>
    public Func<int, string> SpawnAnimation = _ => null;

    /// <summary>The CRT rand() the death pick draws from (0..0x7fff).</summary>
    public Random Rand = new();
    readonly Func<int> crtRand;
    /// <summary>s_random's Tausworthe stream for the hit sound table's pick (HitSoundTable.Get); the field's tombs draw from
    /// it too (<see cref="RemoteLayer.HitSoundRand"/>), as the client has one s_random.</summary>
    readonly Func<uint> hitSoundRand;
    internal Func<uint> HitSoundRand => hitSoundRand;
    /// <summary>The game time (CMainSystem::GetGameTime) the npcs' shakes run on, in seconds: <see cref="Process"/>'s sum.</summary>
    public float Now;

    readonly PackSet packs; readonly ModelBuilder mb; readonly NpcTable table; readonly Node3D parent;
    EmotionTable emotions;                  // CEmotionManager::GetNpcTable (CNpc::vfunc_70), read on the first body
    readonly Dictionary<int, Model> models = new();
    readonly Dictionary<(int, string), Model> changedModels = new();   // NpcNotice mode 2's kfm models, by row id and kfm
    readonly Dictionary<int, Inst> inst = new();
    readonly Dictionary<int, int> lives = new();
    public int Spawned, NoRow, NoModel, SequencesPlayed, SequencesMissing;
    public readonly HashSet<string> MissingModels = new();
    public int Count => inst.Count;
    /// <summary>Whether an npc of the object id is in the field.</summary>
    public bool Contains(int id) => inst.ContainsKey(id);
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
        crtRand = () => Rand.Next(0x8000);
        hitSoundRand = new Ms2.Quest.QuestRandom(unchecked((int)System.Environment.TickCount64 + Random.Shared.Next(0x8000))).Next;
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
        return models[row.Id] = LoadModel(row, row.Kfm);
    }

    /// <summary>The model of a kfm other than the row's (NpcNotice mode 2), with the row's dummies; null when the kfm is not in
    /// the npc models.</summary>
    Model ModelFor(NpcRow row, string kfm)
    {
        if (row == null) return null;
        if (string.Equals(kfm, row.Kfm, StringComparison.OrdinalIgnoreCase)) return ModelFor(row);
        var key = (row.Id, kfm.ToLowerInvariant());
        if (changedModels.TryGetValue(key, out var m)) return m;
        return changedModels[key] = LoadModel(row, kfm);
    }

    Model LoadModel(NpcRow row, string kfmName)
    {
        Model m = null;
        try
        {
            string kfmEntry = kfmName != null && kfmEntries.TryGetValue(kfmName.ToLowerInvariant(), out var ke) ? ke : null;
            if (kfmEntry != null)
            {
                string folder = kfmEntry[..kfmEntry.LastIndexOf('/')];
                var kfm = KfmFile.Load(packs.Read(NpcArchive, kfmEntry));
                string nif = kfm.Nif.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant();
                if (packs.Exists(NpcArchive, $"{folder}/{nif}"))
                    m = new Model { Data = mb.LoadModel(NpcArchive, $"{folder}/{nif}"), Kfm = kfm, Folder = folder, Row = row };
            }
        }
        catch (Exception ex) { GD.PushWarning($"npc {row.Id} {kfmName}: {ex.Message}"); }
        if (m == null) MissingModels.Add(kfmName ?? "?");
        return m;
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
        if (inst.TryGetValue(n.ObjectId, out var old)) { inst.Remove(n.ObjectId); ReleaseOwnerEffect(old); BossGone(n.ObjectId, old); Removed?.Invoke(n.ObjectId); Tags?.Remove(old.Root); Bars?.Remove(old.Root); old.Root.QueueFree(); }
        var row = table.Find(n.NpcId);
        if (LiveSession.LiveCheck) Godot.GD.Print($"live: npc add object {n.ObjectId} npc {n.NpcId} row {(row == null ? "no" : "yes")}");
        if (row == null) { NoRow++; return; }
        var model = ModelFor(row);
        var root = new Node3D { Name = $"Npc{n.ObjectId}" };
        float scaleAc = e.Pet?.ScaleAc(row.Scale) ?? row.Scale;
        var i = new Inst { Root = root, Model = model, Kfm = row.Kfm, NpcId = n.NpcId, Target = Placed(n.Position), TargetYaw = n.Rotation.Z, AniSpeed = row.AniSpeed * ScaleRate(1f, scaleAc), TurnCap = row.RotationSpeed, ScaleAc = scaleAc };
        i.DeadAction = row.RandomDeadDefaultAction(crtRand);
        if (model != null)
        {
            BuildBody(i, model, row);
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
        i.VisualBase = i.Visual.Position;
        i.Height = row.CapsuleHeight * i.ScaleAc;
        parent.AddChild(root);
        root.Position = i.Target;
        Tags?.AddNpc(root, i.Placeholder ? null : i.Visual, row);
        if (!i.Placeholder) i.Visual.Rotation = new Godot.Vector3(0, Mathf.DegToRad(i.TargetYaw), 0);
        i.Talk = new World.FieldNpcs.Placed { Row = row, Root = root, Body = i.Visual, Height = i.Height, ObjectId = n.ObjectId };
        // the actor's vfunc_10 anchor, its Eff_UI dummy (the hp bar's and the buff balloon's): the point is memoized per model
        // while the npc stands still (DummyMemo, as the name tag's anchor); the body's Model node is read at each call, so a
        // body NpcNotice mode 2 changed anchors it too
        Func<Godot.Vector3> anchor = i.Anchor = () => i.ModelNode != null
            ? World.DummyAnchor.Point(row.EffectDummies, "Eff_UI", i.ModelNode, "Scene Root") ?? root.GlobalPosition
            : World.NpcPresence.Dummy(i.Talk, "Eff_UI") ?? root.GlobalPosition;
        if (Bars != null)
        {
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
        i.RideOwner = n.BossValue;                 // OnFieldAddNpc (scenestategame.cpp:3574): FUN_141095290, 0 for a non-boss npc
        i.AccumDamage = n.AccumulatedDamage;       // :3577, FUN_141095240, 0 without the tag
        i.Life = lives[n.ObjectId] = lives.GetValueOrDefault(n.ObjectId, -1) + 1;
        inst[n.ObjectId] = i;
        if (row.IsBoss) { i.BossRow = row; FieldAudio.Instance?.BossAdded((ulong)n.ObjectId); }   // CNpc::vfunc_10 (:534): AddCombatBgmTracker
        ActorAdded?.Invoke(n.ObjectId);            // WorldService::OnActorAdded: the field's effect nif of this id (m_848)
        Spawned++;
        Added?.Invoke(i.Talk);
        if (n.Hidden) Die(n.ObjectId, false);      // OnFieldAddNpc (scenestategame.cpp:3556-3558): vfunc_125(0)
    }

    static readonly NodePath ModelPath = "Model";

    /// <summary>The npc's body of a model under its root: the row's dummies and scale, its animation player.</summary>
    void BuildBody(Inst i, Model model, NpcRow row)
    {
        i.Visual = mb.Build(model.Data, i.Kfm);
        EffectAttach.SetDummies(i.Visual, row.EffectDummies);
        i.Visual.Scale = new Godot.Vector3(i.ScaleAc, i.ScaleAc, i.ScaleAc);
        i.Player = i.Visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        // the idle queued after a one-shot (Play) starts at speed 1, not the block's: the client's speed is per sequence, and
        // PlayAnimationById passes m_f938 (pcbase.cpp:1533), 1.0 from the PCBase ctor (:787) and never set for a CNpc
        var player = i.Player;
        player.AnimationChanged += (_, _) => player.SpeedScale = 1f;
        i.Root.AddChild(i.Visual);
        i.ModelNode = i.Visual.GetNodeOrNull<Node3D>(ModelPath);
        // PCBase::vfunc_10 (pcbase.cpp:919) from CNpc::vfunc_10 (unit_14108e1e0_npcactorandvibrate.cpp:526): the face's group is
        // the Npc library's group of the row's emotion id. An npc is no MyPC, so its frames wait for their textures
        // (CTextureAnimation state 1). A rebuilt body (NpcNotice mode 2) gets a new face on the new body.
        emotions ??= new EmotionTable(packs, EmotionTable.Npc);
        i.Face = new Ms2.Ui.FaceEmotion(new Ms2Godot.Look.FaceTexture(i.Visual, mb), own: false);
        i.Face.UseGroup(emotions.Group((uint)row.EmotionGroupId));
    }

    /// <summary>
    /// The trigger's Ui sub 9 on an npc (CTriggerService::FUN_1418641e0 @0x1418641e0, triggerservice.cpp:1052-1068): the object
    /// of the id (WorldService::FUN_141bd1200) as a PCBase; a name turns its text keys off and plays it, an empty one rests the
    /// face (<see cref="Ms2.Ui.FaceEmotion.Trigger"/>). False when there is no npc of the id; a placeholder body (no face)
    /// takes it and shows nothing.
    /// </summary>
    public bool TriggerFace(int objectId, string name)
    {
        if (!inst.TryGetValue(objectId, out var i)) return false;
        i.Face?.Trigger(name, NowMs);
        return true;
    }

    /// <summary>The npc's face emotion, null when there is no npc of the id or it has no body (a check's view).</summary>
    public Ms2.Ui.FaceEmotion Face(int objectId) => inst.TryGetValue(objectId, out var i) ? i.Face : null;

    long NowMs => (long)(Now * 1000.0);

    /// <summary>
    /// NpcNotice mode 2 (CSceneStateGame::OnNpcNotice, scenestategame.cpp:9167-9172): FindNpcById, then CNpc::FUN_1410951d0
    /// (@0x1410951d0, unit_14108e1e0_npcactorandvibrate.cpp:1608-1613): m_strED8 set to the kfm, then GameObject::ChangeModel
    /// ("urn:gamebryo-animation:" + kfm, gameobject.cpp:331-345), which swaps the actor's asset; an empty name changes nothing
    /// there (m_strED8 is still set). The body is built again from the kfm's model with the row's dummies and scale, in place of
    /// the old one (a placeholder too), turned as it was; the name tag, the hp bar's anchor, the talk body, the Eff_UI effect
    /// nif and the shadow casters follow it. False when there is no npc of the id.
    /// STILL-OPEN: which sequence the new actor starts with (here the old one's name when the kfm has it, else Idle_A), and
    /// what a kfm the packs lack shows (here the old body stays, the name counted in MissingModels).
    /// </summary>
    public bool ChangeModel(int id, string kfm)
    {
        if (!inst.TryGetValue(id, out var i)) return false;
        i.Kfm = kfm ?? "";
        if (i.Kfm.Length == 0) return true;
        var row = i.Talk?.Row ?? i.Model?.Row;
        var model = ModelFor(row, i.Kfm);
        if (model == null) return true;
        var old = i.Visual;
        string playing = i.Model?.Kfm.ById(i.Sequence)?.Name;
        var turn = i.Placeholder || old == null ? new Godot.Vector3(0, Mathf.DegToRad(i.TargetYaw), 0) : old.Rotation;
        bool shadow = i.Root.GetNodeOrNull(FieldShadows.ShadowNode) != null;
        if (old != null) { i.Root.RemoveChild(old); old.QueueFree(); }
        i.Model = model;
        i.Placeholder = false;
        i.AnchorBones = null; i.BoostMaterials = null;
        i.Sequence = -1; i.Playing = "";
        BuildBody(i, model, row);
        i.Visual.Rotation = turn;
        i.VisualBase = i.Visual.Position;
        if (shadow) FieldShadows.ModelCasters(i.Visual, FieldShadows.NpcKind(row));
        else FieldShadows.Character(i.Root, i.Visual, FieldShadows.NpcKind(row), row.ShadowScale, 1f);
        if (i.Talk != null) i.Talk.Body = i.Visual;
        if (i.EffectUi != null) { i.EffectUi.GetParent()?.RemoveChild(i.EffectUi); i.Visual.AddChild(i.EffectUi); i.EffectUiAt = default; }
        if (Tags != null) { Tags.AddNpc(i.Root, i.Visual, row); if (i.Dead) Tags.Died(i.Root); }
        var seq = (playing != null ? model.Kfm.ByName(playing) : null) ?? model.Kfm.ByName("Idle_A");
        if (seq != null) Play(i, seq.Id);
        return true;
    }

    /// <summary>The kfm an npc's actor loads (m_strED8), its body, and the folder of the model its sequences come from; null
    /// when there is no npc of the id.</summary>
    public (string Kfm, Node3D Body, bool Placeholder, string Folder)? ModelOf(int id) =>
        inst.TryGetValue(id, out var i) ? (i.Kfm, i.Visual, i.Placeholder, i.Model?.Folder) : null;

    /// <summary>OnFieldRemoveNpc drops the npc; OnFieldDeadNpc (<paramref name="dead"/>, @0x1417197c0) is
    /// <see cref="Die"/>(1), the body stays until it is removed.</summary>
    public void Remove(int id, bool dead)
    {
        if (dead) { Die(id, true); return; }
        if (!inst.Remove(id, out var i)) return;
        ReleaseOwnerEffect(i);                      // CNpc::~CNpc (:519-521)
        BossGone(id, i);                            // CNpc::vfunc_14 (:613): RemoveCombatBgmTracker
        Removed?.Invoke(id);
        Tags?.Remove(i.Root);
        Bars?.Remove(i.Root);
        i.Root.QueueFree();
    }

    /// <summary>WorldService::FUN_141bd1200 for an npc: the npc of that id is in the field (an npc here is loaded once added).</summary>
    public bool Has(int objectId) => inst.ContainsKey(objectId);

    /// <summary>The npc duel bar's view of an npc (WorldService::FindNpcById by object id): its npc id and the stat pair
    /// (FieldAddNpc, then the Stat packets); null when the field has no npc of that id.</summary>
    public (int NpcId, long Hp, long MaxHp)? HpOf(int objectId) => inst.TryGetValue(objectId, out var i) ? (i.NpcId, i.HpCur, i.HpMax) : null;

    /// <summary>WorldService::OnActorAdded (@0x141bbfb50): an npc was added, with its object id (FieldEffectNifs.ActorAdded).</summary>
    public Action<int> ActorAdded;

    /// <summary>The Model pack an effect nif's path names: "Model/" + the path (JoinPath("Model", name)), its first folder the pack.</summary>
    const string ModelRoot = "Resource/Model/";
    readonly Dictionary<string, ModelData> effectNifs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CNifLoadingService::Load(JoinPath("Model", name)) of PCBase::FUN_141678ad0: the nif in the Model pack of the path's
    /// first folder ("Map/Royalcity/Indoor/x.nif" is Resource/Model/Map's royalcity/indoor/x.nif). Null when it is not there.
    /// </summary>
    ModelData EffectNif(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (effectNifs.TryGetValue(path, out var md)) return md;
        md = null;
        string p = path.Replace('\\', '/').TrimStart('/');
        int slash = p.IndexOf('/');
        if (slash > 0)
        {
            string archive = ModelRoot + p[..slash], entry = p[(slash + 1)..].ToLowerInvariant();
            try
            {
                if (packs.Exists(archive, entry))
                {
                    if (!mb.TextureArchives.Contains(archive)) mb.TextureArchives = mb.TextureArchives.Prepend(archive).ToArray();
                    md = mb.LoadModel(archive, entry);
                }
            }
            catch (Exception ex) { GD.PushWarning($"effect nif {path}: {ex.Message}"); }
        }
        if (md == null) NotApplied.Print($"live: effect nif {path} is not in the data");
        return effectNifs[path] = md;
    }

    static readonly NodePath EffUiName = "Eff_UI";
    const string SceneRoot = "Scene Root";

    /// <summary>
    /// PCBase::FUN_141678ad0 (@0x141678ad0, pcbase.cpp:4278): with no effect on the npc yet (m_spC40) and an Eff_UI node, the
    /// nif under Model/ loaded, scaled when the scale is not 1, turned about z by the degrees when they are not 0, and attached
    /// to the Eff_UI node. The npc's Eff_UI node is its row's dummy (DummyWorld), so the nif is placed at the dummy each time the
    /// model moves (<see cref="PlaceEffectUi"/>), under the body, so it hides with it.
    /// STILL-OPEN: the outline (bOutline: FUN_141b70cb0 on every render object, width 3, colour (1, 0.95, 0.5058)) is not drawn;
    /// a pc's Eff_UI (the server names npcs only) is not looked up.
    /// </summary>
    public void AttachEffectNif(int objectId, TriggerEffectNif e)
    {
        if (!inst.TryGetValue(objectId, out var i) || i.EffectUi != null || i.Placeholder) return;
        var modelNode = i.Visual?.GetNodeOrNull<Node3D>(ModelPath);
        if (modelNode == null || EffectDummyTable.Find(i.Model?.Row?.EffectDummies, EffUiName.ToString()) == null) return;   // FUN_141675150("Eff_UI") finds none
        var md = EffectNif(e.NifPath);
        if (md == null) return;
        var node = mb.Build(md, "EffectUi");
        // the nif root's own transform goes into the place (SetScale / SetRotate replace its scale and rotation), so the Model
        // node keeps only the turn to y-up
        node.GetNode<Node3D>(ModelPath).Transform = ClientToGodot;
        i.Visual.AddChild(node);
        i.EffectUi = node; i.EffectUiOf = e; i.EffectUiAt = default; i.EffectUiRoot = md.RootLocal;
        PlaceEffectUi(i);
    }

    /// <summary>PCBase::DetachEffectUiNode (@0x141678f50, pcbase.cpp:4317): the npc's effect nif taken off, m_spC40 cleared.</summary>
    public void DetachEffectNif(int objectId)
    {
        if (!inst.TryGetValue(objectId, out var i) || i.EffectUi == null) return;
        i.EffectUi.QueueFree();
        i.EffectUi = null; i.EffectUiOf = null;
    }

    /// <summary>Client z-up to Godot y-up, (x, y, z) to (x, z, -y): the Model node's turn (ModelBuilder.Build), written out.</summary>
    static readonly Transform3D ClientToGodot = new(new Basis(new Godot.Vector3(1, 0, 0), new Godot.Vector3(0, 0, -1), new Godot.Vector3(0, 1, 0)), Godot.Vector3.Zero);

    /// <summary>
    /// The effect nif's place: the Eff_UI dummy's client world transform (DummyWorld.Find under the model root) times the nif
    /// root's local transform, whose scale is the effect's when it is not 1 (SetScale) and whose rotation the z turn when the
    /// degrees are not 0 (SetRotate of FromEulerAnglesXYZ(0, 0, degrees * pi / 180)), in the body's frame. Done again only
    /// when the model's global transform changed.
    /// </summary>
    void PlaceEffectUi(Inst i)
    {
        var modelNode = i.Visual.GetNodeOrNull<Node3D>(ModelPath);
        if (modelNode == null || !modelNode.IsInsideTree()) return;
        var g = modelNode.GlobalTransform;
        if (g == i.EffectUiAt) return;
        i.EffectUiAt = g;
        var e = i.EffectUiOf;
        if (DummyWorld.Find(i.Model.Row.EffectDummies, EffUiName.ToString(), World.DummyAnchor.ClientWorld(modelNode), SceneRoot) is not { } w) { i.EffectUi.Visible = false; return; }
        var r = i.EffectUiRoot;
        var own = new Xform(r.T, e.RotateZ != 0 ? DummyWorld.EulerXyz(0, 0, e.RotateZ) : r.R, e.Scale != 1f ? e.Scale : r.S);
        var world = ClientToGodot * ModelBuilder.ToGodot(w.Compose(own)) * ClientToGodot.AffineInverse();
        i.EffectUi.Visible = true;
        i.EffectUi.Transform = i.Visual.GlobalTransform.AffineInverse() * world;
    }

    /// <summary>CAnimationManager::PlayAnimation (animationmanager.cpp:238-290): <paramref name="force"/> (bForce) plays the sequence
    /// from its start even when it is the one playing; without it a Play of the playing sequence goes on where it is.</summary>
    void Play(Inst i, int seqId, float speed = 1f, bool force = false)
    {
        var a = Anim(i.Model, seqId);
        if (a == null) { SequencesMissing++; return; }
        string name = $"s{seqId}";
        var lib = i.Player.GetAnimationLibrary("");
        if (!lib.HasAnimation(name)) lib.AddAnimation(name, a);
        bool again = force && i.Player.CurrentAnimation == name;
        i.Player.Play(name, 0.12);
        if (again) i.Player.Seek(0, true);
        i.Player.SpeedScale = speed;
        i.Playing = name; i.Sequence = seqId; SequencesPlayed++;
        // kept once per play, so the skill state's end (SkillEnd) reads no animation state each frame; from the player's
        // position, since a Play of the animation already playing goes on from where it is rather than from its start
        i.PlayEnds = double.PositiveInfinity;
        if (a.LoopMode == Godot.Animation.LoopModeEnum.None)
        {
            i.PlayEnds = Now + Math.Max(i.Player.CurrentAnimationLength - i.Player.CurrentAnimationPosition, 0) / Math.Max(speed, 0.01f);
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
                CorpseHit = i.Dead && row.CorpseHitAble, DefenceMaterial = row.DefenceMaterial, Boss = row.IsBoss,
                CollisionOffset = new System.Numerics.Vector3(row.CollisionWidthOffset, row.CollisionDepthOffset, row.CollisionHeightOffset),
                BoundOffset = i.RideOwner != 0 && ActorPosOf(i.RideOwner) is { } owner ? owner - p : null,
            });
        }
    }

    /// <summary>WorldService::FindNearestNpc for the talk key's poll (<see cref="World.NpcTalk.NearerTalkNpc"/>) over the live
    /// npcs that are not dead, without an allocation.</summary>
    public World.FieldNpcs.Placed NearestTalkable(int friendly, Vector3 me, Vector3 face, float range, bool frontOnly)
    {
        World.FieldNpcs.Placed best = null;
        float bestD = float.MaxValue;
        foreach (var i in inst.Values)
            if (!i.Dead && i.Talk != null) World.NpcTalk.NearerTalkNpc(i.Talk, friendly, me, face, range, frontOnly, ref best, ref bestD);
        return best;
    }

    /// <summary>The live npcs that are not dead, for the talk.</summary>
    public IEnumerable<World.FieldNpcs.Placed> Talkable => inst.Values.Where(x => !x.Dead && x.Talk != null).Select(x => x.Talk);

    /// <summary>The npc's object kind for its effects (CNpc::vfunc_58): 6 for a boss, else 5; 0 for no such npc.</summary>
    public int CasterKind(int id) =>
        !inst.TryGetValue(id, out var i) ? 0 : table.Find(i.NpcId)?.IsBoss == true ? Ms2.Audio.SoundMusicEffectState.CasterBoss : Ms2.Audio.SoundMusicEffectState.CasterNpc;

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

    /// <summary>
    /// The <see cref="Ms2.Ui.Hud.DamageHitAnchors"/> of an npc: false unless its row has property tag 0x1d
    /// "collision_position" (CNpcData::HasPropertyTag, npcdata.cpp:1387). The client's anchors (CNpc::BuildCollision,
    /// m_arrE68) are one box per &lt;nodeCollisions&gt; row whatever the model has, so whether a mode 5 packet ends
    /// (scenestategame.cpp:7802, an empty array) follows the rows only:
    /// - a tagged row without rows: true with <paramref name="into"/> empty, placeholder or not;
    /// - else <paramref name="into"/> (cleared first) gets in client space the center of each box whose node the skeleton
    ///   has, at the node's world transform, rotation only (vfunc_50, <see cref="Ms2.Ui.Hud.DamageHitEffects.NodeBoxCenter"/>);
    /// - a tagged row with rows on a placeholder, a model without a skeleton, or a skeleton with none of the nodes: false
    ///   (the target's position), since the client's boxes are there but this port cannot place them.
    /// STILL-OPEN: a row whose node the model lacks keeps its box in the client, unplaced by vfunc_50 (:2169), so its
    /// center is the NxBox center never set; here that row is left out of the nearest pick.
    /// </summary>
    public bool CollisionAnchors(int id, List<System.Numerics.Vector3> into)
    {
        if (!inst.TryGetValue(id, out var i)) return false;
        var row = i.Talk?.Row ?? i.Model?.Row;
        if (row == null || !row.CollisionPosition) return false;
        into.Clear();
        var rows = row.NodeCollisions;
        if (rows.Length == 0) return true;
        if (i.Placeholder || i.Visual == null || !GodotObject.IsInstanceValid(i.Visual)) return false;
        var sk = i.Visual.GetNodeOrNull<Skeleton3D>(HoldSkeletonPath);
        if (sk == null || !sk.IsInsideTree()) return false;
        if (i.AnchorBones == null)
        {
            i.AnchorBones = new int[rows.Length];
            for (int k = 0; k < rows.Length; k++) i.AnchorBones[k] = sk.FindBone(rows[k].TargetNode);
        }
        var skWorld = sk.GlobalTransform;
        for (int k = 0; k < rows.Length; k++)
        {
            int b = i.AnchorBones[k];
            if (b < 0) continue;
            var w = RemoteLayer.ToClient * (skWorld * sk.GetBoneGlobalPose(b));
            var q = w.Basis.Orthonormalized().GetRotationQuaternion();
            into.Add(Ms2.Ui.Hud.DamageHitEffects.NodeBoxCenter(rows[k].Volume, rows[k].Offset,
                new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W), new System.Numerics.Vector3(w.Origin.X, w.Origin.Y, w.Origin.Z)));
        }
        return into.Count != 0;
    }

    /// <summary>Plays a named kfm sequence (a skill's motion) on an npc.
    /// STILL-OPEN: it plays at 1; the client's StateSkill plays the motion at its m_fSequenceSpeed through CalcNpcAsiR
    /// (stateskill.cpp:1250-1252).
    /// Returns the animation's name on the body, null when it did not play.</summary>
    public string PlaySequence(int id, string name)
    {
        if (!inst.TryGetValue(id, out var i) || i.Model == null || i.Dead) return null;
        var seq = i.Model.Kfm.ByName(name);
        if (seq == null) return null;
        Play(i, seq.Id);
        return i.Sequence == seq.Id ? i.Playing : null;
    }

    /// <summary>PCBase::vfunc_112 on an npc (CNpc::vfunc_112 @0x141090740 returns early for a dead npc whose corpse is
    /// hittable): a hit sets the shown flag only while it is off, and only for the local player's. A dead npc's bar never
    /// draws either way.</summary>
    public void Damaged(int id, bool byLocalPlayer)
    {
        if (inst.TryGetValue(id, out var i) && !i.Dead && i.Bar is { Shown: false }) i.Bar.Shown = byLocalPlayer;
    }

    /// <summary>
    /// A hit record on the npc (<paramref name="kind"/>): CNpc::vfunc_33 for a cast's (@0x141097c40,
    /// unit_14108e1e0_npcactorandvibrate.cpp:1840), vfunc_34 for a dot's (@0x141098390) and vfunc_35 for a splash's
    /// (@0x141098940, :1957).
    /// - A cast's or a splash's total below 0 plays the npc's damage effect first (FUN_141098fb0(1), :1858 and :1976;
    ///   <see cref="PlayNpcEffect"/>). The dot plays none.
    /// - A cast's total below 0 by the own player arms the npc's shake (<see cref="Ms2.Ui.Skills.NpcVibrate.OnHit"/>,
    ///   :1869); <see cref="Process"/> moves the body with it. The dot and the splash do not shake.
    /// - A cast's total below 0 by the own player also adds the npc's colour boost (AddColorBoost(damagedColorScale, 0.1, 0.1),
    ///   DAT_1439f6ff4 and DAT_1439f6ff8, :1862-1865, before the shake); <see cref="Process"/> runs it.
    /// - A cast's total below 0 whose owner is not the own player (:1874-1880), and any splash's total below 0 (:1979-1982),
    ///   plays the hit sound of the attack's material on the npc's defenceMaterial at the npc's place
    ///   (<see cref="PlayHitSound"/>), after the shake.
    /// - Then, all three: a living npc whose current hp stat is 0 (FUN_141670a00(4), PCBase m_unk_1c0[2][4], pcbase.cpp:1967)
    ///   dies with its dead sound (vfunc_125(1), :1890-1892), whatever the hit's total.
    /// True when the shake armed.
    /// - A boss the own player's cast took health from goes in MyPC's set (FUN_14152e240, :1871), and the killing record
    ///   plays the boss's kill voice (:1889-1902, <see cref="PlayBossDeathVoice"/>); a dot's hit sound (vfunc_34, :1927-1935)
    ///   is RemoteLayer's (PlayRecordSounds).
    /// STILL-OPEN: the rest of the three (PlayInvokeEffectsByEvent(1), the boss camera, the knockback record, the hit basis,
    /// the cast's MakeDeadPhysXActor, the dps meter and vfunc_113) is not ported here.
    /// </summary>
    public bool Hit(int id, NpcHitKind kind, long totalDamage, bool byLocalPlayer, Ms2.Ui.Skills.HitOwner owner,
        Ms2.Ui.Skills.SkillAttackDamage attack, bool ownerBoss = false)
    {
        if (!inst.TryGetValue(id, out var i)) return false;
        var row = table.Find(i.NpcId);
        bool cast = kind == NpcHitKind.Cast;
        if (kind != NpcHitKind.Dot && totalDamage < 0) PlayNpcEffect(i, row, row?.DamageEffect);
        if (cast && row != null && totalDamage < 0 && byLocalPlayer) i.Boost.Add(Now, row.DamagedColorScale, DamagedBoostDuration, DamagedBoostAccumRatio);
        if (cast && row != null && totalDamage < 0 && byLocalPlayer && row.IsBoss) bossHits.Add(id);   // MyPC::FUN_14152e240 (:1871)
        bool armed = cast && row != null
            && i.Vibrate.OnHit(Now, totalDamage, byLocalPlayer, row.DamagedVibrateDuration, row.DamagedVibrateAmp, attack);
        if (row != null && totalDamage < 0 && (kind == NpcHitKind.Splash || (cast && !byLocalPlayer))) PlayHitSound(i, row, byLocalPlayer, owner, attack, ownerBoss);
        if (!i.Dead && i.HpCur == 0) Die(id, true);
        if (i.HpCur == 0) PlayBossDeathVoice(id, i, row, owner, byLocalPlayer);
        return armed;
    }

    /// <summary>
    /// CNpc::FUN_141098fb0 (@0x141098fb0, :2006): a non-empty effect of the row (deadEffect for 0, damageEffect for 1) goes to
    /// InsertEffect with the npc as its actor and object kind 6 for a boss, else 5 (which only the soundMusic parts test).
    /// Played once at the npc's place and facing through the combat layer.
    /// STILL-OPEN: the effect is not attached to the actor (it stays where it started), and the dead kind's removal of the
    /// npc's kept effect (m_nE54, FUN_1411a6060) is not ported.
    /// </summary>
    void PlayNpcEffect(Inst i, NpcRow row, string effect)
    {
        if (string.IsNullOrEmpty(effect) || i.Root == null) return;
        EffectPlayed?.Invoke(i.NpcId, effect);
        Combat?.PlayEffect(effect, i.Root.GlobalPosition, i.Visual?.Rotation.Y ?? 0, row.IsBoss ? Ms2.Audio.SoundMusicEffectState.CasterBoss : Ms2.Audio.SoundMusicEffectState.CasterNpc);
    }

    /// <summary>
    /// The hit sound of a record on the npc: CEffectService::PlayHitSound(GetAttackMaterial(), m_nDefenceMaterial, the owner's
    /// object kind, vfunc_18(), the relation's volume) (:1876-1879, :1979-1982; <see cref="Ms2.Ui.Skills.NpcHitSound"/>).
    /// Nothing plays when the attack's material cannot be told (<see cref="Ms2.Ui.Skills.NpcHitSound.AttackMaterial"/>).
    /// A boss owner's kind is 6 (CNpc::vfunc_58, unit_14108e1e0_npcactorandvibrate.cpp:655), another npc's 5.
    /// </summary>
    void PlayHitSound(Inst i, NpcRow row, bool byLocalPlayer, Ms2.Ui.Skills.HitOwner owner, Ms2.Ui.Skills.SkillAttackDamage attack,
        bool ownerBoss)
    {
        int material = Ms2.Ui.Skills.NpcHitSound.AttackMaterial(owner, attack);
        if (material < 0 || i.Root == null) return;
        int relation = Ms2.Ui.Skills.NpcHitSound.Relation(owner, byLocalPlayer, ownerBoss);
        constants ??= Ms2Godot.World.FieldContents.Constants(packs);
        float volume = Ms2.Ui.Skills.NpcHitSound.Volume(relation, constants);
        HitSoundPlayed?.Invoke(i.NpcId, material, row.DefenceMaterial, relation, volume);
        FieldAudio.Instance?.PlayHitSound(material, row.DefenceMaterial, relation, i.Root.GlobalPosition, volume, Viewer?.Invoke(), hitSoundRand);
    }

    Ms2.Formats.World.ClientConstants constants;

    /// <summary>The bosses the own PC's cast hit took health from (MyPC m_set15f8), for <see cref="PlayBossDeathVoice"/>.</summary>
    readonly Ms2.Ui.Skills.BossHits bossHits = new();

    /// <summary>The npc's row, null for no such npc or an id the table lacks.</summary>
    public NpcRow RowOf(int id) => inst.TryGetValue(id, out var i) ? table.Find(i.NpcId) : null;

    /// <summary>
    /// The voice of a boss's death by a record, played for the own PC (CNpc::vfunc_33 :1889-1902, the same in vfunc_34 and
    /// vfunc_35; <see cref="Ms2.Ui.Skills.CombatVoice.BossDeathVoice"/>): Boss_EndKill for the own PC's record, else Boss_Kill
    /// when the own PC is alive, within BossKillSoundRange of the boss and hit it before. The distance is the own player's
    /// place to the boss's (the boss's focus distance, GameObject m_unk_bc). It runs for the first record that finds the hp at
    /// 0 (<see cref="Ms2.Ui.Skills.BossHits.FirstKill"/>), as an hp 0 stat packet may have run <see cref="Die"/> before it.
    /// </summary>
    void PlayBossDeathVoice(int id, Inst i, NpcRow row, Ms2.Ui.Skills.HitOwner owner, bool byLocalPlayer)
    {
        var live = LiveSession.Instance;
        if (row == null || !row.IsBoss || live == null || i.Root == null || Viewer?.Invoke() is not { } player || !bossHits.FirstKill(id)) return;
        constants ??= Ms2Godot.World.FieldContents.Constants(packs);
        int type = Ms2.Ui.Skills.CombatVoice.BossDeathVoice(true, Ms2.Ui.Skills.CombatVoice.ObjectKind(owner, byLocalPlayer, false),
            !live.Me.Dead, player.DistanceTo(i.Root.GlobalPosition), Ms2.Ui.Skills.CombatVoice.BossKillRange(constants), bossHits.Contains(id));
        if (LiveSession.LiveCheck) GD.Print($"audio: boss death voice {id}: type {type}, hit by the own PC {bossHits.Contains(id)}");
        if (type != 0)
            FieldAudio.Instance?.PlayEventVoice(type, live.Me.User.Gender, live.Me.User.JobCode, true,
                Ms2.Audio.SoundMusicEffectState.CasterMyPc, player, player);
    }

    /// <summary>The own player's place, FUN_1411a5110's view distance origin for the hit sound; none when null.</summary>
    public Func<Vector3?> Viewer;

    /// <summary>The field camera's eye, null with no camera (for the boss music's camera point).</summary>
    public Func<Vector3?> CameraEye;

    Vector3 MusicPoint()
    {
        static System.Numerics.Vector3? N(Vector3? v) => v is { } x ? new(x.X, x.Y, x.Z) : null;
        var p = Ms2.Audio.BossBgmTracker.MusicPoint(N(Viewer?.Invoke()), N(CameraEye?.Invoke()));
        return new Vector3(p.X, p.Y, p.Z);
    }

    /// <summary>The npc id, attack material, defence material, relation and volume of each hit sound asked for (for the checks).</summary>
    public Action<int, int, int, int, float> HitSoundPlayed;

    /// <summary>The npc id and effect xml of each damage or dead effect played (for the checks).</summary>
    public Action<int, string> EffectPlayed;

    // DAT_1439f6ff4, DAT_1439f6ff8: the hit boost's CColorBoostData duration and accumulation ratio
    const float DamagedBoostDuration = 0.1f, DamagedBoostAccumRatio = 0.1f;

    /// <summary>The colour boost last written to the npc's body (1 before any); null when there is no npc of the id.</summary>
    public float? ColorBoostOf(int id) => inst.TryGetValue(id, out var i) ? i.Boost.Scale : null;

    /// <summary>The npc's character-shader materials the boost writes to (gathered on the first boost); null before it.</summary>
    public IReadOnlyList<ShaderMaterial> BoostMaterialsOf(int id) => inst.TryGetValue(id, out var i) ? i.BoostMaterials : null;

    /// <summary>
    /// SetColorScale (util.cpp:717): the scale goes to the "ColorBoost" float of every render object under the actor, the
    /// character shader's color_boost here, in place of the nif's own value. The shadow-only twins draw no colour.
    /// </summary>
    static void GatherBoostMaterials(Node n, List<ShaderMaterial> into)
    {
        if (n is GeometryInstance3D g && g.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly)
        {
            if (g.MaterialOverride is ShaderMaterial o) AddPasses(o, into);
            else if (g is MeshInstance3D mi && mi.Mesh != null)
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    if ((mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s)) is ShaderMaterial sm) AddPasses(sm, into);
        }
        for (int c = 0; c < n.GetChildCount(); c++) GatherBoostMaterials(n.GetChild(c), into);

        static void AddPasses(ShaderMaterial m, List<ShaderMaterial> into)
        {
            for (var p = m; p != null; p = p.NextPass as ShaderMaterial)
                if (ModelBuilder.IsCharacterShader(p.Shader) && !into.Contains(p)) into.Add(p);
        }
    }

    /// <summary>The npc's body offset from its root by the shake (zero when none runs); null when there is no npc of the id.</summary>
    public Godot.Vector3? ShakeOf(int id) => inst.TryGetValue(id, out var i) && i.Visual != null ? i.Visual.Position - i.VisualBase : null;

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
        // STILL-OPEN: the client's OnStat only fills the bar; its npc dies on FieldDeadNpc, the add packet's flag (Die)
        // or a hit record that finds the hp stat at 0 (Hit). Here a hit runs when its packet comes in, not when the cast's
        // magic model lands it, so a stat packet after the damage packet would leave the npc standing: the hp 0 death
        // stays here until the hit runs on the cast's landing.
        if (cur <= 0) Die(id, true);
    }

    /// <summary>Whether the npc of the object id is dead (PCBase::IsDead, m_unk_15c); false when there is none.</summary>
    /// <summary>The id of the npc model's sequence of that name, -1 when the npc or the sequence is missing.</summary>
    public int SequenceId(int id, string name) => inst.TryGetValue(id, out var i) ? i.Model?.Kfm.ByName(name)?.Id ?? -1 : -1;

    /// <summary>The id of the npc model's first sequence whose name <paramref name="pick"/> takes, -1 when there is none.</summary>
    public int SequenceId(int id, Func<string, bool> pick) =>
        inst.TryGetValue(id, out var i) ? i.Model?.Kfm.Sequences.FirstOrDefault(q => pick(q.Name))?.Id ?? -1 : -1;

    /// <summary>The animation the npc's player is on (a queued one once it started), null when the npc or its model is missing.</summary>
    public string CurrentAnimation(int id) => inst.TryGetValue(id, out var i) && i.Player != null ? i.Player.CurrentAnimation : null;

    /// <summary>The speed the npc's animation plays at, 0 when the npc or its model is missing.</summary>
    /// <summary>Whether the npc's body is shown (the spawn state's hide reason off).</summary>
    public bool BodyShown(int id) => inst.TryGetValue(id, out var i) && i.Visual != null && i.Visual.Visible;
    public float AnimSpeed(int id) => inst.TryGetValue(id, out var i) && i.Player != null ? (float)i.Player.SpeedScale : 0;

    public bool IsDead(int id) => inst.TryGetValue(id, out var i) && i.Dead;

    /// <summary>The npc's Eff_UI anchor (vfunc_10) as the hp bar reads it; null when there is no npc of the id.</summary>
    public Func<Godot.Vector3> AnchorOf(int id) => inst.TryGetValue(id, out var i) ? i.Anchor : null;

    /// <summary>The npc's root node, dead or not; null when there is no npc of the id.</summary>
    public Node3D RootOf(int id) => inst.TryGetValue(id, out var i) && i.Root != null && GodotObject.IsInstanceValid(i.Root) ? i.Root : null;

    /// <summary>The npc's position in the client's z-up coordinates (vfunc_18) and its rotation z in degrees (vfunc_20, the
    /// one the moves set, not the eased visual); null when there is no npc of the id.</summary>
    public (System.Numerics.Vector3 Pos, float RotZ)? PoseOf(int id)
    {
        if (!inst.TryGetValue(id, out var i) || i.Root == null || !GodotObject.IsInstanceValid(i.Root)) return null;
        var g = i.Root.GlobalPosition;
        return (new System.Numerics.Vector3(g.X, -g.Z, g.Y), i.TargetYaw);
    }

    /// <summary>The pc of an object id an owner effect is kept on (WorldService::FindPCById: the own pc or another player's
    /// body); null when none is here.</summary>
    public Func<int, Node3D> FindPc = _ => null;
    /// <summary>CEffectService::FUN_1411a49c0 with an actor target: the kept effect's id, 0 when it did not spawn.</summary>
    public Func<string, Node3D, int, int> SpawnKept = (_, _, _) => 0;
    /// <summary>CEffectService::FUN_1411a6060: a kept effect stops at once.</summary>
    public Action<int> StopKept = _ => { };
    /// <summary>The NoticeMessage object kind AttachOwnerEffect passes (m_nObject = 6, the boss npc's kind).</summary>
    public const int OwnerEffectKind = 6;

    /// <summary>
    /// NpcNotice mode 1 (CSceneStateGame::OnNpcNotice, scenestategame.cpp:9158-9166): FindNpcById, then CNpc::SetOwnerEffect
    /// (@0x141094fa0, unit_14108e1e0_npcactorandvibrate.cpp:1559-1564): the name kept, the old effect released and the new one
    /// attached. False when there is no npc of the id.
    /// </summary>
    public bool SetOwnerEffect(int id, string effect)
    {
        if (!inst.TryGetValue(id, out var i)) return false;
        i.OwnerEffect = effect ?? "";
        ReleaseOwnerEffect(i);
        AttachOwnerEffect(i);
        return true;
    }

    /// <summary>The owner effect an npc keeps (the name, the pc it sits on, the kept id); null when there is no npc of the id.</summary>
    public (string Effect, int Actor, int Kept)? OwnerEffectOf(int id) =>
        inst.TryGetValue(id, out var i) ? (i.OwnerEffect, i.OwnerActor, i.OwnerEffectId) : null;

    /// <summary>
    /// CNpc::SetOwnerEffectActor (@0x141094ff0, :1567-1575): a new actor moves the effect to it; the same actor keeps it. The
    /// client's kept effect names its actor by object id (NoticeMessage::SetActor in AttachOwnerEffect), so an owner that left
    /// and came back with the same id has it again; here the effect hangs on the pc's node, so the same actor with a new node
    /// (it left and was added again) attaches it again on that node.
    /// </summary>
    void SetOwnerEffectActor(Inst i, int actor)
    {
        int old = i.OwnerActor;
        i.OwnerActor = actor;
        if (actor == old && (i.OwnerEffect.Length < 1 || FindPc(actor) is not { } pc || !GodotObject.IsInstanceValid(pc) || pc == i.OwnerNode)) return;
        ReleaseOwnerEffect(i);
        AttachOwnerEffect(i);
    }

    /// <summary>CNpc::AttachOwnerEffect (@0x141095050, :1579-1592): with the actor a pc here and a name set, the effect is kept
    /// on that pc (SetActor, object kind 6, m_bFlag31).</summary>
    void AttachOwnerEffect(Inst i)
    {
        if (FindPc(i.OwnerActor) is not { } pc || !GodotObject.IsInstanceValid(pc)) return;
        if (i.OwnerEffect.Length < 1) return;
        i.OwnerEffectId = SpawnKept(i.OwnerEffect, pc, OwnerEffectKind);
        i.OwnerNode = pc;
    }

    /// <summary>CNpc::ReleaseOwnerEffect (@0x141095140, :1595-1600): the kept effect stopped at once.</summary>
    void ReleaseOwnerEffect(Inst i)
    {
        if (i.OwnerEffectId != 0) StopKept(i.OwnerEffectId);
        i.OwnerEffectId = 0;
        i.OwnerNode = null;
    }

    /// <summary>
    /// The world point of an actor's Ride_A node (PCBase::FUN_141675930, m_spBE8, which PCBase::vfunc_119 finds by name on the
    /// actor's model root, pcbase.cpp:3039-3041/3065-3067) for the actor of an object id of any kind (WorldService::FUN_141bd1200);
    /// null when no such actor is here or its model has no such node. Only npc models have one in the data (the vehicle npcs
    /// and the mount models); a pc body has none.
    /// </summary>
    public Func<int, Godot.Vector3?> RideNodeOf = _ => null;
    /// <summary>The position (vfunc_18, client space) of the actor of an object id of any kind (WorldService::FUN_141bd1200);
    /// null when none is here. CNpc::vfunc_51 of a bound npc reads its owner's.</summary>
    public Func<int, System.Numerics.Vector3?> ActorPosOf = _ => null;

    /// <summary>
    /// NpcRide after OnNpcRide's FindNpcById (scenestategame.cpp:4871-4877): CNpc::FUN_141092c50 (@0x141092c50,
    /// unit_14108e1e0_npcactorandvibrate.cpp:1036-1046), mode 0 the accumulated damage (FUN_141095240, m_nEE0), mode 1 the
    /// owner actor (m_nF20), mode 2 none; another mode changes nothing. False when there is no npc of the id.
    /// </summary>
    public bool Ride(Ms2.Net.Packets.NpcRide p)
    {
        if (!inst.TryGetValue(p.ObjectId, out var i)) return false;
        switch (p.Mode)
        {
            case Ms2.Net.Packets.NpcRide.Damage: i.AccumDamage = p.AccumulatedDamage; break;
            case Ms2.Net.Packets.NpcRide.Ride: i.RideOwner = p.Owner; break;      // placed by the next frame's update
            case Ms2.Net.Packets.NpcRide.RideEnd: i.RideOwner = 0; break;
        }
        return true;
    }

    /// <summary>An npc's ride state (m_nF20, m_nEE0) and where its root is; null when there is no npc of the id.</summary>
    public (int Owner, long AccumDamage, Godot.Vector3 At)? RideOf(int id) =>
        inst.TryGetValue(id, out var i) ? (i.RideOwner, i.AccumDamage, i.Root.GlobalPosition) : null;

    /// <summary>
    /// CNpc::FUN_141093eb0 (@0x141093eb0, :1295-1307): with an owner, the npc's position (vfunc_19) is the world point of the
    /// owner's Ride_A node; an owner that is not here or has no such node leaves the npc where it is. The next frame's move
    /// starts from there, as the client's does.
    /// </summary>
    void RideStep(Inst i)
    {
        if (i.RideOwner == 0 || RideNodeOf(i.RideOwner) is not { } at) return;
        i.Root.GlobalPosition = at;
    }

    /// <summary>The npc a followed pc copies (StateTriggerFollowNpc::vfunc_3, stateemotionidle.cpp:521-562): <see cref="PoseOf"/>
    /// and its sync sequence key (CNpc::FUN_141093510, m_nDE8); null when there is no npc of the id.</summary>
    public (System.Numerics.Vector3 Pos, float RotZ, short AnimKey)? FollowPoseOf(int id) =>
        PoseOf(id) is { } p && inst.TryGetValue(id, out var i) ? (p.Pos, p.RotZ, i.AnimKey) : null;

    /// <summary>
    /// CNpc::vfunc_125 (@0x141091210): the npc dies (m_unk_15c = 1) and enters StateDeadNPC (0xc), which plays its
    /// death sequence; <paramref name="show"/> (OnFieldDeadNpc passes 1, OnFieldAddNpc's dead flag 0,
    /// scenestategame.cpp:3556-3558/3610) plays its dead sound (m_strSoundDead) too.
    /// StateDeadNPC::vfunc_4 (statedead.cpp:224) plays <see cref="NpcRow.DeadSequence"/> by name; a model without that
    /// sequence plays nothing (PlayAnimation finds no sequence) and counts in <see cref="NoDeathSequence"/>.
    /// With <paramref name="show"/> the npc's dead effect plays before the sound (FUN_141098fb0(0), :2134; <see cref="PlayNpcEffect"/>).
    /// A boss leaves the field's boss music tracker (:2150, RemoveCombatBgmTracker).
    /// STILL-OPEN: FUN_141099ab0, the corpse box (m_corpse) and the corpse effect are not ported.
    /// </summary>
    public void Die(int id, bool show)
    {
        if (!inst.TryGetValue(id, out var i) || i.Dead) return;
        i.Dead = true; Deaths++;
        i.State = 0xc;                 // StateDeadNPC
        Tags?.Died(i.Root);
        if (i.Bar != null) { i.Bar.Dead = true; Bars?.Remove(i.Root); }      // vfunc_8: a dead npc's bar drops for good; its entry and clip go back to the pool
        var row = table.Find(i.NpcId);
        if (show) PlayNpcEffect(i, row, row?.DeadEffect);
        var dead = show ? row?.SoundDead : null;
        if (!string.IsNullOrEmpty(dead)) FieldAudio.Instance?.PlayEventAt(dead, i.Root.GlobalPosition);
        BossGone(id, i);
        if (i.HpMat != null) { i.Root.GetNodeOrNull("HpBar")?.QueueFree(); i.HpMat = null; }
        if (i.Model == null) return;
        var seq = row == null ? null : i.Model.Kfm.ByName(row.DeadSequence(show, i.DeadAction));
        var a = seq == null ? null : Anim(i.Model, seq.Id);
        if (a == null) { NoDeathSequence++; return; }
        a.LoopMode = Godot.Animation.LoopModeEnum.None;
        string name = $"s{seq.Id}";
        var lib = i.Player.GetAnimationLibrary("");
        if (!lib.HasAnimation(name)) lib.AddAnimation(name, a);
        i.Player.Play(name, 0.1);
        i.Player.SpeedScale = 1f;      // StateDeadNPC::vfunc_4 plays it at 1.0 (statedead.cpp:250), whatever the last block's speed
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

    /// <summary>
    /// A sync block as CNpc::ApplySyncBlock (@0x1410918e0) applies it: the npc is put at the block position, its target yaw is the block's
    /// (snapped when the rotate flag is set), and the block velocity moves it until the next block (<see cref="Process"/>). On a Run_ sequence
    /// an npc more than 30 off the block position does not jump to it: when the point 0.3 s ahead on the velocity is up to twice as far from the npc
    /// as from the block position, the ratio clamped to 0.8 - 1.2 scales the velocity so the npc catches up, unless that is within 3 % of 1.
    /// The animation plays at the block's speed, unclamped (CAnimationManager::PlayAnimation passes fScale through); a Run_ sequence at the
    /// row's anispeed times PCBase::vfunc_97 (<see cref="ScaleRate"/>), times the unclamped ratio while it catches up. As in
    /// CAnimationManager::PlayAnimation the speed only takes when the sequence changes.
    /// STILL-OPEN: a block speed of 0 is no clean freeze in either client: Godot scales the cross-fade by SpeedScale too, and the
    /// client's frequency 0 gives a NaN or infinite sequence time (NiControllerSequence.cpp:755).
    /// A jump block (-2/-3) starts the arc mover m_unkDB0 (<see cref="Ms2.Net.Replay.NpcJumpArc"/>, :916-929). One without the arc data
    /// plays its Jump_A / Jump_B from its start at the block speed (:928); one with it plays nothing here, and entering the jump state
    /// (6) plays the arc's sequence at 1 (StateJumpNpc::vfunc_4, stateidle.cpp:1014-1021). While the arc runs a block neither places
    /// nor turns the npc (:961) and <see cref="Process"/> moves it along the arc; the block velocity is kept either way (:972) and
    /// moves the npc once the arc ended.
    /// A block of state 0x11 (spawn) plays no sequence of its own (:953). With a new serial, while the npc is not in the spawn
    /// state, it enters StateSpawn (:975-984) with the sequence of the field's spawn point that its spawn id names
    /// (<see cref="SpawnAnimation"/>), or with none when the id is 0 or names no point (the plain change, :1027); see
    /// <see cref="EnterSpawn"/>. A block of the spawn state while the npc is in it changes nothing (State::vfunc_25,
    /// statemanager.cpp:297).
    /// In the hit state (0xd) the npc keeps its own height (:962-963), and a block of state 0xd with a new serial plays its sequence
    /// again from its start at the block speed (:1022), after the plain play of the block's sequence (:955).
    /// STILL-OPEN: the hit state is left here only by a block that enters another state; the client's StateHitNPC ends by itself
    /// at the later of its sequence's end and the block's pause plus push time, so the height is kept for longer here.
    /// STILL-OPEN: the additional-list flag (AdditionalCliList::FUN_1409709b0, :953) that also stops the sequence is not followed.
    /// </summary>
    public void Move(Ms2.Net.Replay.NpcMovedEvent e)
    {
        var b = e.Sync;
        if (!inst.TryGetValue(b.ObjectId, out var i)) return;
        i.Target = Placed(b.Position.ToVector3());
        i.Moves++;
        i.AnimKey = b.Sequence;                     // CNpc::ApplySyncBlock (:899): m_nDE8, read whatever the state
        SetOwnerEffectActor(i, b.OwnerActor);       // :959, the block's owner actor (0 for a non-boss block)
        bool hitHeld = i.State == 0xd;              // GetCurStateId() before the block's own state is entered (:962)
        bool fresh = b.Serial != i.Serial;
        byte before = i.State;
        var talk = SyncState(i, b.State, b.Serial);
        if (fresh && b.State == 0x11 && i.State == 0x11 && before != 0x11) EnterSpawn(i, b);
        if (i.Dead) { i.Velocity = default; i.Eases = true; return; }       // a dead body keeps its last frame
        i.Eases = false;
        var vel = G(b.Velocity.ToVector3());
        bool place = true;
        float speed = b.AnimSpeed;
        var at = b.Position.ToVector3() + new System.Numerics.Vector3(0, 0, World.FieldNpcs.PacketLift);
        if (i.Arc.Begin(b, at))
        {
            if (!b.JumpAbsolute) PlayNamed(i, i.Arc.Name, speed, true);
        }
        else
        {
            string name = b.Sequence >= 0 ? i.Model?.Kfm.ById(b.Sequence)?.Name : null;
            if (name != null && name.StartsWith("Run_", System.StringComparison.Ordinal))
            {
                speed = i.AniSpeed;
                if (i.Root.Position.DistanceTo(i.Target) > 30f)
                {
                    var ahead = i.Target + vel * 0.3f;
                    float ratio = (ahead - i.Root.Position).Length() / (ahead - i.Target).Length();
                    if (ratio < 2f)
                    {
                        float scale = Mathf.Clamp(ratio, 0.8f, 1.2f);
                        if (scale > 1.03f || scale < 0.97f) { speed *= ratio; vel *= scale; place = false; }
                    }
                }
            }
            if (i.Model != null && name != null && b.Sequence != i.Sequence && b.State != 0x11) Play(i, b.Sequence, speed);
            if (i.Model != null && name != null && fresh && b.State == 0xd) Play(i, b.Sequence, speed, true);
        }
        if (fresh && b.State == 6 && talk == Ms2.Net.Replay.NpcTalkFacing.Step.Enter && i.Arc.Moving) PlayNamed(i, i.Arc.Name, 1f, false);
        if (!i.Arc.Moving)
        {
            if (hitHeld) i.Target.Y = i.Root.Position.Y;
            if (place) i.Root.Position = i.Target;
            i.TargetYaw = b.RotationZ;
            if (b.Rotates && !i.Placeholder) i.Visual.Rotation = new Godot.Vector3(0, Mathf.DegToRad(b.RotationZ), 0);
        }
        // the talk branch's LookAtPoint(local player, 0) comes after the block's SetTargetYaw (:966, :1003): the npc turns
        // to the own PC at its rotation speed, no snap
        if (talk == Ms2.Net.Replay.NpcTalkFacing.Step.LookAtPlayer && OwnPosition() is { } me)
            i.TargetYaw = Ms2.Net.Replay.NpcTalkFacing.LookYaw(new System.Numerics.Vector3(i.Root.Position.X, -i.Root.Position.Z, i.Root.Position.Y), me);
        i.Velocity = vel;
    }

    /// <summary>
    /// The spawn branch of ApplySyncBlock (:975-984) and StateSpawn::vfunc_4 (statespawn.cpp:65-77): the npc and its shadow are
    /// hidden (AddHideReason(10), SetShadowVisible(false)) for the delay its object id draws, with the sequence of the point the
    /// block's spawn id names. <see cref="Process"/> runs the state (<see cref="SpawnStep"/>).
    /// </summary>
    void EnterSpawn(Inst i, Ms2.Net.Packets.NpcSync b)
    {
        int id = b.SpawnId;
        i.Spawn.Enter(b.ObjectId, id != 0 ? SpawnAnimation(id) : null, Now * 1000.0);
        // GetAnimationDuration (0x14099eb30): the sequence's length, 0 for one the body lacks
        i.SequenceLength ??= name => i.Model?.Kfm.ByName(name) is { } seq ? (float)(Anim(i.Model, seq.Id)?.Length ?? 0) : 0f;
        SpawnHidden(i, true);
        if (LiveSession.LiveCheck) GD.Print($"live: npc {b.ObjectId} spawn state, point {id}, sequence {i.Spawn.Animation ?? "none"}, hidden {i.Spawn.Delay} ms");
    }

    /// <summary>PCBase::AddHideReason / RemoveHideReason of the spawn state's reason, with the simple shadow (statespawn.cpp:72-73, :88-89).</summary>
    static void SpawnHidden(Inst i, bool hidden)
    {
        if (i.Visual != null) i.Visual.Visible = !hidden;
        FieldShadows.SetShadowVisible(i.Root, !hidden);
    }

    /// <summary>
    /// StateSpawn::vfunc_3 (statespawn.cpp:94-130) for one frame, and vfunc_5 (:80-92) once the npc left the state some other
    /// way (a block of another state, a death, a cast): shown again. At the delay's end the npc is shown and the point's
    /// sequence plays forced at speed 1 and the npc data's regenEffect plays at its place; at its length the npc goes to the idle
    /// state (vfunc_59(1)).
    /// </summary>
    void SpawnStep(Inst i, float dt)
    {
        if (i.State != 0x11) { i.Spawn.Leave(); SpawnHidden(i, false); return; }
        var step = i.Spawn.Update(dt, Now * 1000.0, i.SequenceLength);
        if (step is Ms2.Ui.Skills.NpcSpawnState.Step.Show or Ms2.Ui.Skills.NpcSpawnState.Step.ShowAndIdle)
        {
            SpawnHidden(i, false);
            if (!string.IsNullOrEmpty(i.Spawn.Animation)) PlayNamed(i, i.Spawn.Animation, 1f, true);
            // the npc data's regenEffect at the npc's place, no actor and object kind 0 (:114-127)
            if (i.Root != null && table.Find(i.NpcId)?.RegenEffect is { Length: > 0 } regen)
            {
                EffectPlayed?.Invoke(i.NpcId, regen);
                Combat?.PlayEffect(regen, i.Root.GlobalPosition, Basis.Identity, Ms2.Audio.SoundMusicEffectState.CasterSystem);
            }
        }
        if (step is Ms2.Ui.Skills.NpcSpawnState.Step.Idle or Ms2.Ui.Skills.NpcSpawnState.Step.ShowAndIdle) i.State = 1;
    }

    /// <summary>PlayAnimation by name (PCBase::PlayAnimation): nothing when the body has no such sequence; without
    /// <paramref name="force"/> nothing when it is the sequence playing.</summary>
    void PlayNamed(Inst i, string name, float speed, bool force)
    {
        var seq = i.Model?.Kfm.ByName(name);
        if (seq == null || !force && seq.Id == i.Sequence) return;
        Play(i, seq.Id, speed, force);
    }

    /// <summary>
    /// The state part of CNpc::ApplySyncBlock (unit_14108e1e0_npcactorandvibrate.cpp:973-1032): a block whose serial is the last one
    /// (m_nDAC) changes nothing; otherwise the serial is kept and the block's state entered (vfunc_59), except 0x12, gosNone (0) and a
    /// state the npc does not have (<see cref="Ms2.Net.Replay.NpcStates.BlockEnters"/>).
    /// The spawn (0x11), talk (0x16) and hit (0xd) branches enter their state too, with their data.
    /// The talk branch (:986-1006, <see cref="Ms2.Net.Replay.NpcTalkFacing.OnBlock"/>) keeps the state or looks at the own PC
    /// (the step is returned; <see cref="Move"/> turns the npc after the block's yaw); entering the talk state sets its phase
    /// from <see cref="Talking"/> (StateTalkNpc::vfunc_4).
    /// STILL-OPEN: the additional-list flag (AdditionalCliList::FUN_1409709b0, :1025) and the current state's refusal of the same
    /// state again (State::vfunc_25, statemanager.cpp:297) are not followed; the state is taken as entered. The talk state's own motion (useTalkMotion) is not played from it.
    /// </summary>
    Ms2.Net.Replay.NpcTalkFacing.Step SyncState(Inst i, byte state, ushort serial)
    {
        if (serial == i.Serial) return Ms2.Net.Replay.NpcTalkFacing.Step.Keep;
        i.Serial = serial;
        bool talking = Talking();
        var row = i.Talk?.Row ?? i.Model?.Row;
        var step = Ms2.Net.Replay.NpcTalkFacing.OnBlock(state, i.State, i.TalkPhase, talking, row?.LookAtMyPcWhenTalking ?? true);
        if (step != Ms2.Net.Replay.NpcTalkFacing.Step.Enter) return step;
        if (Ms2.Net.Replay.NpcStates.BlockEnters(state) && !i.Dead)
        {
            i.State = state;
            if (state == Ms2.Net.Replay.NpcTalkFacing.TalkState) i.TalkPhase = Ms2.Net.Replay.NpcTalkFacing.EnterPhase(talking);
        }
        return step;
    }

    /// <summary>UITalkManager::GetTalking() != 0: a talk with an npc is open (the talk branch's test, :987 and :998).</summary>
    public Func<bool> Talking = () => false;
    /// <summary>The local player's position in client space (the talk look's target, :1003); null without one.</summary>
    public Func<System.Numerics.Vector3?> OwnPosition = () => null;

    /// <summary>The npc's state (GetCurStateId), 0 when there is no npc of the id.</summary>
    public byte StateOf(int id) => inst.TryGetValue(id, out var i) ? i.State : (byte)0;

    /// <summary>
    /// StateSkill::PlayCastStartEffect (@0x1417ea750, stateskill.cpp:1820-1829) on an npc: a motion with a sequence goes to
    /// PCBase::FUN_141673ce0, which replaces the record list (m_unk_8e8) unless it holds the uid and motion already, and a new
    /// record enters StateSkill (0x10). False for a dead npc, a motion without a sequence or the record already there.
    /// <paramref name="anim"/> is the sequence as played on the body by <see cref="PlaySequence"/> just before (null when the
    /// body lacks it); <paramref name="last"/> is whether the motion is the level's last (<see cref="Process"/> ends the state
    /// on it).
    /// </summary>
    public bool BeginCast(int id, long castUid, int motionIndex, bool last, Ms2.Formats.World.SkillMotion motion, string anim)
    {
        if (!inst.TryGetValue(id, out var i) || i.Dead || motion == null || string.IsNullOrEmpty(motion.SequenceName)) return false;
        if (i.Cast != null && i.CastUid == castUid && i.CastMotion == motionIndex) return false;
        i.Cast = motion; i.CastUid = castUid; i.CastMotion = motionIndex; i.CastSeq = anim != null ? i.Sequence : -1; i.CastLast = last; i.CastEnded = -1;
        i.State = 0x10;
        return true;
    }

    /// <summary>
    /// The npcs WorldService::FUN_141bc8420 takes as force zones (worldservice_1.cpp:5535-5570, the state test at :5542): in
    /// StateSkill (0x10) with a cast record whose motion has a push cylinder, centred at the npc's position (client axes, z
    /// up). Fills <paramref name="into"/> after clearing it.
    /// </summary>
    public void ForceSources(List<Ms2.Formats.World.ForceZone.Source> into)
    {
        into.Clear();
        foreach (var i in inst.Values)
        {
            if (i.State != 0x10 || i.Cast?.PushCylinder is not { } cyl || i.Root == null) continue;
            if (cyl.VelH == 0f && cyl.VelV == 0f) continue;   // a motion without a push cylinder: ForceZone skips it anyway
            var g = i.Root.GlobalPosition;
            into.Add(new Ms2.Formats.World.ForceZone.Source(new System.Numerics.Vector3(g.X, -g.Z, g.Y), cyl));
        }
    }

    /// <summary>
    /// StateSkill's own end for an npc (not the own pc, no rider), FUN_1417ee120 (stateskill.cpp:2885-2992):
    /// - it returns while the cast's sequence plays (!m_b48, :2889-2893); EndOfSequence (:1054-1070) sets m_b48 and keeps the
    ///   sequence's event time in m_fStartTime. Here the sequence is over when the time its Play kept has passed or another
    ///   sequence took the body, and the time kept is the layer's at the first frame that sees it, not the animation's event
    ///   time: the two are taken as one clock (inferred);
    /// - the record's motions over (CSkillRecord::FUN_141759e40, skillrecord.cpp:315) go to StateIdle through vfunc_59(1)
    ///   (:2951-2958) when the level's doesPlayIdleOnSkillEnd is set, its default (skillleveldata.cpp:1017, :1203);
    /// - otherwise, 5 s after m_fStartTime (:2990), FUN_1417f0070 goes to StateIdle.
    /// A cast whose sequence did not play here (a placeholder body without a model, or a model without the sequence) counts as
    /// over at its start, so those two rules end it. The client has no such case for a placeholder, and for a model without
    /// the sequence it waits for EndOfSequence; a bound of the port, so a push cylinder does not act without end.
    /// STILL-OPEN: doesPlayIdleOnSkillEnd is not read (a level that clears it holds the state to the 5 s check, which
    /// changes no zone: vfunc_103 has no record past the last motion, pcbase.cpp:2703-2712); FUN_1417f0070's StateHold
    /// (0x20) for the record's extra (m_nB0) and a looping motion (m_nRepeat) are not followed.
    /// </summary>
    void SkillEnd(Inst i)
    {
        if (i.CastEnded < 0)
        {
            if (i.CastSeq >= 0 && i.Sequence == i.CastSeq && Now < i.PlayEnds) return;
            i.CastEnded = Now;
        }
        if (i.CastLast || Now > i.CastEnded + 5.0) i.State = 1;
    }

    /// <summary>Where the busiest npc is (the one that has had the most sync blocks), for a camera that follows the action.</summary>
    public Godot.Vector3? ActivePosition() => inst.Count == 0 ? null : inst.Values.MaxBy(i => i.Moves).Root.Position;

    /// <summary>A boss npc's RemoveCombatBgmTracker (its death, removal or replacement); nothing for another npc.</summary>
    static void BossGone(int id, Inst i)
    {
        if (i.BossRow != null) FieldAudio.Instance?.BossRemoved((ulong)id);
    }

    public void Process(float dt)
    {
        Now += dt;
        long nowMs = NowMs;
        var audio = FieldAudio.Instance;
        Vector3? me = null; bool viewerRead = false;
        foreach (var i in inst.Values)
        {
            i.Face?.Tick(nowMs);                              // PCBase::vfunc_53 (pcbase.cpp:3729): the face first (FUN_14166f940)
            // CNpc::vfunc_53 (:2354-2359): a live boss's distance from the camera point (GameObject m_unk_c4,
            // Ms2.Audio.BossBgmTracker.MusicPoint) against its bossSoundDistance / bossSoundEndDistance. The port's camera
            // target is the own player (no entity camera on an npc, no observer); without one, the camera's eye.
            if (i.BossRow != null && !i.Dead && audio != null)
            {
                if (!viewerRead) { me = MusicPoint(); viewerRead = true; }
                if (me is { } at)
                    audio.BossDistance((ulong)i.Talk.ObjectId, i.Root.GlobalPosition.DistanceTo(at), i.BossRow.BossSoundDistance, i.BossRow.BossSoundEndDistance);
            }
            if (i.Eases)
            {
                var d = i.Target - i.Root.Position;
                if (d.Length() > 600) i.Root.Position = i.Target;
                else i.Root.Position += d * Mathf.Min(1f, dt * 10f);
            }
            else if (i.Arc.Moving)                            // CNpc::vfunc_53 (:2360-2366): the arc's point and yaw target
            {
                float yaw = i.TargetYaw;
                i.Root.Position = G(i.Arc.Advance(dt, ref yaw));
                i.TargetYaw = yaw;
            }
            else i.Root.Position += i.Velocity * dt;          // CNpc::vfunc_53 (@0x1410952f0): pos += m_posD40 * time
            if (!i.Placeholder) Turn(i, dt);
            if (i.EffectUi != null) PlaceEffectUi(i);
            if (i.State == 0x10) SkillEnd(i);
            if (i.Spawn.Active) SpawnStep(i, dt);
            // the CNpc update's shake (:2419-2424): the drawn point is the npc's own plus the offset, each frame anew
            if (i.Vibrate.Active)
            {
                var off = System.Numerics.Vector3.Zero;
                i.Visual.Position = i.Vibrate.Update(Now, ref off, Rand) ? i.VisualBase + G(off) : i.VisualBase;
            }
            // the CNpc update's ride (:2425): at the owner's Ride_A node each frame, after the shake
            if (i.RideOwner != 0) RideStep(i);
            // PCBase::vfunc_53 (pcbase.cpp:3733): UpdateBoosts, which writes the scale only when it changed
            if (i.Boost.Count > 0 && i.Boost.Update(Now) && i.Visual != null)
            {
                if (i.BoostMaterials == null) GatherBoostMaterials(i.Visual, i.BoostMaterials = new List<ShaderMaterial>());
                foreach (var m in i.BoostMaterials) m.SetShaderParameter(ShaderParam.ColorBoost, i.Boost.Scale);
            }
        }
        // the world update after the actors' (worldservice_1.cpp:9717-9731): the field's music again once no boss is in range
        audio?.BossTick();
    }

    /// <summary>
    /// PCBase::vfunc_97 (@0x141671720, pcbase.cpp:2503-2518), which CNpc does not override: the Run_ speed factor from the buff scale
    /// multiplier <paramref name="buff"/> (m_fScaleA8) against the row's model scale <paramref name="model"/> (m_fScaleAC, set from
    /// m_model.m_fScale when the npc is made). 1 when they are equal; 1.5 below half the model scale, 2 - buff/model up to it,
    /// 1 - (buff - model)/(2 model) up to twice it, 0.5 above.
    /// STILL-OPEN: the buff scale is not tracked, so the caller passes 1 (AdditionalCliList::GetScaleMultiplier without a scale buff).
    /// </summary>
    public static float ScaleRate(float buff, float model)
    {
        if (buff == model) return 1f;
        if (model / 2f > buff) return 1.5f;
        if (buff < model) return 2f - buff / model;
        if (model * 2f < buff) return 0.5f;
        return 1f - (buff - model) / (model * 2f);
    }

    /// <summary>
    /// CNpc::TurnTowardTargetYaw (@0x141099170): the yaw turns toward the target at the rate that would close the gap in 0.1 s, capped by the
    /// row's rotationSpeed (an npc without one does not turn), and lands on the target when the step would pass it.
    /// STILL-OPEN: the optimizer's early out (COptimizer FUN_14109a9d0) is taken as off.
    /// </summary>
    static void Turn(Inst i, float dt)
    {
        float cur = Mathf.RadToDeg(i.Visual.Rotation.Y);
        float delta = Mathf.Wrap(i.TargetYaw - cur, -180f, 180f);
        if (delta == 0 || i.TurnCap <= 0) return;
        float step = Mathf.Sign(delta) * Mathf.Min(Mathf.Abs(delta) / 0.1f, i.TurnCap) * dt;
        cur = Mathf.Abs(delta) < Mathf.Abs(step) ? i.TargetYaw : cur + step;
        i.Visual.Rotation = new Godot.Vector3(0, Mathf.DegToRad(Mathf.Wrap(cur, -180f, 180f)), 0);
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

/// <summary>The kind of a hit record on an npc: a cast's (SkillDamage mode 1, CNpc::vfunc_33), a dot's (mode 3, vfunc_34) or a
/// splash's (modes 5 and 6, vfunc_35).</summary>
public enum NpcHitKind { Cast, Dot, Splash }

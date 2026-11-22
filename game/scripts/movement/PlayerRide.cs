using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2Godot.Movement;
using Ms2.Formats.Text;

namespace Ms2Godot;

/// <summary>
/// A mount (the client's Ridee, a PCBase actor of its own): the riding row, the Npc pack model named by its kfm, and the
/// action table (pcactiondata group 0 with the "override Ridee" group 1001 on top).
/// </summary>
public sealed class Mount
{
    public const string NpcArchive = "Resource/Model/Npc";
    public RideData Data;
    public Node3D Visual;
    public AnimationPlayer Anim;
    public Skeleton3D Skeleton;
    public int SeatBone = -1;
    public string Current = "";
    Dictionary<string, string> actions = NoActions;
    static readonly Dictionary<string, string> NoActions = new();
    static PackSet actionsPacks;
    static Dictionary<string, string> actionsCache;

    /// <summary>pcactiondata.xml groups 0 and 1001 (the same for every mount), parsed once per pack set.</summary>
    static Dictionary<string, string> Actions(PackSet packs)
    {
        if (actionsCache != null && ReferenceEquals(actionsPacks, packs)) return actionsCache;
        var a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var x = PackXml.Parse(packs.Read("Xml", "table/pcactiondata.xml"));
        foreach (var gid in new[] { "0", "1001" })
            foreach (var v in x.Root?.Elements("group").FirstOrDefault(g => (string)g.Attribute("groupID") == gid)?.Elements("v") ?? Enumerable.Empty<XElement>())
                a[(string)v.Attribute("type")] = ((string)v.Attribute("names") ?? "").Split(',')[0].Trim();
        actionsPacks = packs;
        return actionsCache = a;
    }
    KfmFile kfm;
    string folder, kfmEntry;
    PackSet packs;
    ModelBuilder mb;
    ModelData md;

    public static Mount Load(PackSet packs, ModelBuilder mb, int rideId)
    {
        var d = RideData.Load(packs, rideId);
        string want = (d.Kfm ?? "").ToLowerInvariant() + ".kfm";
        string kfmEntry = packs.List(NpcArchive).FirstOrDefault(e => e.EndsWith("/" + want, StringComparison.OrdinalIgnoreCase))
                          ?? throw new FileNotFoundException($"riding {rideId}: kfm {d.Kfm} not in the Npc pack");
        if (!mb.TextureArchives.Contains(NpcArchive)) mb.TextureArchives = mb.TextureArchives.Prepend(NpcArchive).ToArray();
        var m = new Mount { Data = d, packs = packs, mb = mb, folder = kfmEntry[..kfmEntry.LastIndexOf('/')], kfmEntry = kfmEntry };
        m.kfm = KfmFile.Load(packs.Read(NpcArchive, kfmEntry));
        string nif = string.IsNullOrEmpty(d.Nif) ? m.kfm.Nif.Replace('\\', '/').TrimStart('.', '/') : d.Nif;
        nif = Path.GetFileName(nif).ToLowerInvariant();
        if (!nif.EndsWith(".nif")) nif += ".nif";
        m.md = mb.LoadModel(NpcArchive, $"{m.folder}/{nif}");
        m.Visual = mb.Build(m.md, "Mount");
        m.Visual.Scale = Vector3.One * d.Scale;
        m.Anim = m.Visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        m.Skeleton = m.Visual.GetNode<Skeleton3D>($"Model/{ModelBuilder.SkeletonName}");
        m.SeatBone = string.IsNullOrEmpty(d.RideBone) ? -1 : m.Skeleton.FindBone(d.RideBone);

        m.actions = Actions(packs);
        return m;
    }

    /// <summary>Plays the sequence of an action type (IDLE, WALK, JUMP_UP, ...) when the mount's kfm has it.</summary>
    public void Play(string type, bool loop, bool restart = false)
    {
        var key = Key(type, loop);
        if (key == null || key == Current && !restart) return;
        Anim.Play(key, 0.15);
        if (restart) Anim.Seek(0, true);
        Current = key;
    }

    /// <summary>Plays a kfm sequence by name (Ridee::vfunc_123's idleXMotion / runXMotion) when the mount's kfm has it.</summary>
    public void PlaySequence(string seq, bool loop)
    {
        var key = SequenceKey(seq, loop);
        if (key == null || key == Current) return;
        Anim.Play(key, 0.15);
        Current = key;
    }

    readonly Dictionary<string, string> typeKeys = new(StringComparer.Ordinal), seqKeys = new(StringComparer.Ordinal);

    /// <summary>The animation key of an action type's sequence, loaded on first use (with <paramref name="loop"/> as its loop
    /// mode); null when the action table or the kfm lacks it. Remembered per type, so a repeat call allocates nothing.</summary>
    public string Key(string type, bool loop)
    {
        if (type == null) return null;
        if (typeKeys.TryGetValue(type, out var k)) return k;
        k = actions.TryGetValue(type, out var seq) ? SequenceKey(seq, loop) : null;
        typeKeys[type] = k;
        return k;
    }

    /// <summary>The animation key of a kfm sequence by name, loaded on first use; null when the kfm or the pack lacks it (or a
    /// bare mount has no model).</summary>
    public string SequenceKey(string seq, bool loop)
    {
        if (string.IsNullOrEmpty(seq) || Anim == null || kfm == null) return null;
        if (seqKeys.TryGetValue(seq, out var k)) return k;
        k = LoadSequence(seq, loop);
        seqKeys[seq] = k;
        if (k != null && KeyCache.TryGetValue(kfmEntry + "/" + k, out var keys)) textKeys[k] = keys;
        return k;
    }

    /// <summary>
    /// The text keys of a loaded sequence (by its animation key), in time order as NiControllerSequence throws them: the
    /// kf's own NiTextKeyExtraData keys and those of the kfm's companion xml (NiTextKeyExtraData::LoadBinary,
    /// NiTextKeyExtraData.cpp:59-75: &lt;kf dir&gt;&lt;studio name&gt;.xml, the &lt;ms2&gt; root's first element named as the kf,
    /// read by FUN_1414a90c0, ms2animationservice.cpp:349-422). Null for none. The ridee's PCBase::TextKeyEvent
    /// (pcbase.cpp:997-1008) plays their step keys.
    /// </summary>
    public List<(double time, string text)> TextKeys(string key) => key != null && textKeys.TryGetValue(key, out var l) ? l : null;
    readonly Dictionary<string, List<(double time, string text)>> textKeys = new(StringComparer.Ordinal);
    /// <summary>The text keys by "kfm entry/key", kept for every mount of the kfm: the keys are the kf's and the kfm's
    /// companion xml's, so two kfms in one folder that name a sequence alike do not share them.</summary>
    static readonly Dictionary<string, List<(double time, string text)>> KeyCache = new(StringComparer.Ordinal);
    static readonly Dictionary<string, Dictionary<string, List<(double, string)>>> XmlKeyCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The companion xml's keys by kf name: under the &lt;ms2&gt; root, the first element of each name, matched
    /// case-sensitively as TinyXML's FirstChildElement does (NiTextKeyExtraData.cpp:59-73); keys without text skipped.</summary>
    Dictionary<string, List<(double, string)>> XmlKeys()
    {
        string x = kfmEntry[..^4] + ".xml";
        if (XmlKeyCache.TryGetValue(x, out var map)) return map;
        map = new Dictionary<string, List<(double, string)>>(StringComparer.Ordinal);
        var root = packs.Exists(NpcArchive, x) ? PackXml.Parse(packs.Read(NpcArchive, x)).Root : null;
        if (root?.Name.LocalName == "ms2")
            foreach (var el in root.Elements())
            {
                if (map.ContainsKey(el.Name.LocalName)) continue;
                List<(double, string)> list = null;
                foreach (var k in el.Elements("key"))
                {
                    string text = (string)k.Attribute("text");
                    if (string.IsNullOrEmpty(text)) continue;
                    double.TryParse((string)k.Attribute("time"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t);
                    (list ??= new()).Add((t, text));
                }
                if (list != null) map[el.Name.LocalName] = list;
            }
        XmlKeyCache[x] = map;
        return map;
    }

    string LoadSequence(string seq, bool loop)
    {
        string key = seq.ToLowerInvariant();
        var lib = Anim.GetAnimationLibrary("");
        if (lib.HasAnimation(key)) return key;
        var s = kfm.ByName(seq);
        if (s == null) return null;
        string kfName = Path.GetFileName(s.KfPath.Replace('\\', '/'));
        string entry = $"{folder}/{kfName.ToLowerInvariant()}";
        if (!packs.Exists(NpcArchive, entry)) return null;
        var kf = KfAnimation.Load(NifFile.Load(mb.Spec, packs.Read(NpcArchive, entry)));
        var keys = new List<(double time, string text)>(kf.TextKeys);
        // the kf's keys, then the xml's, unsorted (NiTextKeyExtraData.cpp:69-72); the xml element is named like the kf file
        if (XmlKeys().TryGetValue(Path.GetFileNameWithoutExtension(kfName), out var extra)) keys.AddRange(extra);
        if (keys.Count > 0) KeyCache[kfmEntry + "/" + key] = keys;
        var a = ModelBuilder.BuildAnimation(kf, md);
        a.LoopMode = loop ? Godot.Animation.LoopModeEnum.Linear : Godot.Animation.LoopModeEnum.None;
        lib.AddAnimation(key, a);
        return key;
    }

    /// <summary>
    /// RideeInterpolator: the rider's transform (Godot space) on the seat bone. In the client's Z-up space the rotation is
    /// the bone's world rotation without its scale times EulerToMatrix(rideRotation), and the position is the bone's
    /// plus col0 * t.x - col1 * t.y + col2 * t.z of that rotation.
    /// </summary>
    public Transform3D Seat()
    {
        ClientSeat(BoneWorld(SeatBone), Data.RideTranslation, Data.RideRotation, out var pos, out var r);
        return new Transform3D(ToGodot * r * ToClient, ToGodot * pos);
    }

    static readonly Basis ToGodot = new(Vector3.Right, -Mathf.Pi / 2), ToClient = ToGodot.Inverse();   // MS2 (Z-up) <-> Godot

    /// <summary>
    /// The seat on a node's Godot world transform <paramref name="w"/> in the client's Z-up axes: the rotation and
    /// position <see cref="Seat"/> converts, also Ridee::FUN_1417083a0 (ridee.cpp:1254) for a passenger seat, with the
    /// passenger's translation and rotation.
    /// </summary>
    public static void ClientSeat(in Transform3D w, float[] t, float[] rr, out Vector3 pos, out Basis r)
    {
        var rs = (ToClient * w.Basis).Orthonormalized();
        var e = new Basis(Vector3.Right, Mathf.DegToRad(rr[0])) * new Basis(new Vector3(0, 1, 0), Mathf.DegToRad(rr[1]))
                * new Basis(new Vector3(0, 0, 1), Mathf.DegToRad(rr[2]));
        r = rs * e;
        pos = ToClient * w.Origin + r.Column0 * t[0] - r.Column1 * t[1] + r.Column2 * t[2];
    }

    /// <summary>The Godot world transform of a skeleton bone, the model's root for -1.</summary>
    public Transform3D BoneWorld(int bone) =>
        bone >= 0 ? Skeleton.GlobalTransform * Skeleton.GetBoneGlobalPose(bone) : Visual.GlobalTransform;

    readonly Dictionary<string, int> bones = new(StringComparer.Ordinal);
    (int Bone, string Name)[] rideBones;

    /// <summary>Ridee::vfunc_119's passenger rideBone nodes (ridee.cpp:638): the seats by passengerid up to the first one
    /// without a rideBone, each with its skeleton bone (those the skeleton lacks left out). Found once per mount, so the
    /// per-frame hold poll does not enumerate the seat map.</summary>
    public (int Bone, string Name)[] RideBones()
    {
        if (rideBones != null) return rideBones;
        var found = new List<(int, string)>();
        if (Data != null && Skeleton != null)
            foreach (var seat in Data.Seats.Values)
            {
                if (string.IsNullOrEmpty(seat.RideBone)) break;
                int b = Bone(seat.RideBone);
                if (b >= 0) found.Add((b, seat.RideBone));
            }
        return rideBones = found.ToArray();
    }

    /// <summary>The skeleton bone of a node name (-1 when absent), remembered per name. Ridee::vfunc_119 (ridee.cpp:638) adds
    /// the passenger seats' rideBone nodes to the rider's node map, where PCBase::FUN_1416757c0 finds them.</summary>
    public int Bone(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        if (!bones.TryGetValue(name, out int b)) bones[name] = b = Skeleton.FindBone(name);
        return b;
    }
}

/// <summary>
/// Riding: the rider sits in StateRide (0x21) on the mount's seat while the mount runs the movement states with the
/// riding row's speeds (walkSpeed, runSpeed, swimSpeed, row * 100 in units/s). The jump height stays the rider's.
/// The mount (Ridee) is a PCBase with its own controller of the PC's size: Ridee::FUN_1417043a0 (@0x1417043a0) sets the
/// model size to (25, 105) and PCBase::vfunc_10 creates the capsule from it, with collision group 2 and groups 0x399
/// (Ridee::vfunc_10 @0x141704750), the same as PC::vfunc_10's. So the body keeps the player's capsule and controller.
/// The row's &lt;collision&gt; box is not the mount's movement shape: with physic set it is a kinematic box other actors meet.
/// </summary>
public partial class Player
{
    public Mount Mount { get; private set; }
    /// <summary>The mount's GetCurStateId and its sub (FUN_14166cc80): the one body runs the mount's states, so the own
    /// state ids, as a passenger's StateHold reads them.</summary>
    public (int State, int Sub) RideeStateIds => (StateId(State), SubOf(State));
    AnimatableBody3D rideBox;

    // MyPC's own controller while the mount's runs: the game tick makes the mount's records instead (scenestategame.cpp:1362-1368,
    // Ridee::FUN_141706650 or MyPC::FUN_141524a80), so the rider's unsent records wait here and go out after the ride off
    SyncRecord footCurrent;
    readonly System.Collections.Generic.List<SyncRecord> footQueued = new();
    bool footFallMarked;

    /// <summary>A new Ridee starts with an empty StateRideeController (ridee.cpp:1296) and m_nDE0 = 0, m_bDF8 = false (ridee.cpp:361, 384).</summary>
    void MountSyncBegin()
    {
        footCurrent = current; footQueued.Clear(); footQueued.AddRange(queued); footFallMarked = fallMarked;
        current = null; queued.Clear(); fallMarked = false;
    }

    /// <summary>The mount goes with its controller: its unsent records are dropped (~StateRideeController only runs the base
    /// dtor, ridee.h:25), and the rider's own controller is back.</summary>
    void MountSyncEnd()
    {
        foreach (var q in queued)
        {
            q.InQueue = false;
            if (q != current) freeRecords.Push(q);
        }
        if (current != null) freeRecords.Push(current);
        current = footCurrent; queued.Clear(); queued.AddRange(footQueued); fallMarked = footFallMarked;
        footCurrent = null; footQueued.Clear();
    }

    /// <summary>RideOnAction: get on the mount (its visual takes the facing; the rider goes onto the seat).</summary>
    public void RideOn(Mount m)
    {
        if (Mount != null) RideOff();
        if (State is MoveState.Ladder or MoveState.Rope or MoveState.Climb or MoveState.Dash) ResetMotion();
        Mount = m;
        MountSyncBegin();
        HeldStateEntered?.Invoke(Ms2.Ui.HeldItemState.Ride);   // StateRide::vfunc_4 (statemanager.cpp:1095): PC::SendRequestCube
        AddChild(m.Visual);
        m.Visual.Rotation = new Vector3(0, Mathf.DegToRad(yawDeg), 0);
        // Ridee::vfunc_49 (ridee.cpp:848-858): the mount's own CObjectShadow, of the myPC row for the own pc's ridee, the
        // row's shadow scale, lifted 1; it goes with the mount's visual. StateRide::vfunc_4 turns the rider's off
        // (statemanager.cpp:1095).
        FieldShadows.Character(m.Visual, m.Visual, Ms2.Formats.Options.GraphicOptionTable.MyPC, m.Data.ShadowScale, 1f, this);
        FieldShadows.SetShadowVisible(this, false);
        // Ridee::vfunc_10: the mount is placed at the rider's position raised by 25 (a rider on a breakable's marker goes to
        // the breakable's top + 10 first), and takes the rider's hit-report entity, which the one body already has
        GlobalPosition += Vector3.Up * 25f;
        if (m.Data.Physic) MakeRideBox(m.Data);
        ResetRunX();
        baseSpeed = RideSpeed();
        Enter(MoveState.Idle);
        if (m.Data.HideRider) SetHideReason(HideReasonRide, true);   // PCBase::vfunc_120 (pcbase.cpp:3331-3333)
    }

    public void RideOff()
    {
        if (Mount == null) return;
        // PCBase::vfunc_121 reads m_spB10->FUN_141671a30() first (pcbase.cpp:3395): m_spB10 is the mount, a Ridee with
        // no mount of its own, so it is its controller's IsOnGround (pcbase.cpp:2532-2540): the last move touched down,
        // and no jump or lift (gameobjectcontrollerphysxhelpers.cpp:487-490). Our mount moves the rider's one body.
        bool mountOnGround = Grounded && !jumping;
        if (Mount.Data.HideRider) SetHideReason(HideReasonRide, false);   // PCBase::vfunc_121 (pcbase.cpp:3402-3404)
        FieldShadows.SetShadowVisible(this, true);                       // StateRide::vfunc_5 (statemanager.cpp:1103)
        RemoveChild(Mount.Visual);
        Mount.Visual.QueueFree();
        Mount = null;
        ResetRunX();
        MountSyncEnd();
        ReleaseRideBox();
        Visual.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), Vector3.Zero);
        baseSpeed = MoveConfig.RunSpeed;
        // PCBase::vfunc_121 clears the jump, the controller velocity and the fall timer before the state change
        // (pcbase.cpp:3408)
        StopJump();
        if (mountOnGround) Enter(MoveState.Idle);
        else
        {
            // ClearCollisionFlags, then StateFall with StateFallData(the mount's m_posC10) (pcbase.cpp:3409-3415): the
            // fall keeps the start the mount already had (StateFall::vfunc_4 -> FUN_141676060, statefall.cpp:98-106,
            // pcbase.cpp:3644-3650), which is our one fallStartY
            nxHit = 0;
            Enter(MoveState.Fall);
        }
    }

    /// <summary>
    /// Ridee::MakeNxBoxController (@0x141707e60), only for &lt;collision physic="true"&gt;: a kinematic actor with one box shape
    /// of half extents (width, depth, height) * 0.01 * 0.5 m, i.e. half the row's size in game units, in shape group 0.
    /// </summary>
    void MakeRideBox(RideData d)
    {
        rideBox = new AnimatableBody3D { Name = "RideBox", TopLevel = true, SyncToPhysics = false };
        // NxBoxShapeDesc dimensions (x width, y depth, z height), Z-up: width along x, height up, depth along the facing
        rideBox.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(d.Width, d.Height, d.Depth) } });
        AddChild(rideBox);
        PoseRideBox();
        nx.IgnoreBody = rideBox.GetRid();
    }

    /// <summary>Ridee::FUN_1417082c0 (@0x1417082c0): the box actor is released.</summary>
    void ReleaseRideBox()
    {
        if (rideBox == null) return;
        nx.IgnoreBody = default;
        RemoveChild(rideBox);
        rideBox.QueueFree();
        rideBox = null;
    }

    /// <summary>Ridee::vfunc_53 (@0x1417079f0) each frame: the box at the mount's position, its centre half its height plus
    /// 0.2 m up, turned by the mount's Z rotation.</summary>
    void PoseRideBox()
    {
        if (rideBox == null || Mount == null) return;
        rideBox.GlobalTransform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)),
            GlobalPosition + Vector3.Up * (Mount.Data.Height / 2 + 20f));
    }

    /// <summary>StateWalk / StateSwim::vfunc_3's speed (statewalk.cpp:164-199, stateswim.cpp:141-166):
    /// <see cref="RunXRideSpeed"/> for the RunX subs 0x17 / 0x85; on water (sub 0x84) the row's run speed for a mount
    /// that runs on water (FUN_1417fb370, <see cref="WaterRunner"/>), else its swim speed; on land the row's walk speed
    /// in walk mode (sub 0x19) and <see cref="RunRideSpeed"/> otherwise (sub 0x18).</summary>
    float RideSpeed() => Mount == null ? MoveConfig.RunSpeed
        : (OnWater ? SwimRunX : walkRunX) ? RunXRideSpeed() * 100f
        : OnWater ? (WaterRunner ? Mount.Data.RunSpeed : Mount.Data.SwimSpeed) * 100f
        : WalkMode ? Mount.Data.WalkSpeed * 100f
        : RunRideSpeed() * 100f;

    /// <summary>Ridee::GetRunSpeed @0x1417071d0 (ridee.cpp:1028-1037): the row's run speed times 1 + the rider's
    /// GetAbilityRate(0xae), with FUN_1411f0e30's rounding.</summary>
    float RunRideSpeed() => ScaleRideSpeed(Mount.Data.RunSpeed, RideAbilityRate?.Invoke(RunXSameSpeedAbility) ?? 0f);

    static float ScaleRideSpeed(float speed, float rate) => (float)((double)speed * (double)(1.0f + rate) + 0.000001);

    /// <summary>Ridee::GetRideSpeed @0x1417070a0 (ridee.cpp:1014-1025): the row's runXSpeed times 1 + the rider's
    /// CEquipment::GetAbilityRate, of special ability 0xae when runSpeed is nearly runXSpeed (IsNearlyEqual, 1e-6), else
    /// of 0xa5 (FUN_1411f0e30: in double, plus 1e-6). The own rider is always a PC; without
    /// <see cref="RideAbilityRate"/> the rate is 0.</summary>
    float RunXRideSpeed()
    {
        var d = Mount.Data;
        int type = MathF.Abs(d.RunSpeed - d.RunXSpeed) < 1e-6f ? RunXSameSpeedAbility : RunXSpeedAbility;
        return ScaleRideSpeed(d.RunXSpeed, RideAbilityRate?.Invoke(type) ?? 0f);
    }

    /// <summary>The special abilities GetRideSpeed reads: 0xae when the row's runSpeed equals runXSpeed (and GetRunSpeed's),
    /// else 0xa5.</summary>
    public const int RunXSameSpeedAbility = 0xae, RunXSpeedAbility = 0xa5;

    /// <summary>The own CEquipment::GetAbilityRate of a special ability type (the total over 1000).</summary>
    public Func<int, float> RideAbilityRate;

    /// <summary>The ridee's m_unk_b80 (FUN_141676b70 / FUN_141676b50): the RunX sprint flag Ridee::UpdateRunEffects keeps.</summary>
    public bool RunX { get; private set; }

    /// <summary>MyPC's IsActionKeyDown(0x49401), the attack key (Unk_PCMove); null reads the offline key X.</summary>
    public Func<bool> AttackKeyDown;
    /// <summary>The rider's current stat 10 (EP, FUN_141670a00(10)); null reads 0.</summary>
    public Func<long> CurrentEp;
    /// <summary>The field's useEPSkill (CFieldData m_nA4); null reads the default 1.</summary>
    public Func<bool> FieldUsesEp;
    /// <summary>CEffectService::InsertEffect of a ride effect at the mount; returns how long it plays (0 when not played).</summary>
    public Func<string, float> PlayRideEffect;
    /// <summary>The RunX sprint without the EP: notice 0x48 (FUN_141275260(0x48, 1, 0)) and the sound
    /// "system_notice_error_ep" (s_strNoticeEp, ridee.cpp:347), at most every 5 s.</summary>
    public event Action RunXNoEp;

    bool walkRunX, landRunX;
    double runXEpAt = double.NegativeInfinity, runXNoticeAt = double.NegativeInfinity;
    double runFxUntil = double.NegativeInfinity, runFx2Until = double.NegativeInfinity;

    /// <summary>A new Ridee: m_unk_b80 0, no RunX effect groups, m_nD40 / m_nD44 0.</summary>
    void ResetRunX()
    {
        RunX = walkRunX = landRunX = false;
        runXEpAt = runXNoticeAt = runFxUntil = runFx2Until = double.NegativeInfinity;
    }

    /// <summary>StateSwim's RunX sub 0x85: the flag and <see cref="WaterRunner"/> (FUN_1417fb370).</summary>
    bool SwimRunX => RunX && WaterRunner;

    /// <summary>Ridee::FUN_1417075e0 @0x1417075e0 (ridee.cpp:1091-1103): a riding type 1 or 2, or the rider's enabled buff
    /// of eventBuffType 3 (<see cref="RiderEventBuff"/>).</summary>
    bool WaterRunner => Mount != null && (Mount.Data.Type is 1 or 2 || RiderEventBuff?.Invoke(SwimEventBuffType) == true);

    /// <summary>The eventBuffType FUN_1417075e0 asks the rider's buffs for.</summary>
    const int SwimEventBuffType = 3;

    /// <summary>The own AdditionalCliList::HasEnabledWithEventBuffType; null reads false.</summary>
    public Func<int, bool> RiderEventBuff;

    /// <summary>
    /// Ridee::UpdateRunEffects (@0x141705b60, ridee.cpp:717-799), each frame before the state's vfunc_8 (Ridee::FUN_141705ac0).
    /// Without the attack key the flag clears. Standing (<paramref name="moving"/> false, FUN_1416749c0) it sets, with
    /// pressXEffectIdle once as it does and pressXEffectIdleNonstop kept playing (respawned once the last one ends).
    /// Moving on a field with useEPSkill it needs the EP (<see cref="RunXEpOk"/>), else clears with the notice; moving with
    /// the EP, or on a free row (runXConsumeEp 0), it sets with pressXEffectRun and pressXEffectRunNonstop the same way;
    /// moving on a field without useEPSkill with a row that costs EP leaves the flag as it was.
    /// </summary>
    void UpdateRunEffects(bool moving)
    {
        if (!(AttackKeyDown?.Invoke() ?? (!Ms2.Ui.InputBlock.Active && Input.IsPhysicalKeyPressed(Key.X)))) { RunX = false; return; }
        var d = Mount.Data;
        if (!moving)
        {
            if (!RunX) PlayRunEffect(d.PressXEffectIdle);
            RunX = true;
            KeepRunEffect(d.PressXEffectIdleNonstop, ref runFxUntil);
            return;
        }
        bool needEp = FieldUsesEp?.Invoke() ?? true, free = d.RunXConsumeEp == 0;
        if (needEp && !RunXEpOk())
        {
            RunX = false;
            if (gameTime - runXNoticeAt > 5.0) { runXNoticeAt = gameTime; RunXNoEp?.Invoke(); }
        }
        else if (needEp || free)
        {
            if (!RunX) PlayRunEffect(d.PressXEffectRun);
            RunX = true;
            KeepRunEffect(d.PressXEffectRunNonstop, ref runFx2Until);
        }
    }

    /// <summary>Ridee::FUN_1417060d0 (ridee.cpp:802): a free row, or within 1 s of the last check that found the EP, or the
    /// rider's EP at least runXConsumeEp (which restarts that second).</summary>
    bool RunXEpOk()
    {
        int cost = Mount.Data.RunXConsumeEp;
        if (cost == 0 || gameTime - runXEpAt <= 1.0) return true;
        if ((CurrentEp?.Invoke() ?? 0) < cost) return false;
        runXEpAt = gameTime;
        return true;
    }

    void PlayRunEffect(string xml)
    {
        if (!string.IsNullOrEmpty(xml)) PlayRideEffect?.Invoke(xml);
    }

    /// <summary>m_nRunFx / m_nRunFx2: a new effect group once the last one has no live effect.</summary>
    void KeepRunEffect(string xml, ref double until)
    {
        if (gameTime < until || string.IsNullOrEmpty(xml) || PlayRideEffect == null) return;
        float len = PlayRideEffect(xml);
        until = gameTime + (len > 0 ? len : MinRunFxSeconds);
    }

    const float MinRunFxSeconds = 0.6f;

    /// <summary>The mount's sub ids with the RunX flag (FUN_141676b70): StateIdle 0xc while it is set (back to 1 once it
    /// clears), StateWalk 0x17 from the frame it is set to the walk's end, StateJump 0x27 the same, StateLand 0x87 when set
    /// at the landing, StateSwim 0x85 (<see cref="SwimRunX"/>).</summary>
    void UpdateRunXSubs()
    {
        if (!RunX) return;
        if (State == MoveState.Walk) walkRunX = true;
        else if (State == MoveState.Jump) jumpSub = 0x27;
    }

    /// <summary>The mount's sequence by its state and sub (Ridee::vfunc_123: 0x1e idleXMotion, 0x1f runXMotion, the rest
    /// pcactiondata's), and the rider's, StateRide::vfunc_9 by the ridee's state, sub and RunX flag (<see cref="StateRideAnim.For"/>).</summary>
    void RideAnimation()
    {
        var m = Mount;
        switch (State)
        {
            case MoveState.Walk:
                if (walkRunX) m.PlaySequence(m.Data.RunXMotion, true); else m.Play("WALK", true);
                break;
            case MoveState.Swim:
                if (SwimRunX) m.PlaySequence(m.Data.RunXMotion, true); else m.Play("WALK", true);
                break;
            case MoveState.Jump:
                bool up = jumpTime < jumpApex;
                if (jumpSub == 0x27) m.Play(up ? "STUNT_JUMP_UP" : "STUNT_JUMP_DOWN", false);
                else m.Play(up ? "JUMP_UP" : "JUMP_DOWN", false);
                break;
            case MoveState.Fall:
                m.Play("JUMP_DOWN", false);
                break;
            case MoveState.Land:
                if (stateTime < 0.05f) m.Play(landRunX ? "STUNT_JUMP_LAND" : "JUMP_LAND", false, restart: true);
                break;
            case MoveState.Idle:
                if (RunX) m.PlaySequence(m.Data.IdleXMotion, true); else m.Play("IDLE", true);
                break;
        }
        var key = Anims?.Sequence(Ms2.Net.Replay.StateRideAnim.For(StateId(State), SubOf(State), m.Data.Rider, RunX), true);
        if (key != null && key != CurrentAnim) PlaySequence(key, 1f, false);
    }

    /// <summary>RideeInterpolator each frame, after the mount's pose: the rider on the seat.</summary>
    /// <summary>The rider sits on the mount's seat after the frame's step (the end of Player._Process).</summary>
    void SeatOnMount()
    {
        if (Mount == null) return;
        Visual.GlobalTransform = Mount.Seat();
    }
}

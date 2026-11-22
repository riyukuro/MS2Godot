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
    string folder;
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
        var m = new Mount { Data = d, packs = packs, mb = mb, folder = kfmEntry[..kfmEntry.LastIndexOf('/')] };
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

    /// <summary>The animation key of a kfm sequence by name, loaded on first use; null when the kfm or the pack lacks it.</summary>
    public string SequenceKey(string seq, bool loop)
    {
        if (string.IsNullOrEmpty(seq)) return null;
        if (seqKeys.TryGetValue(seq, out var k)) return k;
        k = LoadSequence(seq, loop);
        seqKeys[seq] = k;
        return k;
    }

    string LoadSequence(string seq, bool loop)
    {
        string key = seq.ToLowerInvariant();
        var lib = Anim.GetAnimationLibrary("");
        if (lib.HasAnimation(key)) return key;
        var s = kfm.ByName(seq);
        if (s == null) return null;
        string entry = $"{folder}/{Path.GetFileName(s.KfPath.Replace('\\', '/')).ToLowerInvariant()}";
        if (!packs.Exists(NpcArchive, entry)) return null;
        var a = ModelBuilder.BuildAnimation(KfAnimation.Load(NifFile.Load(mb.Spec, packs.Read(NpcArchive, entry))), md);
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
        // Ridee::vfunc_10: the mount is placed at the rider's position raised by 25 (a rider on a breakable's marker goes to
        // the breakable's top + 10 first), and takes the rider's hit-report entity, which the one body already has
        GlobalPosition += Vector3.Up * 25f;
        if (m.Data.Physic) MakeRideBox(m.Data);
        baseSpeed = RideSpeed();
        Enter(MoveState.Idle);
    }

    public void RideOff()
    {
        if (Mount == null) return;
        // PCBase::vfunc_121 reads m_spB10->FUN_141671a30() first (pcbase.cpp:3395): m_spB10 is the mount, a Ridee with
        // no mount of its own, so it is its controller's IsOnGround (pcbase.cpp:2532-2540): the last move touched down,
        // and no jump or lift (gameobjectcontrollerphysxhelpers.cpp:487-490). Our mount moves the rider's one body.
        bool mountOnGround = Grounded && !jumping;
        RemoveChild(Mount.Visual);
        Mount.Visual.QueueFree();
        Mount = null;
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

    float RideSpeed() => Mount == null ? MoveConfig.RunSpeed
        : OnWater ? Mount.Data.SwimSpeed * 100f
        : (WalkMode ? Mount.Data.WalkSpeed : Mount.Data.RunSpeed) * 100f;

    /// <summary>The mount's sequence by its state, and the rider's (StateRide::vfunc_9 by the ridee's state).</summary>
    void RideAnimation()
    {
        var m = Mount;
        string pc;
        switch (State)
        {
            case MoveState.Walk:
                m.Play("WALK", true);
                pc = m.Data.AniPCRun;
                break;
            case MoveState.Swim:
                m.Play("WALK", true);
                pc = m.Data.AniPCRun;
                break;
            case MoveState.Jump:
                if (jumpTime < jumpApex) m.Play("JUMP_UP", false); else m.Play("JUMP_DOWN", false);
                pc = m.Data.AniPCJump;
                break;
            case MoveState.Fall:
                m.Play("JUMP_DOWN", false);
                pc = m.Data.AniPCJump;
                break;
            case MoveState.Land:
                if (stateTime < 0.05f) m.Play("JUMP_LAND", false, restart: true);
                pc = m.Data.AniPCIdle;
                break;
            case MoveState.Idle:
                m.Play("IDLE", true);
                pc = m.Data.AniPCIdle;
                break;
            default:
                pc = m.Data.AniPC;
                break;
        }
        var key = Anims?.Sequence(pc, true);
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

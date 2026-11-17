using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2Godot.Movement;

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
    readonly Dictionary<string, string> actions = new(StringComparer.OrdinalIgnoreCase);
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

        var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/pcactiondata.xml")));
        foreach (var gid in new[] { "0", "1001" })
            foreach (var v in x.Root?.Elements("group").FirstOrDefault(g => (string)g.Attribute("groupID") == gid)?.Elements("v") ?? Enumerable.Empty<XElement>())
                m.actions[(string)v.Attribute("type")] = ((string)v.Attribute("names") ?? "").Split(',')[0].Trim();
        return m;
    }

    /// <summary>Plays the sequence of an action type (IDLE, WALK, JUMP_UP, ...) when the mount's kfm has it.</summary>
    public void Play(string type, bool loop, bool restart = false)
    {
        if (!actions.TryGetValue(type, out var seq)) return;
        string key = seq.ToLowerInvariant();
        var lib = Anim.GetAnimationLibrary("");
        if (!lib.HasAnimation(key))
        {
            var s = kfm.ByName(seq);
            if (s == null) return;
            string entry = $"{folder}/{Path.GetFileName(s.KfPath.Replace('\\', '/')).ToLowerInvariant()}";
            if (!packs.Exists(NpcArchive, entry)) return;
            var a = ModelBuilder.BuildAnimation(KfAnimation.Load(NifFile.Load(mb.Spec, packs.Read(NpcArchive, entry))), md);
            a.LoopMode = loop ? Godot.Animation.LoopModeEnum.Linear : Godot.Animation.LoopModeEnum.None;
            lib.AddAnimation(key, a);
        }
        if (key == Current && !restart) return;
        Anim.Play(key, 0.15);
        if (restart) Anim.Seek(0, true);
        Current = key;
    }

    /// <summary>
    /// RideeInterpolator: the rider's transform (Godot space) on the seat bone. In the client's Z-up space the rotation is
    /// the bone's world rotation without its scale times EulerToMatrix(rideRotation), and the position is the bone's
    /// plus col0 * t.x - col1 * t.y + col2 * t.z of that rotation.
    /// </summary>
    public Transform3D Seat()
    {
        var bone = SeatBone >= 0 ? Skeleton.GlobalTransform * Skeleton.GetBoneGlobalPose(SeatBone) : Visual.GlobalTransform;
        var c = new Basis(Vector3.Right, -Mathf.Pi / 2);          // MS2 (Z-up) -> Godot
        var ci = c.Inverse();
        var rs = (ci * bone.Basis).Orthonormalized();
        var rr = Data.RideRotation;
        var e = new Basis(Vector3.Right, Mathf.DegToRad(rr[0])) * new Basis(new Vector3(0, 1, 0), Mathf.DegToRad(rr[1]))
                * new Basis(new Vector3(0, 0, 1), Mathf.DegToRad(rr[2]));
        var r = rs * e;
        var t = Data.RideTranslation;
        var pos = ci * bone.Origin + r.Column0 * t[0] - r.Column1 * t[1] + r.Column2 * t[2];
        return new Transform3D(c * r * ci, c * pos);
    }
}

/// <summary>
/// Riding: the rider sits in StateRide (0x21) on the mount's seat while the mount runs the movement states with the
/// riding row's speeds (walkSpeed, runSpeed, swimSpeed in m/s). The jump height stays the rider's.
/// The mount (Ridee) is a PCBase with its own controller of the PC's size: Ridee::FUN_1417043a0 (@0x1417043a0) sets the
/// model size to (25, 105) and PCBase::vfunc_10 creates the capsule from it, with collision group 2 and groups 0x399
/// (Ridee::vfunc_10 @0x141704750), the same as PC::vfunc_10's. So the body keeps the player's capsule and controller.
/// The row's &lt;collision&gt; box is not the mount's movement shape: with physic set it is a kinematic box other actors meet.
/// </summary>
public partial class Player
{
    public Mount Mount { get; private set; }
    AnimatableBody3D rideBox;

    /// <summary>RideOnAction: get on the mount (its visual takes the facing; the rider goes onto the seat).</summary>
    public void RideOn(Mount m)
    {
        if (Mount != null) RideOff();
        if (State is MoveState.Ladder or MoveState.Rope or MoveState.Climb or MoveState.Dash) ResetMotion();
        Mount = m;
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
        RemoveChild(Mount.Visual);
        Mount.Visual.QueueFree();
        Mount = null;
        ReleaseRideBox();
        Visual.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), Vector3.Zero);
        baseSpeed = MoveConfig.RunSpeed;
        Enter(MoveState.Idle);
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
        if (key != null && key != CurrentAnim)
        {
            Anim.Play(key, 0.15);
            CurrentAnim = key;
        }
    }

    /// <summary>RideeInterpolator each frame, after the mount's pose: the rider on the seat.</summary>
    public override void _Process(double delta)
    {
        if (Mount == null) return;
        Visual.GlobalTransform = Mount.Seat();
    }
}

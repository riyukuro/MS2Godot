using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Net;

namespace Ms2.Ui.Skills;

/// <summary>
/// The loaded path nif of a couple emotion animation (CoupleEmotionAnimInfo's m_spNifRoot, m_spOwnerNode, m_spPeerNode,
/// m_spCenterNode; Load @0x140b7ff20, statecoupleemotion.cpp:253-319): the nodes' transforms at a time since the load
/// (UpdateNif @0x140b80630, every controller run on application time), and the two uses the state makes of them:
/// <see cref="OwnerPose"/> (SyncWithPartner) and <see cref="FindPlacement"/>.
/// The couple path nifs of KMS2 and GMS2 animate their nodes with NiTransformInterpolator only; the NiPathInterpolator Load
/// looks for (m_spTarget) and the duration it sums (m_fDuration) are kept by the client and never read in the state.
/// </summary>
public sealed class CouplePath
{
    readonly EffectScene scene;
    readonly int owner, peer, center, parent;
    readonly Xform[] world;
    readonly bool[] done;
    /// <summary>The per-frame matrices, made at the load: each node's world rotation (its <see cref="world"/> entry's R),
    /// a local rotation, the end angles' parent rotation and the pose's rotation.</summary>
    readonly double[][] worldR;
    readonly double[] localR = new double[9], endR = new double[9], poseR = new double[9];
    double time;

    CouplePath(EffectScene scene, int owner, int peer, int center)
    {
        this.scene = scene;
        this.owner = owner;
        this.peer = peer;
        this.center = center;
        parent = scene.Nodes[owner].Parent;
        world = new Xform[scene.Nodes.Count];
        done = new bool[scene.Nodes.Count];
        worldR = new double[scene.Nodes.Count][];
        for (int i = 0; i < worldR.Length; i++) worldR[i] = new double[9];
        Update(0);
    }

    /// <summary>
    /// Load's node lookups: GetObjectByName from the root (the node itself, then its children in order, depth first)
    /// cast to NiNode; no owner or peer node fails, no Eff_center node is the owner node. FindPlacement and SyncWithPartner
    /// need the owner's parent (their asserts 0x292 / 0x18a), so an owner without one fails here.
    /// </summary>
    public static CouplePath Load(NifFile nif, string ownerNode, string peerNode)
    {
        if (nif == null || nif.Blocks.Count == 0) return null;
        var scene = EffectScene.Load(nif);
        int o = Find(nif, scene, ownerNode), p = Find(nif, scene, peerNode);
        if (o < 0 || p < 0 || scene.Nodes[o].Parent < 0) return null;
        int c = Find(nif, scene, CoupleEmotionTable.CenterNode);
        if (c < 0) c = o;
        return new CouplePath(scene, o, p, c);
    }

    static bool IsNiNode(string type) => type is "NiNode" or "NiSortAdjustNode" or "NiBillboardNode";

    static int Find(NifFile nif, EffectScene scene, string name)
    {
        if (name == null) return -1;
        var kids = new Dictionary<int, List<int>>();
        int root = -1;
        for (int i = 0; i < scene.Nodes.Count; i++)
        {
            int pa = scene.Nodes[i].Parent;
            if (pa < 0) { if (root < 0) root = i; continue; }
            if (!kids.TryGetValue(pa, out var list)) kids[pa] = list = new();
            list.Add(i);
        }
        if (root < 0) return -1;
        // the child lists follow the parent's Children order
        foreach (var (pa, list) in kids)
        {
            var order = nif.Blocks[scene.Nodes[pa].Block].Ints("Children");
            list.Sort((a, b) => Array.IndexOf(order, (long)scene.Nodes[a].Block).CompareTo(Array.IndexOf(order, (long)scene.Nodes[b].Block)));
        }
        int Walk(int i)
        {
            if (scene.Nodes[i].Name == name) return i;
            if (kids.TryGetValue(i, out var list))
                foreach (var k in list) { int r = Walk(k); if (r != -2) return r; }
            return -2;
        }
        int hit = Walk(root);
        // NiObject::DynamicCast(NiNode): a match that is not a node is no node; the search does not go on
        return hit >= 0 && IsNiNode(nif.Blocks[scene.Nodes[hit].Block].Type) ? hit : -1;
    }

    /// <summary>UpdateNif: the root updated at the time since the load (controllers on application time). Each node's world
    /// rotation is written into its own buffer, so an update allocates nothing.</summary>
    public void Update(double t)
    {
        time = t;
        Array.Fill(done, false);
        for (int i = 0; i < scene.Nodes.Count; i++) WorldOf(i);
    }

    void WorldOf(int i)
    {
        if (done[i]) return;
        int pa = scene.Nodes[i].Parent;
        if (pa >= 0) WorldOf(pa);
        var local = LocalAt(scene.Nodes[i], time, localR);
        var r = worldR[i];
        if (pa >= 0)
        {
            var pw = world[pa];
            Mul(pw.R, local.R, r);
            world[i] = new Xform(pw.Apply(local.T), r, pw.S * local.S);
        }
        else
        {
            Array.Copy(local.R, r, 9);
            world[i] = new Xform(local.T, r, local.S);
        }
        done[i] = true;
    }

    /// <summary>The node's local transform with the interpolator's valid parts in place of the rest ones; an animated
    /// rotation goes into <paramref name="r"/> (the rest one is the node's own).</summary>
    static Xform LocalAt(EffectNode n, double t, double[] r)
    {
        if (n.Anim == null) return n.Local;
        double lt = n.AnimCtl != null ? n.AnimCtl.Local(t) : t;
        var T = n.Anim.T != null ? n.Anim.T(lt) : n.Local.T;
        var R = n.Anim.R != null ? ToMatrix(n.Anim.R(lt), r) : n.Local.R;
        double S = n.Anim.S != null ? n.Anim.S(lt) : n.Local.S;
        return new Xform(T, R, S);
    }

    /// <summary><see cref="Quat.ToMatrix"/> into <paramref name="m"/>.</summary>
    static double[] ToMatrix(Quat q, double[] m)
    {
        q = q.Normalized();
        double w = q.W, x = q.X, y = q.Y, z = q.Z;
        m[0] = 1 - 2 * (y * y + z * z); m[1] = 2 * (x * y - w * z); m[2] = 2 * (x * z + w * y);
        m[3] = 2 * (x * y + w * z); m[4] = 1 - 2 * (x * x + z * z); m[5] = 2 * (y * z - w * x);
        m[6] = 2 * (x * z - w * y); m[7] = 2 * (y * z + w * x); m[8] = 1 - 2 * (x * x + y * y);
        return m;
    }

    /// <summary>The 3x3 product a * b (Xform.Compose's) into <paramref name="o"/>, which is neither a nor b.</summary>
    static void Mul(double[] a, double[] b, double[] o)
    {
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                o[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
    }

    /// <summary>The world transforms of the last <see cref="Update"/>; their rotations are the path's buffers, which the next
    /// update overwrites.</summary>
    public Xform OwnerWorld => world[owner];
    public Xform PeerWorld => world[peer];
    public Xform ParentWorld => world[parent];
    /// <summary>A node's local transform now, its rotation a new matrix.</summary>
    public Xform OwnerLocal => LocalOf(owner);
    public Xform CenterLocal => LocalOf(center);
    Xform LocalOf(int i) => LocalAt(scene.Nodes[i], time, new double[9]);

    /// <summary>EulerToMatrix @0x140b86230: Rx(x) Ry(y) Rz(z) of degrees with Gamebryo's turn sense undone.</summary>
    public static double[] EulerToMatrix(Vector3 euler)
    {
        var m = new double[9];
        CameraMath.FromEuler(euler.X, euler.Y, euler.Z, m);
        return m;
    }

    /// <summary>NiMatrix3::ToEulerAnglesXYZ times -57.2957764; false (no angles) at its two pole cases.</summary>
    public static bool ToAngles(double[] r, out Vector3 angles)
    {
        var (x, y, z) = CameraMath.ToEuler(r);
        angles = new Vector3(x, y, z);
        return Math.Abs(Math.Asin(Math.Clamp(r[2], -1, 1))) < Math.PI / 2 - 1e-3;
    }

    static Vector3 V(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>
    /// SyncWithPartner's pose (@0x140b82c40, statecoupleemotion.cpp:989-998): the parent's world transform with its
    /// rotation replaced by EulerToMatrix(posEnd), times the owner's local transform; the feet at posStart plus its
    /// translation, the body turned to its angles (none at a pole). The matrices are the path's buffers: no allocation.
    /// </summary>
    public (Vector3 Pos, Vector3? Angles) OwnerPose(Vector3 posStart, Vector3 posEnd)
    {
        var pw = world[parent];
        CameraMath.FromEuler(posEnd.X, posEnd.Y, posEnd.Z, endR);
        var parentXf = new Xform(pw.T, endR, pw.S);
        var local = LocalAt(scene.Nodes[owner], time, localR);
        Mul(endR, local.R, poseR);
        var pos = posStart + V(parentXf.Apply(local.T));
        return (pos, ToAngles(poseR, out var a) ? a : null);
    }

    /// <summary>
    /// FindPlacement @0x140b82230 (statecoupleemotion.cpp:818-897): from the owner's angles, turning z one degree a step
    /// for 360 steps, the owner, parent and peer world transforms moved so the owner is at the origin, the owner turned to
    /// the angles and put before the other two; the peer's and the owner's points (posStart plus their translations) must
    /// both be placeable (FUN_140b84dc0, which raises the point by 5 in place) and the ray from the owner's point to the
    /// peer's must hit nothing. The first such step gives posA (the peer's raised point), posB (posStart plus the parent's
    /// translation) and the yaw (the parent's angle z).
    /// STILL-OPEN: when the parent's rotation is at a ToEulerAnglesXYZ pole the client keeps an unset angle; 0 here.
    /// </summary>
    public bool FindPlacement(Vector3 posStart, Vector3 ownerRot, Func<Vector3, bool> placeable, Func<Vector3, Vector3, float, bool> rayHits,
        out Vector3 posA, out Vector3 posB, out float yaw)
    {
        posA = posB = default;
        yaw = 0;
        var rot = ownerRot;
        for (int step = 0; step < 360; step++, rot.Z += 1f)
        {
            var ow = world[owner];
            var ot = ow.T;
            var pw = world[parent];
            var qw = world[peer];
            pw = pw with { T = new Vec3(pw.T.X - ot.X, pw.T.Y - ot.Y, pw.T.Z - ot.Z) };
            qw = qw with { T = new Vec3(qw.T.X - ot.X, qw.T.Y - ot.Y, qw.T.Z - ot.Z) };
            var ox = new Xform(new Vec3(0, 0, 0), EulerToMatrix(rot), ow.S);
            pw = ox.Compose(pw);
            qw = ox.Compose(qw);
            var ptPeer = posStart + V(qw.T);
            var ptOwner = posStart + V(ox.T);
            bool okPeer = placeable(ptPeer);
            ptPeer.Z += 5f;
            if (!okPeer) continue;
            bool okOwner = placeable(ptOwner);
            ptOwner.Z += 5f;
            if (!okOwner) continue;
            var dir = ptPeer - ptOwner;
            float len = dir.Length();
            if (len > 0) dir /= len;
            if (rayHits(ptOwner, dir, len)) continue;
            yaw = ToAngles(pw.R, out var a) ? a.Z : 0f;
            posA = ptPeer;
            posB = posStart + V(pw.T);
            return true;
        }
        return false;
    }
}

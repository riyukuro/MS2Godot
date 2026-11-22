using System;
using System.Collections.Generic;
using Godot;
using Ms2.Net.Replay;
using Ms2Godot.Movement;

namespace Ms2Godot.Net;

// StateHold's target (stateholddata.cpp vfunc_3 / vfunc_9): the held actor of the field and its named node.
public sealed partial class RemoteLayer
{
    /// <summary>Bone index per skeleton and node name (FindBone converts the name to a StringName each call).</summary>
    readonly Dictionary<(ulong, string), int> holdBones = new();

    /// <summary>
    /// WorldService::FUN_141bd1200 (the actor of the object id) and PCBase::FUN_1416757c0 (its node by name, m_unk_b88) over
    /// the field's users and npcs for the PC <paramref name="asker"/>: the node is the body's skeleton bone of that name, its
    /// world rotation and translate in MS2 axes. A PC's map holds its ridee's passenger seat nodes too (Ridee::vfunc_119
    /// adds them, overwriting by name), so a riding PC's node is the mount's bone first; with the asker's seat on the mount
    /// (FUN_141676790, seat 0 when not in the map) the seat's position, rotation and sequences come with it.
    /// The own pc (<see cref="SelfPc"/>) is looked up the same way, with its mount and <see cref="SelfPassengers"/>.
    /// A ridee's object id (OnObjectCreated of m_spB10, pcbase.cpp:3321) resolves in the client (FUN_141bd1200 is
    /// FindObjectById and a cast to PCBase), but StateHold asks the target's own m_unk_b88 for the node (FUN_1416757c0,
    /// pcbase.cpp:2995). Ridee's vfunc_119 (ridee.cpp:638) fills the rider's map instead, and no function of the exe
    /// fills a ridee's own (+0xb88 is written by PCBase's ctor, dtor, vfunc_14 and vfunc_119 only, and Ridee overrides
    /// slot 119), so the node is null and the hold falls back to its jump on the first frame: HoldKind.None does the same.
    /// </summary>
    // StateHold asks every frame: the path made once (a string and a NodePath per call otherwise)
    static readonly NodePath HoldSkeletonPath = "Model/" + ModelBuilder.SkeletonName;

    /// <summary>The own pc of an object id (MyPC in WorldService's actor map), null for any other id or before it is in.</summary>
    public Func<int, Player> SelfPc;
    /// <summary>The own pc's m_mapB28 (passenger object id to seat).</summary>
    public IReadOnlyDictionary<int, int> SelfPassengers;

    public HoldTarget FindHoldTarget(int asker, int id, string node)
    {
        var t = default(HoldTarget);
        Node3D visual;
        Mount mount = null;
        IReadOnlyDictionary<int, int> passengers = null;
        int rideeState = 0, rideeSub = 0;
        float rideeSpeed = 0f;
        if (users.TryGetValue(id, out var r))
        {
            t.Kind = HoldKind.Pc; visual = r.Visual;
            t.Dead = r.Pc?.States.Dead ?? false;    // pTarget->IsDead() (stateholddata.cpp:211): the fallback jump
            if (r.Ridee is { } ridee)
            {
                t.HasRidee = true;
                t.RideeZ = ridee.Position.Z;
                mount = ridee.Mount;
                passengers = r.Passengers;
                rideeState = ridee.States.Cur;
                rideeSub = ridee.States.Sub;
                rideeSpeed = ridee.AnimSpeed;
            }
        }
        else if (SelfPc?.Invoke(id) is { } self && IsInstanceValid(self))
        {
            t.Kind = HoldKind.Pc; visual = self.Visual;
            t.Dead = self.DeadFlag;
            if (self.Mount != null)
            {
                // the one body is the mount's (Ridee::vfunc_10): its feet are the ridee's, (x, y, z) to MS2 (x, -z, y)
                t.HasRidee = true;
                t.RideeZ = self.GlobalPosition.Y;
                mount = self.Mount;
                passengers = SelfPassengers;
                (rideeState, rideeSub) = self.RideeStateIds;
                rideeSpeed = 1f;   // the own mount's sequences play at 1 (Mount.Play)
            }
        }
        else if (npcs.HoldInfo(id, out int npcId, out bool dead, out visual)) { t.Kind = HoldKind.Npc; t.NpcId = npcId; t.Dead = dead; }
        else return t;
        RideSeat model = null;
        if (mount != null)
        {
            // GetRidee, GetData, GetPassenger(FUN_141676790(asker)): seat 0 for a rider not in m_mapB28
            model = mount.Data.Passenger(passengers?.GetValueOrDefault(asker) ?? 0);
            if (model != null)
            {
                t.HasSeat = true;
                t.RideeState = rideeState;
                t.RideeSub = rideeSub;
                t.RideeAnimSpeed = rideeSpeed;
                t.Seat = model.Sequences;
            }
            int mb = mount.Bone(node);
            if (mb >= 0) { SetNode(ref t, mount.BoneWorld(mb), model); return t; }
        }
        var sk = visual?.GetNodeOrNull<Skeleton3D>(HoldSkeletonPath);
        if (sk == null || string.IsNullOrEmpty(node)) return t;
        var key = (sk.GetInstanceId(), node);
        if (!holdBones.TryGetValue(key, out int bone)) holdBones[key] = bone = sk.FindBone(node);
        if (bone < 0) return t;
        SetNode(ref t, sk.GlobalTransform * sk.GetBoneGlobalPose(bone), model);
        return t;
    }

    /// <summary>
    /// WorldService::FindNearestActor (@0x141bc9830, worldservice_1.cpp:5143) over the field's other players: each one not
    /// dead whose nearest hold node (PC::vfunc_118, the distance from <paramref name="from"/> to each node of m_unk_b88) lies
    /// within <paramref name="maxDist"/> and below <paramref name="best"/> replaces the pick, then the npcs
    /// (<see cref="NpcLayer.NearestHold"/>). 0 when none is nearer.
    /// </summary>
    public int NearestHold(Vector3 from, float maxDist, ref float best, out string node)
    {
        node = null;
        int id = 0;
        foreach (var r in users.Values)
        {
            if (r.Pc == null || r.Pc.States.Dead || r.Visual == null || !IsInstanceValid(r.Visual)) continue;
            float d = NearestHoldNode(r.Visual.GetNodeOrNull<Skeleton3D>(HoldSkeletonPath), r.Ridee?.Mount, from, out string n);
            if (d > maxDist || d >= best) continue;
            best = d;
            id = r.ObjectId;
            node = n;
        }
        if (npcs != null && npcs.NearestHold(from, maxDist, ref best, out string npcNode) is var npc && npc != 0) { id = npc; node = npcNode; }
        return id;
    }

    /// <summary>
    /// PC::vfunc_118 (@0x141655a30, pc.cpp:1307) over a PC's m_unk_b88: PCBase::vfunc_119's "Hold_Point00".."Hold_Point49"
    /// nodes of the body (the optimizer's DAT_143ae5ce0 names are STILL-OPEN) and Ridee::vfunc_119's passenger rideBone
    /// nodes of its mount (ridee.cpp:638; the list stops at the first seat without a rideBone). float.MaxValue when the map
    /// is empty.
    /// </summary>
    public static float NearestHoldNode(Skeleton3D body, Mount mount, Vector3 from, out string node)
    {
        node = null;
        float best = float.MaxValue;
        if (body != null && IsInstanceValid(body))
        {
            foreach (var (bone, name) in HoldPoints(body))
            {
                float d = (body.GlobalTransform * body.GetBoneGlobalPose(bone)).Origin.DistanceTo(from);
                if (d < best) { best = d; node = name; }
            }
        }
        if (mount?.Data != null && mount.Skeleton != null && IsInstanceValid(mount.Skeleton))
        {
            foreach (var (b, name) in mount.RideBones())
            {
                float d = mount.BoneWorld(b).Origin.DistanceTo(from);
                if (d < best) { best = d; node = name; }
            }
        }
        return best;
    }

    static readonly string[] HoldPointNames = MakeHoldPointNames();
    static string[] MakeHoldPointNames()
    {
        var n = new string[50];
        for (int i = 0; i < n.Length; i++) n[i] = "Hold_Point" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        return n;
    }

    /// <summary>The Hold_PointNN bones of a skeleton, found once per skeleton; emptied when a field's layer goes
    /// (<see cref="_ExitTree"/>), as the skeletons go with it.</summary>
    static readonly Dictionary<ulong, (int Bone, string Name)[]> holdPoints = new();
    static (int Bone, string Name)[] HoldPoints(Skeleton3D sk)
    {
        ulong key = sk.GetInstanceId();
        if (holdPoints.TryGetValue(key, out var list)) return list;
        var found = new List<(int, string)>();
        foreach (var name in HoldPointNames)
        {
            int b = sk.FindBone(name);
            if (b >= 0) found.Add((b, name));
        }
        return holdPoints[key] = found.ToArray();
    }

    /// <summary>What MyPC::FUN_1415279d0 reads of another player: its ridee, m_mapB28's count, the ridee's passenger count
    /// (CRideeData m_passengers), its character id, guild name, feet and m_b1224. False when it is not here.</summary>
    public bool HoldPcInfo(int id, out bool hasRidee, out int passengers, out int seats, out long characterId, out string guild,
        out Vector3 feet, out bool noBoarding)
    {
        hasRidee = false; passengers = seats = 0; characterId = 0; guild = ""; feet = default; noBoarding = false;
        if (!users.TryGetValue(id, out var r)) return false;
        hasRidee = r.Ridee != null;
        passengers = r.Passengers.Count;
        seats = r.Ridee?.Mount?.Data.Seats.Count ?? 0;
        characterId = r.CharacterId;
        guild = r.GuildName;
        noBoarding = r.NoBoarding;
        if (r.Root != null && IsInstanceValid(r.Root) && r.Root.IsInsideTree()) feet = r.Root.GlobalPosition;
        return true;
    }

    /// <summary>The node's world translation and rotation row 0 in MS2 axes, and with a seat model Ridee::FUN_1417083a0's
    /// seat on the node (the passenger's translation and rotation).</summary>
    static void SetNode(ref HoldTarget t, in Transform3D godotWorld, RideSeat model)
    {
        var w = ToClient * godotWorld;
        var b = w.Basis;
        float s = b.Column0.Length();
        if (s == 0) s = 1;
        t.HasNode = true;
        t.NodePosition = new System.Numerics.Vector3(w.Origin.X, w.Origin.Y, w.Origin.Z);
        t.NodeRow0 = new System.Numerics.Vector3(b.Column0.X / s, b.Column1.X / s, b.Column2.X / s);
        if (model == null) return;
        Mount.ClientSeat(godotWorld, model.Translation, model.Rotation, out var p, out var r);
        t.SeatPosition = new System.Numerics.Vector3(p.X, p.Y, p.Z);
        t.SeatRow0 = new System.Numerics.Vector3(r.Column0.X, r.Column1.X, r.Column2.X);
    }

    /// <summary>Godot world to the client's Z-up world, (x, y, z) to (x, -z, y) (DummyAnchor's ToClient).</summary>
    static readonly Transform3D ToClient = new(new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0)), Vector3.Zero);

    /// <summary>The field is freed: its skeletons' hold points are dropped (the next field's are found again).</summary>
    public override void _ExitTree() => holdPoints.Clear();
}

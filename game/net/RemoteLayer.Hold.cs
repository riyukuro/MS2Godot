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
    /// STILL-OPEN: the local player and the ridees as targets are not looked up (HoldKind.None).
    /// </summary>
    // StateHold asks every frame: the path made once (a string and a NodePath per call otherwise)
    static readonly NodePath HoldSkeletonPath = "Model/" + ModelBuilder.SkeletonName;

    HoldTarget FindHoldTarget(int asker, int id, string node)
    {
        var t = default(HoldTarget);
        Node3D visual;
        Remote owner = null;
        if (users.TryGetValue(id, out var r)) { t.Kind = HoldKind.Pc; visual = r.Visual; owner = r; }
        else if (npcs.HoldInfo(id, out int npcId, out bool dead, out visual)) { t.Kind = HoldKind.Npc; t.NpcId = npcId; t.Dead = dead; }
        else return t;
        RideSeat model = null;
        if (owner?.Ridee is { } ridee && ridee.Mount != null)
        {
            // GetRidee, GetData, GetPassenger(FUN_141676790(asker)): seat 0 for a rider not in m_mapB28
            model = ridee.Mount.Data.Passenger(owner.Passengers.GetValueOrDefault(asker));
            if (model != null)
            {
                t.HasSeat = true;
                t.RideeState = ridee.States.Cur;
                t.RideeSub = ridee.States.Sub;
                t.RideeAnimSpeed = ridee.AnimSpeed;
                t.Seat = model.Sequences;
            }
            int mb = ridee.Mount.Bone(node);
            if (mb >= 0) { SetNode(ref t, ridee.Mount.BoneWorld(mb), model); return t; }
        }
        var sk = visual?.GetNodeOrNull<Skeleton3D>(HoldSkeletonPath);
        if (sk == null || string.IsNullOrEmpty(node)) return t;
        var key = (sk.GetInstanceId(), node);
        if (!holdBones.TryGetValue(key, out int bone)) holdBones[key] = bone = sk.FindBone(node);
        if (bone < 0) return t;
        SetNode(ref t, sk.GlobalTransform * sk.GetBoneGlobalPose(bone), model);
        return t;
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
}

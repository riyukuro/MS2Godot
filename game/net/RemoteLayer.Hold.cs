using System.Collections.Generic;
using Godot;
using Ms2.Net.Replay;

namespace Ms2Godot.Net;

// StateHold's target (stateholddata.cpp vfunc_3 / vfunc_9): the held actor of the field and its named node.
public sealed partial class RemoteLayer
{
    /// <summary>Bone index per skeleton and node name (FindBone converts the name to a StringName each call).</summary>
    readonly Dictionary<(ulong, string), int> holdBones = new();

    /// <summary>
    /// WorldService::FUN_141bd1200 (the actor of the object id) and PCBase::FUN_1416757c0 (its node by name, m_unk_b88) over
    /// the field's users and npcs: the node is the body's skeleton bone of that name, its world rotation and translate in
    /// MS2 axes.
    /// STILL-OPEN: the local player and the ridees as targets are not looked up (HoldKind.None).
    /// </summary>
    HoldTarget FindHoldTarget(int id, string node)
    {
        var t = default(HoldTarget);
        Node3D visual;
        if (users.TryGetValue(id, out var r)) { t.Kind = HoldKind.Pc; visual = r.Visual; }
        else if (npcs.HoldInfo(id, out int npcId, out bool dead, out visual)) { t.Kind = HoldKind.Npc; t.NpcId = npcId; t.Dead = dead; }
        else return t;
        var sk = visual?.GetNodeOrNull<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        if (sk == null || string.IsNullOrEmpty(node)) return t;
        var key = (sk.GetInstanceId(), node);
        if (!holdBones.TryGetValue(key, out int bone)) holdBones[key] = bone = sk.FindBone(node);
        if (bone < 0) return t;
        var w = ToClient * (sk.GlobalTransform * sk.GetBoneGlobalPose(bone));
        var b = w.Basis;
        float s = b.Column0.Length();
        if (s == 0) s = 1;
        t.HasNode = true;
        t.NodePosition = new System.Numerics.Vector3(w.Origin.X, w.Origin.Y, w.Origin.Z);
        t.NodeRow0 = new System.Numerics.Vector3(b.Column0.X / s, b.Column1.X / s, b.Column2.X / s);
        return t;
    }

    /// <summary>Godot world to the client's Z-up world, (x, y, z) to (x, -z, y) (DummyAnchor's ToClient).</summary>
    static readonly Transform3D ToClient = new(new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0)), Vector3.Zero);
}

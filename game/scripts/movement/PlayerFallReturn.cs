using System;
using Godot;
using Ms2Godot.Movement;

namespace Ms2Godot;

/// <summary>
/// Falling out of the field. The client has no floor of its own: the world box (MoveStep) walls the sides and the top, and
/// MyPC::FUN_141524a80 (@0x141524a80, mypc.cpp:2286-2370) only forces one state sync once the feet go below the box floor.
/// The return is the server's answer to the state syncs. Offline this file stands in for that server with the restored-
/// client reference (Maple2 fork kms2525): FieldPlayer.OnStateSync keeps the last "ground" position and
/// FieldManager.EnsurePlayerPosition sends the player back there when a sync lies outside the map's bounding prism.
/// </summary>
public partial class Player
{
    /// <summary>
    /// The server's bounding prism (kms2525 MapEntityStorage.cs:105-113): the box spanned by the first two MS2Bounding
    /// entities' x/y, from the lower z up by the z distance. Without two of them it is LargeBoundingBox, +-127 cubes on
    /// every axis (MapEntityStorage.cs:13-17). Field units and axes (Z up).
    /// </summary>
    public Vector3 PrismMin = new(-PrismLimit, -PrismLimit, -PrismLimit), PrismMax = new(PrismLimit, PrismLimit, PrismLimit);
    public const float PrismLimit = sbyte.MaxValue * 150f;

    /// <summary>
    /// The server's position test for a return (kms2525 FieldManager.ValidPosition): the field's navmesh has a polygon near
    /// the point (field units). Null or false = no polygon, and the return is not sent.
    /// </summary>
    public Func<Vector3, bool> ValidPosition;

    /// <summary>
    /// CTrafficOptimizer::GetTrafficLimit (@0x14087d2f0, trafficoptimizer.cpp:33-63): the ms a state record waits before
    /// it is sent. The parts come from the TrafficOptimizer game event (GameEventTrafficOptimizerCli::OnStart,
    /// gameeventcli.cpp:674-679), which is server data; without it every part is 0 and the limit is MIN_TRAFFIC, 100.
    /// </summary>
    public int TrafficLimitMs = 100;

    /// <summary>The server processed a state sync: the synced position and whether it carried a cube (SyncNumber).</summary>
    public event Action<Vector3, bool> ServerSynced;

    /// <summary>
    /// A synced position lay outside the prism: the return's target (Align(LastGroundPosition) + 150) and whether the
    /// server sent it (ValidPosition passed). The warp itself happens at the next physics frame.
    /// </summary>
    public event Action<Vector3, Vector3, bool> BoundsReturn;

    /// <summary>One StateSyncObject: the synced fields this model needs, its age and whether it went out.</summary>
    sealed class SyncRecord
    {
        public Vector3 Pos; public bool Marked; public MoveState State; public Vector3 Dir; public float Yaw;
        public double Made; public float Time; public bool Sent;
    }

    int hitFallReturn = -1;               // the hit report's entity when it is a fall-return cube (FieldBuilder.FallReturnMeta), else -1
    bool fallMarked;                      // MyPC m_b15d6: the below-the-box-floor sync was forced
    SyncRecord current;                   // StatePcGuideObjectController m_spCurrent
    readonly System.Collections.Generic.List<SyncRecord> queued = new();   // m_records: the next packet's records
    double syncClock;
    Vector3 moveDir;                      // GameObject m_pos98: the move direction (the state's GetMoveDirection), not the body's velocity
    Vector3 lastGround;                   // kms2525 FieldPlayer.LastGroundPosition: default (0, 0, 0) until a marked sync
    Vector3? portal;                      // the server's MoveByPortal, delivered at the next frame like a packet

    /// <summary>The server's last ground position (field units), for probes.</summary>
    public Vector3 LastGround => lastGround;

    void ClearHitReport()
    {
        hitGroup = -1;
        hitFallReturn = -1;
    }

    /// <summary>
    /// MyPC::FUN_141524a80 (mypc.cpp:2286-2370) with the client's default -optimizeUserSync 1 (unit_1406aee80
    /// launchoptions: m_bOptimizeUserSync = 1). The current record ages by the frame and goes out once it is
    /// TrafficLimitMs old (IsRecordDue, state.cpp:411-423). A new record is made when the feet first go below the world
    /// box's floor (forced), when there is none, or when the state, the move direction (m_pos98) or the target yaw changed or a second
    /// passed since the current one; an unsent current record goes out first, except that a walk record left for another
    /// state goes out in one packet with the new record. Not modelled: m_b15d5 and the state's own vfunc_22 test.
    /// </summary>
    void StateSync(float dt)
    {
        if (portal is { } to)                                                       // CSceneStateGame::OnUserMoveByPortal
        {
            portal = null;
            UserMoveByPortal(to, 0f);
        }
        syncClock += dt;
        if (current != null) current.Time += dt;                                    // FUN_1417b76b0
        if (current != null && !current.Sent && (int)(current.Time * 1000f) >= TrafficLimitMs) Flush();
        var g = GlobalPosition;                                                      // the feet (vfunc_18)
        var feet = new Vector3(g.X, -g.Z, g.Y);
        bool force = false;
        if (!fallMarked && feet.Z < BoundsMin.Z)
        {
            fallMarked = true;
            force = true;
        }
        if (!(force || current == null || State != current.State || moveDir != current.Dir || syncClock - current.Made >= 1.0 || targetYawDeg != current.Yaw))
            return;
        bool flushAfter = false;
        if (current != null)
        {
            if (current.State == MoveState.Walk && State != MoveState.Walk) flushAfter = true;   // gosWalk (2) left
            else Flush();
        }
        // mypc.cpp:2353-2366: m_nA0, the record's last int, is the cube's cell when the hit report's entity is a MS2CubeProp
        // with IsFallReturn and PropCollisionGroup 9 or 8, the PC is not falling (vfunc_99(0) == 0), and a ray straight
        // down from the feet (CPhysXHelper::FUN_14168a9b0, groups 0x389) hits that entity first; else 0x7fffffff
        bool marked = hitFallReturn >= 0 && !InAirFor(MoveConfig.IdleFallDelay) && GroundFallReturnEntity() == hitFallReturn;
        var rec = new SyncRecord
        {
            // StateSyncObject::FUN_1408356c0 writes the position as three int16 (CByteBuffer::WriteVector3Short, stream.cpp:127)
            Pos = new Vector3((short)feet.X, (short)feet.Y, (short)(feet.Z + 0.0001f)), Marked = marked,
            State = State, Dir = moveDir, Yaw = targetYawDeg, Made = syncClock,
        };
        QueueCurrent();                                                              // FUN_1417b7670
        current = rec;
        if (flushAfter) Flush();
    }

    /// <summary>FUN_1417b7720 (optimize mode): an unsent current record joins the packet and stays current.</summary>
    void QueueCurrent()
    {
        if (current == null || current.Sent) return;
        queued.Add(current);
        current.Sent = true;
    }

    /// <summary>
    /// StateLocalController::vfunc_0 (state.cpp:483-505): the queued records go out in one packet. The server reads only
    /// the packet's last record (kms2525 UserSyncHandler.cs:49, session.Player.OnStateSync(stateSyncs[^1])).
    /// </summary>
    void Flush()
    {
        QueueCurrent();
        if (queued.Count == 0) return;
        var last = queued[^1];
        queued.Clear();
        ServerStateSync(last.Pos, last.Marked);
    }

    /// <summary>The fall-return entity of the first shape straight below the feet on groups 0x389, or -1.</summary>
    int GroundFallReturnEntity()
    {
        var from = GlobalPosition;
        var q = rayQ.Set(from, from + Vector3.Down * MoveConfig.UnlimitedRay, GetRid());
        var exclude = rayQ.Ex;
        var space = GetWorld3D().DirectSpaceState;
        for (int i = 0; i < 16; i++)
        {
            var hit = space.IntersectRay(q);
            if (hit.Count == 0) return -1;
            var col = hit["collider"].AsGodotObject();
            int group = col is Node n && n.HasMeta("ms2_group") ? (int)n.GetMeta("ms2_group") : 0;
            if ((GroundGroups & (1u << group)) != 0) return FallReturnEntity(col, (int)hit["shape"]);
            exclude.Add((Rid)hit["rid"]);
            q.Exclude = exclude;
        }
        return -1;
    }

    static int FallReturnEntity(GodotObject collider, int shape)
    {
        if (collider is not CollisionObject3D body || shape < 0) return -1;
        var owner = body.ShapeOwnerGetOwner(body.ShapeFindOwner(shape)) as Node;
        return owner != null && owner.HasMeta(FieldBuilder.FallReturnMeta) ? (int)owner.GetMeta(FieldBuilder.FallReturnMeta) : -1;
    }

    /// <summary>
    /// The server's handling of one state sync (kms2525): FieldPlayer.OnStateSync (FieldPlayer.cs:248-266) keeps the
    /// position as LastGroundPosition when SyncNumber (m_nA0) is not int.MaxValue, then EnsurePlayerPosition
    /// (FieldManager.cs:364-371, called at FieldPlayer.cs:356): a position outside the prism (x/y within 0.001, z within
    /// [base, top]; Prism.Contains, BoundingBox.Contains) takes FallDamage(Constants.FallBoundingAddedDistance) and
    /// MoveToPosition(LastGroundPosition.Align() + (0, 0, 150)). MoveToPosition (FieldPlayer.cs:585-591) sends
    /// PortalPacket.MoveByPortal only when Field.ValidPosition passes. Not done offline: the fall damage (no HP;
    /// FallBoundingAddedDistance is in the server's constants.xml, not the client's).
    /// </summary>
    void ServerStateSync(Vector3 pos, bool marked)
    {
        if (marked) lastGround = pos;
        ServerSynced?.Invoke(pos, marked);
        const float eps = 0.001f;
        if (PrismMin.Z <= pos.Z && pos.Z <= PrismMax.Z
            && PrismMin.X - eps <= pos.X && pos.X <= PrismMax.X + eps && PrismMin.Y - eps <= pos.Y && pos.Y <= PrismMax.Y + eps)
            return;
        var to = Align(lastGround) + new Vector3(0, 0, 150);
        bool valid = ValidPosition?.Invoke(to) ?? false;
        BoundsReturn?.Invoke(pos, to, valid);
        if (!valid) return;
        // PortalPacket.MoveByPortal(actor, position, rotation) (PortalPacket.cs:88-96): the position goes out raised by 25,
        // rotation default (0, 0, 0)
        portal = to + new Vector3(0, 0, 25);
    }

    /// <summary>kms2525 VectorExtensions.Align: x and y to the nearest 150 (MathF.Round, half to even), z floored to 150.</summary>
    public static Vector3 Align(Vector3 p) => new(
        MathF.Round(p.X / 150f) * 150f,
        MathF.Round(p.Y / 150f) * 150f,
        MathF.Floor(MathF.Round(p.Z) / 150f) * 150f);

    /// <summary>
    /// CSceneStateGame::OnUserMoveByPortal (scenestategame.cpp:5775-5786) -> MyPC::FUN_141528e50 (@0x141528e50,
    /// mypc.cpp:2738-2791), the warp: the state records go, the below-the-floor latch is cleared, a living PC not riding goes
    /// to StateFall (5) while a ridee goes to StateIdle (1), the fall start is cleared, the position and facing are set,
    /// StopJump (no jump, no controller velocity, fall timer reset) and the hit report is cleared. Field units; the facing is
    /// the MS2 Z rotation in degrees.
    /// </summary>
    void UserMoveByPortal(Vector3 pos, float rotZ)
    {
        current = null; queued.Clear();                                             // ClearRecords / Ridee::ResetStateController
        fallMarked = false;                                                         // m_b15d6 = false
        Enter(Mount == null ? MoveState.Fall : MoveState.Idle);                    // vfunc_59(5) / spRidee->vfunc_59(1)
        fallStartY = float.NaN;                                                     // FUN_141676060(ZERO): m_posC10 = ZERO
        GlobalPosition = new Vector3(pos.X, pos.Z, -pos.Y);                         // field (x, y, z) -> Godot (x, z, -y)
        float r = Mathf.DegToRad(rotZ);
        Face(new Vector3(Mathf.Sin(r), 0, Mathf.Cos(r)));                           // forward (sin z, -cos z) in MS2 axes
        jumping = false;                                                            // StopJump (@0x141240730)
        kVel = Vector3.Zero;
        airTime = 0;
        fallTimerRunning = false;
        ClearHitReport();                                                           // GetHitReport()->FUN_140cfaf30()
        // m_pushControl.Reset() (mypc.cpp:2758): the Player has no push / knockback state to reset
    }
}

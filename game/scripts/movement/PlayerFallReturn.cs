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

    /// <summary>A bounds return happened: the synced position outside the prism, and where the player was sent.</summary>
    public event Action<Vector3, Vector3> BoundsReturn;

    int hitFallReturn = -1;               // the hit report's entity when it is a fall-return cube (FieldBuilder.FallReturnMeta), else -1
    bool fallMarked;                      // MyPC m_b15d6: the below-the-box-floor sync was forced
    bool haveRecord;                      // a current state sync record (StatePcGuideObjectController::GetCurrent)
    MoveState recState; Vector3 recVel; float recYaw; double recTime, syncClock;
    Vector3 lastGround;                   // kms2525 FieldPlayer.LastGroundPosition: default (0, 0, 0) until a marked sync

    /// <summary>The server's last ground position (field units), for probes.</summary>
    public Vector3 LastGround => lastGround;

    void ClearHitReport()
    {
        hitGroup = -1;
        hitFallReturn = -1;
    }

    /// <summary>
    /// MyPC::FUN_141524a80: a new state sync record when the feet first go below the world box's floor (forced), when there
    /// is no current record, or when the state, the velocity or the target yaw changed or a second passed since the current
    /// record. Not modelled: m_b15d5 and the state's own vfunc_22 test.
    /// </summary>
    void StateSync(float dt)
    {
        syncClock += dt;
        var g = GlobalPosition;                                                      // the feet (vfunc_18)
        var feet = new Vector3(g.X, -g.Z, g.Y);
        bool force = false;
        if (!fallMarked && feet.Z < BoundsMin.Z)
        {
            fallMarked = true;
            force = true;
        }
        if (!(force || !haveRecord || State != recState || Velocity != recVel || syncClock - recTime >= 1.0 || targetYawDeg != recYaw))
            return;
        haveRecord = true;
        recState = State; recVel = Velocity; recYaw = targetYawDeg; recTime = syncClock;
        // mypc.cpp:2353-2366: m_nA0, the record's last int, is the cube's cell when the hit report's entity is a MS2CubeProp
        // with IsFallReturn and PropCollisionGroup 9 or 8, the PC is not falling (vfunc_99(0) == 0), and a ray straight
        // down from the feet (CPhysXHelper::FUN_14168a9b0, groups 0x389) hits that entity first; else 0x7fffffff
        bool marked = hitFallReturn >= 0 && !InAirFor(MoveConfig.IdleFallDelay) && GroundFallReturnEntity() == hitFallReturn;
        // StateSyncObject::FUN_1408356c0 writes the position as three int16 (CByteBuffer::WriteVector3Short, stream.cpp:127)
        ServerStateSync(new Vector3((short)feet.X, (short)feet.Y, (short)(feet.Z + 0.0001f)), marked);
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
    /// The server's handling of one state sync (kms2525): FieldPlayer.OnStateSync (FieldPlayer.cs:264-266) keeps the
    /// position as LastGroundPosition when SyncNumber (m_nA0) is not int.MaxValue, then EnsurePlayerPosition
    /// (FieldManager.cs:364-371): a position outside the prism (x/y within 0.001, z within [base, top]; Prism.Contains,
    /// BoundingBox.Contains) takes FallDamage(Constants.FallBoundingAddedDistance) and MoveToPosition(LastGroundPosition.Align()
    /// + (0, 0, 150)). MoveToPosition sends MoveByPortal only when Field.ValidPosition finds a navmesh polygon there.
    /// Not done offline: the fall damage (no HP; FallBoundingAddedDistance is in the server's constants.xml, not the client's)
    /// and the ValidPosition test (the field's navmesh is only loaded for the F9 overlay).
    /// </summary>
    void ServerStateSync(Vector3 pos, bool marked)
    {
        if (marked) lastGround = pos;
        const float eps = 0.001f;
        if (PrismMin.Z <= pos.Z && pos.Z <= PrismMax.Z
            && PrismMin.X - eps <= pos.X && pos.X <= PrismMax.X + eps && PrismMin.Y - eps <= pos.Y && pos.Y <= PrismMax.Y + eps)
            return;
        var to = Align(lastGround) + new Vector3(0, 0, 150);
        // PortalPacket.MoveByPortal(actor, position, rotation): the position goes out raised by 25, rotation default (0, 0, 0)
        UserMoveByPortal(to + new Vector3(0, 0, 25), 0f);
        BoundsReturn?.Invoke(pos, to + new Vector3(0, 0, 25));
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
        haveRecord = false;                                                         // ClearRecords / Ridee::ResetStateController
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
    }
}

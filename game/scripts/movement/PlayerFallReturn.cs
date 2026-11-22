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
        public Vector3 Pos; public bool Marked; public MoveState State; public Vector3 Dir; public float Yaw, BodyYaw; public Vector3 CamEuler;
        public byte Sub; public int Tick;
        public double Made; public float Time; public bool Sent; public int Cell = int.MaxValue;
        public bool InQueue;              // in the queued list until the next Flush
    }

    // a walking frame makes a record every drawn frame (StateVfunc22): records are reused, so the sync allocates nothing per frame
    readonly System.Collections.Generic.Stack<SyncRecord> freeRecords = new();
    readonly System.Collections.Generic.List<(Ms2.Net.Packets.StateSync, int)> liveOut = new();

    SyncRecord RentRecord()
    {
        var r = freeRecords.Count > 0 ? freeRecords.Pop() : new SyncRecord();
        r.Time = 0; r.Sent = false; r.InQueue = false;
        return r;
    }

    int hitFallReturn = -1;               // the hit report's entity when it is a fall-return cube (FieldBuilder.FallReturnMeta), else -1
    bool fallMarked;                      // MyPC m_b15d6: the below-the-box-floor sync was forced
    SyncRecord current;                   // StatePcGuideObjectController m_spCurrent
    readonly System.Collections.Generic.List<SyncRecord> queued = new();   // m_records: the next packet's records
    double syncClock;
    Vector3 moveDir;                      // GameObject m_pos98: the move buffer's input (x +1 left, y +1 forward, z 0), not a world direction

    /// <summary>
    /// Unk_PCMove::GetMoveDirection (@0x141683290, unit_141682f80.cpp:313-383): x +1 / -1 and y +1 (forward) / -1, or the mouse
    /// move when no key is held. The receiver turns it by the sender's camera (PCBase::CalcMoveDirection, pcbase.cpp:2828-2840:
    /// y times vfunc_67's camera forward plus x times its side, side = -(forward x UP), so +x is left; vfunc_67 builds them from
    /// m_posBF4, pcbase.cpp:1310-1340). Our input has x to the right, so x flips.
    /// STILL-OPEN: the reversed-controls flip (bFlipped, AdditionalCliList FUN_140970a40, unit_141682f80.cpp:342-381) and the
    /// gamepad stick are not ported.
    /// </summary>
    static Vector3 MoveInputDir(Vector2 input) => new(-input.X, input.Y, 0f);
    Vector3 lastGround;                   // kms2525 FieldPlayer.LastGroundPosition: default (0, 0, 0) until a marked sync
    Vector3? portal;                      // the server's MoveByPortal, delivered at the next frame like a packet
    float portalRot;                      // its rotation z (degrees)

    /// <summary>Live mode (opt-in, Ms2.Net session): the records and state changes go out as real packets and the kms2525 emulation
    /// (ServerStateSync) stays off; the server's MoveByPortal and SyncNumber arrive through ServerMoveByPortal / OnSyncNumber.</summary>
    public bool Live;
    /// <summary>The queued records of one StateLocalController::vfunc_0 packet, in order (live only).</summary>
    public event Action<System.Collections.Generic.List<(Ms2.Net.Packets.StateSync Record, int Tick)>> StateSyncOut;
    /// <summary>The same while riding: the mount's controller's packet (StateRideeController::vfunc_0, RideSync).</summary>
    public event Action<System.Collections.Generic.List<(Ms2.Net.Packets.StateSync Record, int Tick)>> RideSyncOut;
    /// <summary>GetServerTick (live): stamps each record when it is queued (m_nAC = nTick, mypc.cpp:2332).</summary>
    public Func<int> ServerTick;
    /// <summary>MyPC::SendStateChange (mypc.cpp:3655-3666): mode, argument and the feet position in field units (live only).</summary>
    public event Action<byte, byte, Vector3> StateChangeOut;
    /// <summary>A state whose vfunc_4 starts with PC::SendRequestCube was entered (<see cref="Ms2.Ui.HeldItem.EnterState"/>).</summary>
    public event Action<Ms2.Ui.HeldItemState> HeldStateEntered;

    /// <summary>The client's state id (gostate.h) of a move state, for the sync record's first byte.</summary>
    static byte StateId(MoveState s) => s switch
    {
        MoveState.Idle => 1, MoveState.Walk => 2, MoveState.Land => 4, MoveState.Fall => 5, MoveState.Jump => 6,
        MoveState.Ladder => 8, MoveState.Rope => 9, MoveState.Dead => 0xc, MoveState.Hit => 0xd, MoveState.Revival => 0x18, MoveState.Dash => 20, MoveState.Swim => 27, MoveState.Climb => 29, _ => 1,
    };

    /// <summary>The state's own sub id (FUN_14166cc80, mypc.cpp:2323): Walk 0x18 (statewalk.cpp:86; the gamepad 0x19 is not modelled), Swim 0x84,
    /// Jump the Player's jumpSub 0x21-0x24, Land 0x86, Idle 1 (boredom variants 2-5 not modelled), Ladder/Rope/Climb/Dash their m_n10; Fall 0.</summary>
    byte SubOf(MoveState s)
    {
        // The player's own records never carry the riding ids 0x17/0x27/0x85/0x87: that flag (m_unk_b80, FUN_141676b70) is set
        // by Ridee::UpdateRunEffects on the mount itself (ridee.cpp:721-779).
        // Rope 0x33-0x39 (statemanager.cpp:1226-1414), climb 0x56-0x62 (state.cpp:1049-1051, 1177), dash 0x1d-0x1f
        // (state.cpp:1812-1920) and ladder 0x2a-0x31 (stateladder.cpp:237-462) are the states' own m_n10, which the controller keeps.
        // Fall has no sub id (0).
        return s switch
        {
            MoveState.Walk => (byte)0x18,
            MoveState.Swim => 0x84,
            MoveState.Jump => (byte)jumpSub,
            MoveState.Land => 0x86,
            MoveState.Idle => 1,
            MoveState.Ladder => (byte)lsub,
            MoveState.Rope => (byte)rsub,
            MoveState.Climb => (byte)csub,
            MoveState.Dash => (byte)dsub,
            _ => 0,
        };
    }

    /// <summary>The cube cell key of a field position (ugcmapdata_1.cpp:270-280): (int)(p / 150) per axis packed x | y &lt;&lt; 8 | z &lt;&lt; 16.
    /// The client takes the cell of the hit prop's position; the feet cell is used here (the server only tests != int.MaxValue).</summary>
    static int CellKey(Vector3 p) => ((int)(p.X / 150f) & 0xFF) | (((int)(p.Y / 150f) & 0xFF) << 8) | (((int)(p.Z / 150f) & 0xFF) << 16);

    /// <summary>The server's MoveByPortal (live): the warp runs at the next frame through UserMoveByPortal.</summary>
    /// <summary>The last MoveByPortal's IsPortal: the client's bFlyCamera (mypc.cpp:2774-2788 plays the camera fly-by to the new position
    /// when the camera is in mode 2 and it is set). The fly-by itself is not built yet; the flag is kept for it.</summary>
    public bool PortalFlyCamera { get; private set; }
    public void ServerMoveByPortal(Vector3 pos, float rotZ, bool isPortal = false) { portal = pos; portalRot = rotZ; PortalFlyCamera = isPortal; }

    /// <summary>CNetworkService::OnSyncNumber: the local controller's records are cleared (the number itself lives in the session).
    /// It is MyPC's own controller (MyPC::GetStateController, mypc.cpp:1628, not virtual; networkservice.cpp:803): while riding
    /// that is the set put aside, and the mount's records stay.</summary>
    public void OnSyncNumber()
    {
        if (Mount != null) { footCurrent = null; footQueued.Clear(); }
        else { current = null; queued.Clear(); }
    }

    /// <summary>The server's last ground position (field units), for probes.</summary>
    public Vector3 LastGround => lastGround;

    void ClearHitReport()
    {
        hitGroup = -1;
        hitFallReturn = -1;
        hitOwner = 0;
        ground.Entity = null;
    }

    /// <summary>
    /// MyPC::FUN_141524a80 (mypc.cpp:2286-2370) with the client's default -optimizeUserSync 1 (unit_1406aee80
    /// launchoptions: m_bOptimizeUserSync = 1). The current record ages by the frame and goes out once it is
    /// TrafficLimitMs old (IsRecordDue, state.cpp:411-423). A new record is made when the feet first go below the world
    /// box's floor (forced), when there is none, or when the state, the move direction (m_pos98) or the target yaw changed or a second
    /// passed since the current one; an unsent current record goes out first, except that a walk record left for another
    /// state goes out in one packet with the new record. The state's own test (vfunc_22, StateVfunc22) also makes one: a
    /// walk makes a record every frame. Not modelled: m_b15d5.
    /// </summary>
    void StateSync(float dt)
    {
        if (portal is { } to)                                                       // CSceneStateGame::OnUserMoveByPortal
        {
            portal = null;
            UserMoveByPortal(to, portalRot); portalRot = 0f;
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
        // the mount's test (Ridee::FUN_141706650, ridee.cpp:924) has no one-second and no target-yaw term
        bool onFoot = Mount == null;
        if (!(force || current == null || State != current.State || moveDir != current.Dir
              || onFoot && (syncClock - current.Made >= 1.0 || targetYawDeg != current.Yaw) || StateVfunc22()))
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
        var rec = RentRecord();
        {
            // StateSyncObject::FUN_1408356c0 writes the position as three int16 (CByteBuffer::WriteVector3Short, stream.cpp:127)
            rec.Pos = new Vector3((short)feet.X, (short)feet.Y, (short)(feet.Z + 0.0001f)); rec.Marked = marked;
            rec.Cell = marked ? CellKey(feet) : int.MaxValue;
            rec.State = State; rec.Dir = moveDir; rec.Yaw = targetYawDeg; rec.BodyYaw = yawDeg; rec.CamEuler = CameraEuler; rec.Sub = SubOf(State);
            rec.Tick = ServerTick?.Invoke() ?? 0; rec.Made = syncClock;
        }
        QueueCurrent();                                                              // FUN_1417b7670
        if (current != null && !current.InQueue) freeRecords.Push(current);         // sent in an earlier packet: no longer held
        current = rec;
        if (flushAfter) Flush();
    }

    double swimRideTick;                  // StateSwim m_nMode: the tick of its last new record, set on entering (stateswim.cpp:92)

    /// <summary>
    /// The current state's vfunc_22(spCur), the last test of the new-record condition (mypc.cpp:2311). State::vfunc_22 is 0
    /// (state.cpp:732). StateWalk's is always 1 (statewalk.cpp:392-395), so every walking frame makes a record and the
    /// unsent one before it goes out. StateSwim's is 1 once a second while riding (stateswim.cpp:273-288); its other test,
    /// and those of StateIdle (stateidle.cpp:510) and StateJump (statejump.cpp:346), compare the record's sub-state, which
    /// this model's states do not change, so they are 0.
    /// </summary>
    bool StateVfunc22()
    {
        switch (State)
        {
            case MoveState.Walk:
                return true;
            case MoveState.Swim:
                if (Mount != null && syncClock - swimRideTick >= 1.0)
                {
                    swimRideTick = syncClock;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>FUN_1417b7720 (optimize mode): an unsent current record joins the packet and stays current.</summary>
    void QueueCurrent()
    {
        if (current == null || current.Sent) return;
        queued.Add(current);
        current.Sent = true;
        current.InQueue = true;
    }

    /// <summary>
    /// StateLocalController::vfunc_0 (state.cpp:483-505): the queued records go out in one packet. The server reads only
    /// the packet's last record (kms2525 UserSyncHandler.cs:49, session.Player.OnStateSync(stateSyncs[^1])).
    /// </summary>
    void Flush()
    {
        QueueCurrent();
        if (queued.Count == 0) return;
        if (Live)
        {
            liveOut.Clear();   // the handler copies the records into its packet before it returns
            foreach (var q in queued) liveOut.Add((LiveRecord(q), q.Tick));
            ReleaseQueued();
            (Mount != null ? RideSyncOut : StateSyncOut)?.Invoke(liveOut);
            return;
        }
        var last = queued[^1];
        var (pos, marked) = (last.Pos, last.Marked);
        ReleaseQueued();
        ServerStateSync(pos, marked);
    }

    /// <summary>The flushed records go back to the pool, except the current one (it stays until the next record replaces it).</summary>
    void ReleaseQueued()
    {
        foreach (var q in queued)
        {
            q.InQueue = false;
            if (q != current) freeRecords.Push(q);
        }
        queued.Clear();
    }

    /// <summary>
    /// The wire record of a sync (StateSyncObject::vfunc_1, statesyncobject.cpp:113-146), filled as MyPC builds it (mypc.cpp:2324-2332):
    /// SubState = FUN_14166cc80 (the state's own sub id, mypc.cpp:2323: SubOf); Rotation = m_dir2c.z (vfunc_20, the body's direction); Animation = m_nDirFlags, from m_vel3c = m_pos98, the move
    /// direction, packed by FUN_140836250 (statesyncobject.cpp:202-220: x -1/0/1 = 0/1/2, y -1/0/1 = +4/+8/+12, else 0x80 and the two
    /// floats follow); Speed = m_pos48 = m_posBF4 = the camera mode's Euler rotation m_setting.m_18 in degrees as int16 (FUN_140afdd40 over the eye and look, ported in Ms2.Net CameraMath; cameramode.cpp:316-331,
    /// targetcameramode.cpp:302,872). It is refreshed with every record while CameraMgr::m_1b0 != 0, which is set at field camera setup (camera.cpp:803 via 1847) and cleared only at teardown (camera.cpp:855; mypc.cpp:2291, 1256),
    /// so it follows the camera; Unknown1 = m_n54 = CameraMgr::GetModeType() when the record is made (mypc.cpp:2331; ridee.cpp:947), 2 for the field's CTargetCameraMode (camera.cpp:780-783, 2020). 17 is CHomeCameraMode
    /// (homecameramode.h:57), housing only (camera.cpp:1938-1941); Unknown1 must become 17 while the player is in a home (CHomeCameraMode) once live housing exists, we always send 2 now. Sniff check (kms2-sniffs, builds 2493-2549): Unknown1 = 17 appears only on map 62000000 (housing, builds 2495-2511 incl. the 2525 era files), every other map carries 2. Rotation2 = m_f58 = GetTargetYaw, x10; Unknown3 = m_fTime x1000 (the record's age, ms).
    /// m_vel3c is the move buffer's input (MoveInputDir), sent as it is: the receiver rotates it by the record's camera.
    /// </summary>
    Ms2.Net.Packets.StateSync LiveRecord(SyncRecord q)
    {
        float vx = q.Dir.X, vy = q.Dir.Y;
        var rec = new Ms2.Net.Packets.StateSync
        {
            State = StateId(q.State), SubState = q.Sub,
            Position = new Ms2.Net.Packets.Vector3S((short)q.Pos.X, (short)q.Pos.Y, (short)q.Pos.Z),
            Rotation = (short)(q.BodyYaw * 10f), Rotation2 = (short)(q.Yaw * 10f), SyncNumber = q.Cell,
            Speed = new Ms2.Net.Packets.Vector3S((short)q.CamEuler.X, (short)q.CamEuler.Y, (short)q.CamEuler.Z), /* camera field: see LiveRecord doc */ Unknown1 = 2,
            Unknown3 = (short)(q.Time * 1000f),   // WriteShortDivScaled(m_fTime, 1000), the record's age in ms (statesyncobject.cpp:126)
        };
        static int Axis(float v) => v == -1f ? 0 : v == 0f ? 1 : v == 1f ? 2 : -1;
        int ax = Axis(vx), ay = Axis(vy);
        if (ax >= 0 && ay >= 0) rec.Animation = (byte)(ax + 4 * (ay + 1));
        else { rec.Animation = 0x80; rec.Float1 = vx; rec.Float2 = vy; }
        return rec;
    }

    /// <summary>The fall-return entity of the first shape straight below the feet on groups 0x389, or -1.</summary>
    int GroundFallReturnEntity()
    {
        var from = GlobalPosition;
        return rayQ.Nearest(GetWorld3D().DirectSpaceState, from, from + Vector3.Down * MoveConfig.UnlimitedRay, GetRid(), GroundGroups) is { } hit
            ? FallReturnEntity(GodotObject.InstanceFromId(hit.ColliderId), hit.Shape) : -1;
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
        fallMarked = false;                                                         // m_b15d6 = false / the ridee's m_nDE0 = 0
        footFallMarked = false;                                                     // m_b15d6 = false also while riding (mypc.cpp:2745)
        // FUN_141676060(ZERO) (mypc.cpp:2749, pcbase.cpp:3644-3653) clears the fall start (forwarded to a ridee) before the
        // ridee's vfunc_59(1) (mypc.cpp:2774), so a mount's Idle sends no fall damage (SendFallDamage bails on ZERO,
        // pcbase.cpp:4352); on foot StateFall::vfunc_4 sets the fall start on entry, and the clear comes after it
        if (Mount != null)
        {
            fallStartY = float.NaN;
            Enter(MoveState.Idle);                                                  // spRidee->vfunc_59(1)
        }
        else
        {
            Enter(MoveState.Fall);                                                  // vfunc_59(5)
            fallStartY = float.NaN;                                                 // FUN_141676060(ZERO): m_posC10 = ZERO
        }
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

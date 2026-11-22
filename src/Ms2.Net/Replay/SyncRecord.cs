using System.Numerics;
using Ms2.Net.Packets;

namespace Ms2.Net.Replay;

/// <summary>
/// One state sync record as the client holds it after reading (StateSyncObject, statemanager.h:36-75): the wire record
/// (<see cref="StateSync"/>) read by StateSyncObject::vfunc_2 (@0x140835bd0, statesyncobject.cpp:153-183) and
/// FUN_1408365d0 (@0x1408365d0, statesyncobject.cpp:238-258), in the client's units. CSceneStateGame::OnUserSync
/// (@0x141712690, scenestategame.cpp:2461-2468) finds the PC by object id and FUN_1417127b0 (scenestategame.cpp:2486-2518)
/// makes one record per entry (CreateStateSyncObject of the leading state byte) and, for any PC but MyPC, gives it to
/// PCBase::vfunc_114 (PC::vfunc_114, pc.cpp:2570), which queues it in the PC's CStateManager (<see cref="RemotePcStates"/>).
/// The queue changes <see cref="Time"/> and <see cref="Fired"/> as it plays the record.
/// </summary>
public sealed class SyncRecord
{
    /// <summary>m_nState (+0x18): the state id (the leading byte FUN_1417127b0 reads).</summary>
    public int State;
    /// <summary>m_nSub (+0x1c): the state's own sub id.</summary>
    public int Sub;
    /// <summary>m_pos20 (+0x20): the feet position, MS2 axes (ReadVector3Short: three int16).</summary>
    public Vector3 Position;
    /// <summary>m_dir2c.z (+0x34): the body's Z rotation in degrees (ReadShortDiv10).</summary>
    public float RotationZ;
    /// <summary>m_vel3c (+0x3c): the move buffer's direction (GameObject m_pos98): x = 1 left / -1 right (moveLeft 0x30d43
    /// sets +1, Unk_PCMove::GetMoveDirection, unit_141682f80.cpp:319-323), y = 1 forward / -1 back; unpacked from the
    /// direction flags or the two floats.</summary>
    public Vector2 Move;
    /// <summary>m_pos48 (+0x48): the sender's camera Euler rotation in degrees (MyPC m_posBF4, mypc.cpp:2330; three int16).</summary>
    public Vector3 CameraEuler;
    /// <summary>m_n54 (+0x54): the sender's CameraMgr mode type (mypc.cpp:2331), 0..0x13.</summary>
    public int CameraMode;
    /// <summary>m_f58 (+0x58): the target yaw (PCBase m_f934, ReadShortDiv10).</summary>
    public float TargetYaw;
    /// <summary>m_fTime (+0x5c): the record's time in seconds (ReadShortDivScaled(1000)); the queue counts it down.</summary>
    public float Time;
    /// <summary>m_n64 (+0x64, optional bit 0): the skill id of a skill state.</summary>
    public int SkillId;
    /// <summary>m_str88 (+0x88, optional bit 4) and m_pos78: the object the sender stood on and the offset from it.</summary>
    public string MarkerName = "";
    public Vector3 MarkerOffset;
    /// <summary>m_n90 / m_str98 (+0x90 / +0x98, optional bit 5): the held actor's id and node (StateHoldData::vfunc_2).</summary>
    public int HoldTargetId;
    public string HoldNode = "";
    /// <summary>m_nA0 (+0xa0): the cube cell, 0x7fffffff when unset.</summary>
    public int CubeCell = 0x7fffffff;
    /// <summary>m_nA8 (+0xa8): set once the queue has played the record.</summary>
    public bool Fired;

    /// <summary>The client's record from a read wire record.</summary>
    public static SyncRecord From(StateSync s) => From(s, ProtocolProfile.Kms2525);

    /// <summary>On GMS2 a record read by <see cref="StateSync.ReadGms2"/> keeps the wire bits as they are: bit 1 is the marker (vec3 + name,
    /// Flag5Vec/Text), bit 2 the held actor (id + node, Flag2Value/Text); bit 4 (i32 + string, 40 recorded) has no known meaning here.</summary>
    public static SyncRecord From(StateSync s, ProtocolProfile profile)
    {
        var r = new SyncRecord
        {
            State = s.State, Sub = s.SubState,
            Position = new Vector3(s.Position.X, s.Position.Y, s.Position.Z),
            RotationZ = s.Rotation / 10.0f,                      // ReadShortDiv10: (float)int16 / 10 (packet.cpp:581)
            CameraEuler = new Vector3(s.Speed.X, s.Speed.Y, s.Speed.Z),
            CameraMode = s.Unknown1,
            TargetYaw = s.Rotation2 / 10.0f,
            Time = s.Unknown3 / 1000.0f,                        // ReadShortDivScaled(1000): (float)int16 / 1000 (packet.cpp:583)
            CubeCell = s.SyncNumber,
        };
        if (s.Flags.HasFlag(StateSync.Flag.Flag1)) r.SkillId = s.Flag1Value;
        if (profile.IsGms2)
        {
            if (s.Flags.HasFlag(StateSync.Flag.Flag2)) { r.MarkerName = s.Flag5Text ?? ""; r.MarkerOffset = s.Flag5Vec; }
            if (s.Flags.HasFlag(StateSync.Flag.Flag3)) { r.HoldTargetId = s.Flag2Value; r.HoldNode = s.Flag2Text ?? ""; }
        }
        else
        {
            if (s.Flags.HasFlag(StateSync.Flag.Flag5)) { r.MarkerName = s.Flag5Text ?? ""; r.MarkerOffset = s.Flag5Vec; }
            if (s.Flags.HasFlag(StateSync.Flag.Flag6)) { r.HoldTargetId = s.Flag6Value; r.HoldNode = s.Flag6Text ?? ""; }
        }
        r.Move = UnpackMove(s.Animation, s.Float1, s.Float2);
        return r;
    }

    /// <summary>
    /// FUN_1408365d0 (statesyncobject.cpp:243-257): with bit 0x80 the two floats are the direction; else bits 0-1 give x
    /// (0, 1, 2 = -1, 0, 1) and bits 2-3 give y (4, 8, 12 = -1, 0, 1). A value the switch does not name leaves the
    /// component at the record's initial 0.
    /// </summary>
    public static Vector2 UnpackMove(byte flags, float f1, float f2)
    {
        if ((flags & 0x80) == 0x80) return new Vector2(f1, f2);
        int nx = flags & 3, ny = flags & 0xc;
        float x = nx == 0 ? -1f : nx == 1 ? 0f : nx == 2 ? 1f : 0f;
        float y = ny == 4 ? -1f : ny == 8 ? 0f : ny == 0xc ? 1f : 0f;
        return new Vector2(x, y);
    }
}

// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// One movement record (the server's StateSync; client writer state.cpp / Unk_GuideRecord).
/// State, sub state, position (3 x i16), rotation (i16), animation byte (top bit: two floats follow), speed (3 x i16),
/// a byte (0..0x13 for a relayable record), rotation2 (i16), i16, flags (optional fields), sync number.
/// Four special states (58 couple emotion, 60 rock-paper-scissors, 69 couple dance, 71 wedding emotion) append a few
/// more fields after the sync number; their bytes are kept raw in SpecialTail (length from the layouts of the server
/// subclasses, the field meanings are unknown).
/// </summary>
public sealed class StateSync {
    [Flags] public enum Flag : byte { None = 0, Flag1 = 1, Flag2 = 2, Flag3 = 4, Animation = 8, Flag5 = 16, Flag6 = 32 }

    public byte State, SubState;
    public Vector3S Position; public short Rotation; public byte Animation; public float Float1, Float2;
    public Vector3S Speed; public byte Unknown1; public short Rotation2, Unknown3;
    public Flag Flags;
    public int Flag1Value, Flag2Value; public short Flag3Value;
    public string AnimationName;
    public Vector3 Flag5Vec; public string Flag5Text;
    public int Flag6Value; public string Flag6Text;
    public int SyncNumber;
    public byte[] SpecialTail = Array.Empty<byte>();

    public static StateSync Read(ByteReader r) {
        var s = new StateSync { State = r.ReadByte(), SubState = r.ReadByte(), Position = r.ReadVec3S(), Rotation = r.ReadShort(), Animation = r.ReadByte() };
        if ((s.Animation & 0x80) != 0) { s.Float1 = r.ReadFloat(); s.Float2 = r.ReadFloat(); }
        s.Speed = r.ReadVec3S(); s.Unknown1 = r.ReadByte(); s.Rotation2 = r.ReadShort(); s.Unknown3 = r.ReadShort();
        s.Flags = (Flag) r.ReadByte();
        if (s.Flags.HasFlag(Flag.Flag1)) s.Flag1Value = r.ReadInt();
        if (s.Flags.HasFlag(Flag.Flag2)) s.Flag2Value = r.ReadInt();
        if (s.Flags.HasFlag(Flag.Flag3)) s.Flag3Value = r.ReadShort();
        if (s.Flags.HasFlag(Flag.Animation)) s.AnimationName = r.ReadUnicodeString();
        if (s.Flags.HasFlag(Flag.Flag5)) { s.Flag5Vec = r.ReadVec3(); s.Flag5Text = r.ReadUnicodeString(); }
        if (s.Flags.HasFlag(Flag.Flag6)) { s.Flag6Value = r.ReadInt(); s.Flag6Text = r.ReadUnicodeString(); }
        s.SyncNumber = r.ReadInt();
        int start = r.Position;
        switch (s.State) {
            case 58: r.ReadInt(); r.ReadInt(); r.ReadByte(); r.ReadVec3(); r.ReadVec3(); break;
            case 60: r.ReadInt(); r.ReadByte(); r.ReadByte(); r.ReadUnicodeString(); break;
            case 69: r.ReadInt(); r.ReadBool(); r.ReadUnicodeString(); break;
            case 71: { bool b1 = r.ReadBool(), b2 = r.ReadBool(); if (b2) r.ReadInt(); if (b1) { r.ReadInt(); r.ReadBool(); } break; }
        }
        if (r.Position > start) s.SpecialTail = r.Buffer.AsSpan(start, r.Position - start).ToArray();
        return s;
    }

    public void Write(ByteWriter w) {
        w.WriteByte(State); w.WriteByte(SubState);
        w.WriteShort(Position.X); w.WriteShort(Position.Y); w.WriteShort(Position.Z);
        w.WriteShort(Rotation); w.WriteByte(Animation);
        if ((Animation & 0x80) != 0) { w.WriteFloat(Float1); w.WriteFloat(Float2); }
        w.WriteShort(Speed.X); w.WriteShort(Speed.Y); w.WriteShort(Speed.Z);
        w.WriteByte(Unknown1); w.WriteShort(Rotation2); w.WriteShort(Unknown3);
        w.WriteByte((byte) Flags);
        if (Flags.HasFlag(Flag.Flag1)) w.WriteInt(Flag1Value);
        if (Flags.HasFlag(Flag.Flag2)) w.WriteInt(Flag2Value);
        if (Flags.HasFlag(Flag.Flag3)) w.WriteShort(Flag3Value);
        if (Flags.HasFlag(Flag.Animation)) w.WriteUnicodeString(AnimationName ?? "");
        if (Flags.HasFlag(Flag.Flag5)) { w.WriteFloat(Flag5Vec.X); w.WriteFloat(Flag5Vec.Y); w.WriteFloat(Flag5Vec.Z); w.WriteUnicodeString(Flag5Text ?? ""); }
        if (Flags.HasFlag(Flag.Flag6)) { w.WriteInt(Flag6Value); w.WriteUnicodeString(Flag6Text ?? ""); }
        w.WriteInt(SyncNumber);
        if (SpecialTail.Length > 0) w.WriteBytes(SpecialTail);
    }
}

/// <summary>Inbound 0x1C: i32 object id, u8 count, count x StateSync.</summary>
public sealed class UserSyncIn {
    public int ObjectId; public List<StateSync> Records = new();
    public static UserSyncIn Read(ByteReader r) {
        var p = new UserSyncIn { ObjectId = r.ReadInt() };
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) p.Records.Add(StateSync.Read(r));
        r.ExpectEnd("UserSync");
        return p;
    }
}

/// <summary>Outbound 0x12 (StateLocalController::vfunc_0): u8 sync number, i32 server tick, i32 client tick, u8 count, count x (StateSync + i32 client tick).
/// With <see cref="Ride"/> it is RideSync 0x41 (StateRideeController::vfunc_0, state.cpp:537-562), the mount's records in the same layout.</summary>
public sealed class UserSyncOut : IOutPacket {
    public byte SyncNumber; public int ServerTick, ClientTick;
    public bool Ride;
    public List<(StateSync Record, int Tick)> Records = new();
    public Outbound Op => Ride ? Outbound.RideSync : Outbound.UserSync;
    public void Write(ByteWriter w) {
        w.WriteByte(SyncNumber); w.WriteInt(ServerTick); w.WriteInt(ClientTick); w.WriteByte((byte) Records.Count);
        foreach (var (rec, tick) in Records) { rec.Write(w); w.WriteInt(tick); }
    }
    public static UserSyncOut Read(ByteReader r) {
        var p = new UserSyncOut { SyncNumber = r.ReadByte(), ServerTick = r.ReadInt(), ClientTick = r.ReadInt() };
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) p.Records.Add((StateSync.Read(r), r.ReadInt()));
        r.ExpectEnd("UserSync (out)");
        return p;
    }
}

/// <summary>Outbound 0x80 (MyPC::SendStateChange): u8 mode (0 = jump, the position follows), u8 argument, i32 server tick, vec3 position when mode is 0.</summary>
public sealed record StateChange(byte Mode, byte Arg, int ServerTick, Vector3 Position) : IOutPacket {
    public Outbound Op => Outbound.State;
    public void Write(ByteWriter w) {
        w.WriteByte(Mode); w.WriteByte(Arg); w.WriteInt(ServerTick);
        if (Mode == 0) { w.WriteFloat(Position.X); w.WriteFloat(Position.Y); w.WriteFloat(Position.Z); }
    }
    public static StateChange Read(ByteReader r) {
        byte mode = r.ReadByte(), arg = r.ReadByte(); int tick = r.ReadInt();
        var p = new StateChange(mode, arg, tick, mode == 0 ? r.ReadVec3() : default);
        r.ExpectEnd("StateChange"); return p;
    }
}

/// <summary>
/// Inbound 0x5F (the server's PortalPacket.MoveByPortal, PortalPacket.cs:88-96): i32 object id, vec3 position, vec3 rotation, bool isPortal.
/// The server adds 25 to the position's z before sending. For the own object the client's CSceneStateGame::OnUserMoveByPortal
/// (scenestategame.cpp:5775-5786) warps through MyPC::FUN_141528e50 (mypc.cpp:2738-2791).
/// </summary>
public sealed record UserMoveByPortal(int ObjectId, Vector3 Position, Vector3 Rotation, bool IsPortal) {
    public static UserMoveByPortal Read(ByteReader r) {
        var p = new UserMoveByPortal(r.ReadInt(), r.ReadVec3(), r.ReadVec3(), r.ReadBool());
        r.ExpectEnd("UserMoveByPortal"); return p;
    }
}

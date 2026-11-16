// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// ---- outbound ----

/// <summary>Outbound 0x01: u32 version, u16 feature level, u16 region (CNetworkService::SendResponseVersion; region KR 0, JP 1, NA 2, CN 3).</summary>
public sealed record ResponseVersion(uint Version = 2525, ushort FeatureLevel = 1, ushort Region = 0) : IOutPacket {
    public Outbound Op => Outbound.ResponseVersion;
    public void Write(ByteWriter w) { w.WriteInt((int) Version); w.WriteShort((short) FeatureLevel); w.WriteShort((short) Region); }
    public static ResponseVersion Read(ByteReader r) { var p = new ResponseVersion(r.ReadUInt(), r.ReadUShort(), r.ReadUShort()); r.ExpectEnd("ResponseVersion"); return p; }
}

/// <summary>Outbound 0x03 (WriteLoginA): u8 type (1 server list, 2 char list), unicode user, unicode password, i16 login type, 16 byte machine id, i32, i32, narrow language.</summary>
public sealed record ResponseLogin(byte Type, string User, string Password, short LoginType, byte[] MachineId, int MachineValue, int ClientValue, string Language) : IOutPacket {
    public Outbound Op => Outbound.ResponseLogin;
    public void Write(ByteWriter w) {
        w.WriteByte(Type); w.WriteUnicodeString(User); w.WriteUnicodeString(Password); w.WriteShort(LoginType);
        if (MachineId.Length != 16) throw new ArgumentException("machine id must be 16 bytes");
        w.WriteBytes(MachineId); w.WriteInt(MachineValue); w.WriteInt(ClientValue); w.WriteString(Language);
    }
    public static ResponseLogin Read(ByteReader r) {
        var p = new ResponseLogin(r.ReadByte(), r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadShort(), r.ReadBytes(16), r.ReadInt(), r.ReadInt(), r.ReadString());
        r.ExpectEnd("ResponseLogin"); return p;
    }
}

/// <summary>
/// Outbound 0x03, variant B (WriteLoginB, commonfiles/classobject.cpp): u8 type, unicode user, unicode password (the publisher
/// token form), i16 login type, i32, 16 byte machine id, i32, i32. Recorded KMS traffic from build 2521 on uses this variant;
/// the server fork's ResponseLoginData reads variant A.
/// </summary>
public sealed record ResponseLoginB(byte Type, string User, string Password, short LoginType, int Value, byte[] MachineId, int MachineValue, int ClientValue) : IOutPacket {
    public Outbound Op => Outbound.ResponseLogin;
    public void Write(ByteWriter w) {
        w.WriteByte(Type); w.WriteUnicodeString(User); w.WriteUnicodeString(Password); w.WriteShort(LoginType); w.WriteInt(Value);
        if (MachineId.Length != 16) throw new ArgumentException("machine id must be 16 bytes");
        w.WriteBytes(MachineId); w.WriteInt(MachineValue); w.WriteInt(ClientValue);
    }
    public static ResponseLoginB Read(ByteReader r) {
        var p = new ResponseLoginB(r.ReadByte(), r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadShort(), r.ReadInt(), r.ReadBytes(16), r.ReadInt(), r.ReadInt());
        r.ExpectEnd("ResponseLoginB"); return p;
    }
}

/// <summary>Outbound 0x04, the game server's key check: i64 account id, u64 token (from LoginToGame), 16 byte machine id.</summary>
public sealed record ResponseKey(long AccountId, ulong Token, byte[] MachineId) : IOutPacket {
    public Outbound Op => Outbound.ResponseKey;
    public void Write(ByteWriter w) { w.WriteLong(AccountId); w.WriteLong((long) Token); w.WriteBytes(MachineId); }
    public static ResponseKey Read(ByteReader r) { var p = new ResponseKey(r.ReadLong(), r.ReadULong(), r.ReadBytes(16)); r.ExpectEnd("ResponseKey"); return p; }
}

/// <summary>Outbound 0x0A: the heartbeat tick echoed, then the client's elapsed time (same pair as the tick sync answer).</summary>
public sealed record ResponseHeartbeat(int ServerTick, int ClientTick) : IOutPacket {
    public Outbound Op => Outbound.ResponseHeartbeat;
    public void Write(ByteWriter w) { w.WriteInt(ServerTick); w.WriteInt(ClientTick); }
    public static ResponseHeartbeat Read(ByteReader r) { var p = new ResponseHeartbeat(r.ReadInt(), r.ReadInt()); r.ExpectEnd("ResponseHeartbeat"); return p; }
}

/// <summary>Outbound 0x0C: the server tick echoed, then the client's elapsed time.</summary>
public sealed record ResponseClientTickSync(int ServerTick, int ClientTick) : IOutPacket {
    public Outbound Op => Outbound.ResponseClientTickSync;
    public void Write(ByteWriter w) { w.WriteInt(ServerTick); w.WriteInt(ClientTick); }
    public static ResponseClientTickSync Read(ByteReader r) { var p = new ResponseClientTickSync(r.ReadInt(), r.ReadInt()); r.ExpectEnd("ResponseClientTickSync"); return p; }
}

// ---- inbound ----

/// <summary>Inbound 0x0A. The first fields are common to every outcome; the tail differs (restricted accounts carry ban times) and is kept raw.</summary>
public sealed record LoginResult(byte State, int Const1, string Message, long AccountId, long SyncTime, int TimeBase, byte TimeZone, byte BlockType, byte[] Tail) {
    public bool Success => State == 0;
    public static LoginResult Read(ByteReader r) {
        var p = new LoginResult(r.ReadByte(), r.ReadInt(), r.ReadUnicodeString(), r.ReadLong(), r.ReadLong(), r.ReadInt(), r.ReadByte(), r.ReadByte(), null);
        return p with { Tail = r.ReadBytes(r.Available) };
    }
}

/// <summary>Inbound 0x0D: u8 migration error, 0 is ok.</summary>
public sealed record MoveResult(byte Error) {
    public static MoveResult Read(ByteReader r) { var p = new MoveResult(r.ReadByte()); r.ExpectEnd("MoveResult"); return p; }
}

/// <summary>Inbound 0x12 and 0x13: one i32, to be echoed.</summary>
public sealed record TickRequest(int Tick) {
    public static TickRequest Read(ByteReader r) { var p = new TickRequest(r.ReadInt()); r.ExpectEnd("TickRequest"); return p; }
}

/// <summary>Inbound 0x0E. ok: 4 byte ip, u16 port, u64 token, i32 map id. Error 0x?? "rename required" and the others carry a text.</summary>
public sealed record LoginToGame(byte Error, byte[] Ip, ushort Port, ulong Token, int MapId, string Message) {
    public bool Ok => Error == 0;
    public System.Net.IPAddress Address => Ip == null ? null : new System.Net.IPAddress(Ip);
    public static LoginToGame Read(ByteReader r) {
        byte err = r.ReadByte();
        if (err != 0) return new LoginToGame(err, null, 0, 0, 0, r.Available >= 2 ? r.ReadUnicodeString() : "");
        var p = new LoginToGame(0, r.ReadBytes(4), r.ReadUShort(), r.ReadULong(), r.ReadInt(), "");
        r.ExpectEnd("LoginToGame"); return p;
    }
}

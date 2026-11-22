// Layouts adapted from the Maple2 server (AGPL-3.0, github.com/MS2Community/Maple2) and checked against the restored client and recorded KMS2 traffic.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// ---- outbound ----

/// <summary>
/// Outbound 0x01: u32 version, u16 feature level, u16 region (CNetworkService::SendResponseVersion; region KR 0, JP 1, NA 2, CN 3).
/// GMS2 (upstream ResponseVersionHandler.cs:11-13): u32 version, i16 47, and the locale as one byte, NA 2.
/// </summary>
public sealed record ResponseVersion(uint Version = 2525, ushort FeatureLevel = 1, ushort Region = 0) : IProfileOutPacket {
    public Outbound Op => Outbound.ResponseVersion;
    /// <summary>The answer a client of this profile gives the hello: KMS2 2525 / 1 / KR, GMS2 12 / 47 / NA.</summary>
    public static ResponseVersion For(ProtocolProfile profile) => profile.IsGms2 ? new ResponseVersion(profile.Version, 47, 2) : new ResponseVersion(profile.Version);
    public void Write(ByteWriter w) { w.WriteInt((int) Version); w.WriteShort((short) FeatureLevel); w.WriteShort((short) Region); }
    public void Write(ByteWriter w, ProtocolProfile profile) {
        if (!profile.IsGms2) { Write(w); return; }
        w.WriteInt((int) Version); w.WriteShort((short) FeatureLevel); w.WriteByte((byte) Region);
    }
    public static ResponseVersion Read(ByteReader r) { var p = new ResponseVersion(r.ReadUInt(), r.ReadUShort(), r.ReadUShort()); r.ExpectEnd("ResponseVersion"); return p; }
    public static ResponseVersion Read(ByteReader r, ProtocolProfile profile) {
        if (!profile.IsGms2) return Read(r);
        var p = new ResponseVersion(r.ReadUInt(), r.ReadUShort(), r.ReadByte()); r.ExpectEnd("ResponseVersion"); return p;
    }
}

/// <summary>Outbound 0x03, variant A (WriteLoginA), sent by the in-game login dialogs (UILoginDialog, UIServerListDialog) with the typed id and password and a region string: the variant for a private server. u8 type (1 server list, 2 char list), unicode user, unicode password, i16 login type, 16 byte machine id, i32, i32, narrow language.</summary>
public sealed record ResponseLogin(byte Type, string User, string Password, short LoginType, byte[] MachineId, int MachineValue, int ClientValue, string Language) : IProfileOutPacket {
    public Outbound Op => Outbound.ResponseLogin;
    /// <summary>GMS2 launcher ticket (the --ticket argument the Lith launcher hands the client). When set, the GMS2 form is the recorded official-client login.</summary>
    public string Ticket { get; init; }
    /// <summary>GMS2 recorded form: the i64 after the command byte (0 when unknown).</summary>
    public long AccountId { get; init; }
    /// <summary>GMS2 recorded form: the client's local IPv4 as 4 raw bytes in network order (null writes zeros).</summary>
    public byte[] LocalIp { get; init; }
    /// <summary>GMS2 recorded form: the per-login i32 before the language.</summary>
    public int Nonce { get; init; }
    /// <summary>GMS2 recorded form: the trailing byte (1 in the older recordings, 2 in the newer series; meaning unknown; the default is the newer 2).</summary>
    public byte Trailer { get; init; } = 2;

    /// <summary>
    /// GMS2 without a ticket (upstream LoginHandler.cs:28-34): u8 command, unicode user, unicode password, i16, 16 byte machine id.
    /// GMS2 with a ticket (the 62 byte login recorded from the official client): u8 command, i64, ascii token (u16 len), i16, i32 local ipv4,
    /// 16 byte machine id, i32 0, i32 nonce, ascii "EN" (u16 len), u8 trailer.
    /// </summary>
    public void Write(ByteWriter w, ProtocolProfile profile) {
        if (!profile.IsGms2) { Write(w); return; }
        if (MachineId.Length != 16) throw new ArgumentException("machine id must be 16 bytes");
        if (Ticket == null) { w.WriteByte(Type); w.WriteUnicodeString(User); w.WriteUnicodeString(Password); w.WriteShort(LoginType); w.WriteBytes(MachineId); return; }
        w.WriteByte(Type); w.WriteLong(AccountId); WriteAscii(w, Ticket); w.WriteShort(LoginType); WriteIp(w, LocalIp);
        w.WriteBytes(MachineId); w.WriteInt(0); w.WriteInt(Nonce); WriteAscii(w, Language ?? "EN"); w.WriteByte(Trailer);
    }
    static void WriteIp(ByteWriter w, byte[] ip) {
        if (ip != null && ip.Length != 4) throw new ArgumentException("local ip must be 4 bytes");
        w.WriteBytes(ip ?? new byte[4]);
    }
    static void WriteAscii(ByteWriter w, string s) {
        if (s.Length > short.MaxValue) throw new ArgumentException("ascii string too long");
        foreach (char c in s) if (c > 0x7F) throw new ArgumentException("string must be ASCII");
        w.WriteShort((short) s.Length); w.WriteBytes(System.Text.Encoding.ASCII.GetBytes(s));
    }
    static string ReadAscii(ByteReader r) => System.Text.Encoding.ASCII.GetString(r.ReadBytes(r.ReadUShort()));
    public static ResponseLogin Read(ByteReader r, ProtocolProfile profile) {
        if (!profile.IsGms2) return Read(r);
        var p = new ResponseLogin(r.ReadByte(), r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadShort(), r.ReadBytes(16), 0, 0, null);
        r.ExpectEnd("ResponseLogin"); return p;
    }
    /// <summary>Reads the recorded ticket form (what Write emits for GMS2 when Ticket is set).</summary>
    public static ResponseLogin ReadRecorded(ByteReader r) {
        byte type = r.ReadByte(); long acc = r.ReadLong(); string tok = ReadAscii(r); short lt = r.ReadShort(); var ip = r.ReadBytes(4); var id = r.ReadBytes(16);
        r.ReadInt(); int nonce = r.ReadInt(); string lang = ReadAscii(r); byte tr = r.ReadByte();
        r.ExpectEnd("ResponseLogin");
        return new ResponseLogin(type, null, null, lt, id, 0, 0, lang) { Ticket = tok, AccountId = acc, LocalIp = ip, Nonce = nonce, Trailer = tr };
    }
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
/// token form), i16 login type, i32, 16 byte machine id, i32, i32. Sent by CClientPlatformNexonKorea::vfunc_20 (user = passport string,
/// password = hardware id string): the Nexon Korea platform path, which is why live KMS recordings use it. The server fork reads variant A.
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

/// <summary>
/// Outbound 0x0D (SendOp::RequestQuit, commonfiles/opcodes.h:319): u8 mode.
/// Mode 0 = CNetworkService::SendRequestQuit (@0x141538820, networkservice.cpp:501-505, return to character select);
/// Mode 1 = CNetworkService::SendRequestQuitAndDisconnect (@0x1415388a0, networkservice.cpp:507-512, disconnect and exit).
/// </summary>
public sealed record RequestQuit(byte Mode) : IOutPacket {
    public const byte SelectCharacter = 0;
    public const byte Disconnect = 1;
    public Outbound Op => Outbound.RequestQuit;
    public void Write(ByteWriter w) => w.WriteByte(Mode);
    public static RequestQuit Read(ByteReader r) { var p = new RequestQuit(r.ReadByte()); r.ExpectEnd("RequestQuit"); return p; }
}

// ---- inbound ----

/// <summary>Inbound 0x0A. GMS2 sends the same head (upstream LoginResultPacket.cs:9-24: u8 state, i32, string, i64 account, i64 sync time, i32 time base, u8 zone, u8 block type), so one read serves both. The first fields are common to every outcome; the tail differs (restricted accounts carry ban times) and is kept raw.</summary>
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

/// <summary>
/// Inbound 0x0F (RecvOp::GameToLogin, commonfiles/opcodes.h:23; CNetworkService::OnGameToLogin @0x14153bc00,
/// networkservice.cpp:928-940): u8 result; on 0, u32 ip (4 bytes), u16 port, i32 token1, i32 token2. An error reads
/// nothing more (:939).
/// </summary>
public sealed record GameToLogin(byte Error, byte[] Ip, ushort Port, int Token1, int Token2) {
    public bool Ok => Error == 0;
    public System.Net.IPAddress Address => Ip == null ? null : new System.Net.IPAddress(Ip);
    public ulong Token => (uint) Token1 | (ulong) (uint) Token2 << 32;
    public static GameToLogin Read(ByteReader r) {
        byte err = r.ReadByte();
        if (err != 0) return new GameToLogin(err, null, 0, 0, 0);
        var p = new GameToLogin(0, r.ReadBytes(4), r.ReadUShort(), r.ReadInt(), r.ReadInt());
        r.ExpectEnd("GameToLogin"); return p;
    }
}

/// <summary>
/// Inbound 0x10, the move to another game server (CNetworkService::OnGameToGame @0x14153be30, networkservice.cpp:942): u8
/// result; on 0, i32 token1, i32 token2, u32 ip, u16 port, i32 field, u8 kind. The client stores them (DAT_143ab284c/2850 the
/// tokens, DAT_143ab2a48 the address as FUN_14028bd10 makes it, the same 4 bytes as LoginToGame's, DAT_143ab2848 the port,
/// DAT_143ab2890 the field, DAT_143ab284a the kind, which only RestoreServerAddress reads, networkservice.cpp:585: 0x11 lets a
/// failed reconnect go back to the saved address). An error returns with nothing more read (networkservice.cpp:955).
/// </summary>
public sealed record GameToGame(byte Error, int Token1, int Token2, byte[] Ip, ushort Port, int FieldId, byte Kind) {
    public bool Ok => Error == 0;
    public System.Net.IPAddress Address => Ip == null ? null : new System.Net.IPAddress(Ip);
    /// <summary>The two tokens as ResponseKey carries them (WriteLoginEnter: token1 is the low half).</summary>
    public ulong Token => (uint) Token1 | (ulong) (uint) Token2 << 32;
    public static GameToGame Read(ByteReader r) {
        byte err = r.ReadByte();
        if (err != 0) return new GameToGame(err, 0, 0, null, 0, 0, 0);
        var p = new GameToGame(0, r.ReadInt(), r.ReadInt(), r.ReadBytes(4), r.ReadUShort(), r.ReadInt(), r.ReadByte());
        r.ExpectEnd("GameToGame"); return p;
    }
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

/// <summary>
/// Inbound 0x06 (CNetworkService::OnPingRequest): the i32 is how many of the client's packets the server has seen
/// (it lets the client drop acknowledged ones from its resend buffer). The answer is a Pong.
/// </summary>
public sealed record Ping(int Sequence) {
    public static Ping Read(ByteReader r) { var p = new Ping(r.ReadInt()); r.ExpectEnd("Ping"); return p; }
}

/// <summary>Outbound 0x06: the number of frames the client has received on this connection, the handshake frame included (CGameSocket m_sequence).</summary>
public sealed record Pong(int ReceivedCount) : IOutPacket {
    public Outbound Op => Outbound.Pong;
    public void Write(ByteWriter w) => w.WriteInt(ReceivedCount);
    public static Pong Read(ByteReader r) { var p = new Pong(r.ReadInt()); r.ExpectEnd("Pong"); return p; }
}

/// <summary>Outbound 0x0B (CNetworkService::SendTimeSyncRequest): i32 sequence.</summary>
public sealed record RequestTimeSync(int Sequence) : IOutPacket {
    public Outbound Op => Outbound.RequestTimeSync;
    public void Write(ByteWriter w) => w.WriteInt(Sequence);
    public static RequestTimeSync Read(ByteReader r) { var p = new RequestTimeSync(r.ReadInt()); r.ExpectEnd("RequestTimeSync"); return p; }
}

/// <summary>Outbound 0x14: empty (UICharacterListDialog asks for the server list).</summary>
public sealed record RequestServerList : IOutPacket {
    public Outbound Op => Outbound.RequestServerList;
    public void Write(ByteWriter w) { }
}

public sealed record ServerListChannelEntry(int ServerId, string Name, byte Characters, (string Address, ushort Port)[] Addresses, int TrafficPercent, ushort[] Channels);

/// <summary>Inbound 0x0B (UIServerListDialog::ReceiveServerList): narrow string (server type name), u8 count, per server: i32 id, name, u8 characters, u16 n x (address, u16 port), i32 traffic percent, u16 n x u16 channel.</summary>
public sealed record ServerList(string EnvName, List<ServerListChannelEntry> Servers) {
    /// <summary>
    /// CSceneStateLogin::OnPacket (scenestatelogin.cpp:117-130) reads the same packet as a string and a u8 result: above 0 (the count of servers) it moves on to server enter
    /// (state 0x620000d); at 0 it logs and shows message box 0x20e. So an empty list is a refusal.
    /// </summary>
    public bool Usable => Servers.Count > 0;
    /// <summary>
    /// GMS2 (upstream ServerListPacket.cs:20-40): u8 command (0 error, 1 load), then for load i32 server id, unicode name, u8 4, u16 n x
    /// (unicode address, u16 port), i32 100, u16 n x u16 channel. One server, no type name. An error command is an empty list.
    /// </summary>
    public static ServerList Read(ByteReader r, ProtocolProfile profile) {
        if (!profile.IsGms2) return Read(r);
        byte command = r.ReadByte();
        if (command == 0) { r.ExpectEnd("ServerList"); return new ServerList("", new List<ServerListChannelEntry>()); }
        if (command != 1) throw new InvalidDataException($"ServerList command {command}");
        int id = r.ReadInt(); string name = r.ReadUnicodeString(); r.ReadByte();
        var addr = new (string, ushort)[r.ReadUShort()];
        for (int j = 0; j < addr.Length; j++) addr[j] = (r.ReadUnicodeString(), r.ReadUShort());
        int traffic = r.ReadInt();
        var ch = new ushort[r.ReadUShort()];
        for (int j = 0; j < ch.Length; j++) ch[j] = r.ReadUShort();
        r.ExpectEnd("ServerList");
        return new ServerList("", new List<ServerListChannelEntry> { new(id, name, 0, addr, traffic, ch) });
    }

    public static ServerList Read(ByteReader r) {
        string env = r.ReadString(); int n = r.ReadByte();
        var list = new List<ServerListChannelEntry>();
        for (int i = 0; i < n; i++) {
            int id = r.ReadInt(); string name = r.ReadUnicodeString(); byte chars = r.ReadByte();
            var addr = new (string, ushort)[r.ReadUShort()];
            for (int j = 0; j < addr.Length; j++) addr[j] = (r.ReadUnicodeString(), r.ReadUShort());
            int traffic = r.ReadInt();
            var ch = new ushort[r.ReadUShort()];
            for (int j = 0; j < ch.Length; j++) ch[j] = r.ReadUShort();
            list.Add(new ServerListChannelEntry(id, name, chars, addr, traffic, ch));
        }
        r.ExpectEnd("ServerList");
        return new ServerList(env, list);
    }
}

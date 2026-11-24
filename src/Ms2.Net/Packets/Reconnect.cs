using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// ReconnectionOptions (clientreconnectionstructs.cpp:Read/Write @0x140208500/0x1402084a0): eleven i32. The client reads the ones it
/// uses by index (reconnectionmanagercli.cpp): 0 enabled, 1 resend buffer limit, 2 give-up seconds, 7 attempts, 8 retry delay seconds, 9 hold sends while offline.
/// The server sends them in the Gvg packet, kind 43 (networkservice.cpp:820).
/// </summary>
public sealed class ReconnectionOptions {
    public const int Count = 11;
    public const int Enabled = 0, BufferLimit = 1, GiveUpSeconds = 2, MaxAttempts = 7, RetryDelaySeconds = 8, HoldSends = 9;
    readonly int[] values = new int[Count];

    /// <summary>ReconnectionOptions::GetValue: the client asserts the index and returns 0 outside 0..10.</summary>
    public int this[int index] { get => (uint) index < Count ? values[index] : 0; set { if ((uint) index < Count) values[index] = value; } }

    public void Assign(ReconnectionOptions other) { Array.Copy(other.values, values, Count); }

    public static ReconnectionOptions Read(ByteReader r) {
        var o = new ReconnectionOptions();
        for (int i = 0; i < Count; i++) o.values[i] = r.ReadInt();
        return o;
    }

    public void Write(ByteWriter w) { for (int i = 0; i < Count; i++) w.WriteInt(values[i]); }
}

/// <summary>ReconnectionContext (clientreconnectionstructs.cpp @0x140208680/0x140208610): i32 mode (1 = the server accepts a reconnect), narrow host string, i32 port.</summary>
public sealed record ReconnectionContext(int Mode, string Host, int Port) {
    public static ReconnectionContext Read(ByteReader r) => new(r.ReadInt(), r.ReadString(), r.ReadInt());
    public void Write(ByteWriter w) { w.WriteInt(Mode); w.WriteString(Host); w.WriteInt(Port); }
}

/// <summary>
/// Outbound 0x05 (CReconnectionManagerCli::SendReconnectRequest @0x140aad7f0): u8 1, i64 account id, i64 the second session id (DAT_143ab2828), i32 attempt number, i32 the
/// socket sequence at the start of the reconnect, unicode machine id text, then the context as the server last sent it.
/// </summary>
public sealed record ReconnectRequest(long Account, long Session, int Attempt, int SocketSequence, string MachineId, ReconnectionContext Context) : IOutPacket {
    public Outbound Op => Outbound.Reconnect;
    public void Write(ByteWriter w) {
        w.WriteByte(1); w.WriteLong(Account); w.WriteLong(Session); w.WriteInt(Attempt); w.WriteInt(SocketSequence);
        w.WriteUnicodeString(MachineId); Context.Write(w);
    }
}

/// <summary>Inbound 0x05 (CReconnectionManagerCli::OnReconnectPacket @0x140aada20): u8 kind. 0 = the server's context, 2 = the result of a reconnect, 3 = the session is over.</summary>
public sealed record ReconnectNotice(byte Kind, ReconnectionContext Context, int Result) {
    public const byte KindContext = 0, KindResult = 2, KindEnded = 3;
    public static ReconnectNotice Read(ByteReader r) {
        byte kind = r.ReadByte();
        ReconnectNotice n = kind switch {
            KindContext => new(kind, ReconnectionContext.Read(r), 0),
            KindResult => new(kind, null, r.ReadInt()),
            _ => new(kind, null, 0),    // 3 has no body; an unknown kind is ignored by the client's switch
        };
        r.ExpectEnd("Reconnect");
        return n;
    }
}

/// <summary>
/// Unk_MachineId::ToString (@0x14067c400, unit_14067c020_machineidcodec.cpp:139-149): the 16-byte machine id as uppercase
/// hex pairs joined by '-', sent in <see cref="ReconnectRequest.MachineId"/> (reconnectionmanagercli.cpp:244).
/// </summary>
public static class MachineIdText {
    public static string Format(ReadOnlySpan<byte> id) {
        if (id.Length != 16) throw new ArgumentException("machine id must be 16 bytes", nameof(id));
        var src = id.ToArray();
        return string.Create(47, src, static (dst, bytes) => {
            const string hex = "0123456789ABCDEF";
            for (int i = 0; i < 16; i++) {
                int p = i * 3;
                byte b = bytes[i];
                dst[p] = hex[b >> 4];
                dst[p + 1] = hex[b & 0xF];
                if (i < 15) dst[p + 2] = '-';
            }
        });
    }
}

/// <summary>Inbound 0x07 (CGameSocket::ProcessPacket gamesocket.cpp:320; OnReconnectUnk @0x14153b2d0, networkservice.cpp:882): one i32 that restores CGameSocket::m_sequence.</summary>
public sealed record ReconnectUnk(int ServerSequence) {
    public int Unused => ServerSequence;
    public static ReconnectUnk Read(ByteReader r) { var p = new ReconnectUnk(r.ReadInt()); r.ExpectEnd("ReconnectUnk"); return p; }
}

/// <summary>Outbound 0x07 (RetransmitClientPackets, clientreconnectionstructs.cpp:92): i32 the last sequence the server saw. Sent first, before the resent packets.</summary>
public sealed record ReconnectAck(int Sequence) : IOutPacket {
    public Outbound Op => Outbound.ReconnectUnk;
    public void Write(ByteWriter w) => w.WriteInt(Sequence);
}

/// <summary>Inbound 0x08 (CGameSocket::ProcessPacket gamesocket.cpp:321; OnRetransmitRequest @0x14153b310, networkservice.cpp:884): i32 restoring CGameSocket::m_sequence, i32 the last client sequence the server saw.</summary>
public sealed record RetransmitRequest(int ServerSequence, int Sequence) {
    public int Unused => ServerSequence;
    public static RetransmitRequest Read(ByteReader r) { var p = new RetransmitRequest(r.ReadInt(), r.ReadInt()); r.ExpectEnd("FinalizeReconnect"); return p; }
}

/// <summary>Outbound 0x08 (the end of RetransmitClientPackets, :104): i32 the client's current send sequence.</summary>
public sealed record FinalizeReconnect(int Sequence) : IOutPacket {
    public Outbound Op => Outbound.FinalizeReconnect;
    public void Write(ByteWriter w) => w.WriteInt(Sequence);
}

/// <summary>One entry decoded by <see cref="GvgOptions.ReadAll"/> (CNetworkService::OnGvg @0x14153a8f0, networkservice.cpp:811-832).</summary>
public abstract record GvgEntry(int Kind) {
    /// <summary>Kind 1: launch flag bool (networkservice.cpp:816).</summary>
    public sealed record LaunchFlag(bool Enabled) : GvgEntry(1);
    /// <summary>Kind 7: CContentShutdown::Decode (@0x1402c4c00, contentshutdown.cpp:26-30): u16 count, count x u16 content ids.</summary>
    public sealed record ContentShutdown(ushort[] DisabledContents) : GvgEntry(7);
    /// <summary>Kind 25: four i32 socket options (noDelay, option5, receiveBuffer, sendBuffer, networkservice.cpp:818).</summary>
    public sealed record SocketOptions(int NoDelay, int Option5, int ReceiveBuffer, int SendBuffer) : GvgEntry(25);
    /// <summary>Kind 34: i16, i32, i32 (networkservice.cpp:829).</summary>
    public sealed record GroupState(short Value0, int Value1, int Value2) : GvgEntry(34);
    /// <summary>Kind 42: DungeonRecordManagerCli::OnRates (@0x140b22100, dungeonrecordcli.cpp:466-470): two floats.</summary>
    public sealed record DungeonRates(float RateA, float RateB) : GvgEntry(42);
    /// <summary>Kind 43: ReconnectionOptions (networkservice.cpp:820-828).</summary>
    public sealed record Reconnection(ReconnectionOptions Options) : GvgEntry(GvgOptions.KindReconnectionOptions);
}

/// <summary>
/// Inbound 0x6D (CNetworkService::OnGvg @0x14153a8f0, networkservice.cpp:811-832): i32 count, then per entry i32 kind and its body
/// across all six client kinds: 1 (bool), 7 (u16 count + count x u16, CContentShutdown::Decode), 25 (four i32), 34 (i16, i32, i32),
/// 42 (two floats, DungeonRecordManagerCli::OnRates) and 43 (ReconnectionOptions).
/// </summary>
public static class GvgOptions {
    public const int KindLaunchFlag = 1, KindContentShutdown = 7, KindSocketOptions = 25,
        KindGroupState = 34, KindDungeonRates = 42, KindReconnectionOptions = 43;

    /// <summary>Reads every entry of an OnGvg packet when all kinds are known, verifying no trailing bytes remain.</summary>
    public static List<GvgEntry> ReadAll(ByteReader r) {
        int count = r.ReadInt();
        var list = new List<GvgEntry>(count);
        for (int i = 0; i < count; i++) {
            int kind = r.ReadInt();
            switch (kind) {
                case KindLaunchFlag: list.Add(new GvgEntry.LaunchFlag(r.ReadBool())); break;
                case KindContentShutdown: {
                    int n = r.ReadUShort();
                    var flags = new ushort[n];
                    for (int k = 0; k < n; k++) flags[k] = r.ReadUShort();
                    list.Add(new GvgEntry.ContentShutdown(flags));
                    break;
                }
                case KindSocketOptions: list.Add(new GvgEntry.SocketOptions(r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt())); break;
                case KindGroupState: list.Add(new GvgEntry.GroupState(r.ReadShort(), r.ReadInt(), r.ReadInt())); break;
                case KindDungeonRates: list.Add(new GvgEntry.DungeonRates(r.ReadFloat(), r.ReadFloat())); break;
                case KindReconnectionOptions: list.Add(new GvgEntry.Reconnection(ReconnectionOptions.Read(r))); break;
                default: throw new InvalidDataException($"Unknown Gvg group kind {kind}");
            }
        }
        r.ExpectEnd("Gvg");
        return list;
    }

    /// <summary>The options of the first kind 43 entry, or null when the packet has none (or hits an unknown kind).</summary>
    public static ReconnectionOptions FindReconnection(ByteReader r) {
        int count = r.ReadInt();
        for (int i = 0; i < count; i++) {
            switch (r.ReadInt()) {
                case KindLaunchFlag: r.ReadByte(); break;
                case KindContentShutdown: {
                    int n = r.ReadUShort();
                    for (int k = 0; k < n; k++) r.ReadUShort();
                    break;
                }
                case KindSocketOptions: for (int k = 0; k < 4; k++) r.ReadInt(); break;
                case KindGroupState: r.ReadShort(); r.ReadInt(); r.ReadInt(); break;
                case KindDungeonRates: r.ReadFloat(); r.ReadFloat(); break;
                case KindReconnectionOptions: return ReconnectionOptions.Read(r);
                default: return null;
            }
        }
        return null;
    }
}

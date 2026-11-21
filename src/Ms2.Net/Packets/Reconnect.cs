using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// ReconnectionOptions (clientreconnectionstructs.cpp:Read/Write @0x140208500/0x1402084a0): eleven i32. The client reads the ones it
/// uses by index (reconnectionmanagercli.cpp): 0 enabled, 1 resend buffer limit, 2 give-up seconds, 7 attempts, 8 retry delay seconds, 9 hold sends while offline.
/// The server sends them in the Gvg packet, kind 43 (networkservice.cpp:821).
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

/// <summary>Inbound 0x07 (OnReconnectUnk @0x14153b2d0): one i32 the client reads and drops.</summary>
public sealed record ReconnectUnk(int Unused) {
    public static ReconnectUnk Read(ByteReader r) { var p = new ReconnectUnk(r.ReadInt()); r.ExpectEnd("ReconnectUnk"); return p; }
}

/// <summary>Outbound 0x07 (RetransmitClientPackets, clientreconnectionstructs.cpp:92): i32 the last sequence the server saw. Sent first, before the resent packets.</summary>
public sealed record ReconnectAck(int Sequence) : IOutPacket {
    public Outbound Op => Outbound.ReconnectUnk;
    public void Write(ByteWriter w) => w.WriteInt(Sequence);
}

/// <summary>Inbound 0x08 (OnRetransmitRequest @0x14153b310): i32 dropped, i32 the last client sequence the server saw. The client answers by resending what is after it.</summary>
public sealed record RetransmitRequest(int Unused, int Sequence) {
    public static RetransmitRequest Read(ByteReader r) { var p = new RetransmitRequest(r.ReadInt(), r.ReadInt()); r.ExpectEnd("FinalizeReconnect"); return p; }
}

/// <summary>Outbound 0x08 (the end of RetransmitClientPackets, :104): i32 the client's current send sequence.</summary>
public sealed record FinalizeReconnect(int Sequence) : IOutPacket {
    public Outbound Op => Outbound.FinalizeReconnect;
    public void Write(ByteWriter w) => w.WriteInt(Sequence);
}

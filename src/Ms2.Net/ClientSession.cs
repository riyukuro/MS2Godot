using Maple2.PacketLib.Tools;
using System.Diagnostics;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Net;

/// <summary>
/// Read loop over a MapleChannel. It answers the keepalive packets itself (ping 0x06, heartbeat 0x12 and client tick sync 0x13), on the
/// network thread, so a slow main thread or a long scene load cannot get the session cut. Everything else is handed to
/// <see cref="PacketReceived"/> in order; unknown opcodes come through too, counted in <see cref="OpcodeCounts"/>.
/// It does not open sockets: pass it a channel over any stream.
/// </summary>
public sealed class ClientSession {
    readonly MapleChannel channel;
    readonly Stopwatch clock = Stopwatch.StartNew();
    public readonly Dictionary<ushort, int> OpcodeCounts = new();

    /// <summary>Runs on the read loop's thread. Marshal to the game thread there (a ConcurrentQueue drained in the frame update).</summary>
    public event Action<RawPacket> PacketReceived;

    /// <summary>Frames received on this connection, the handshake included: what the client answers to a Ping (CGameSocket m_sequence).</summary>
    public int ReceivedCount { get; private set; }

    public MapleChannel Channel => channel;
    /// <summary>CMainSystem::GetElapsedTime: milliseconds since this client started (the second tick of UserSync).</summary>
    public int ElapsedMs => (int) clock.ElapsedMilliseconds;

    int serverTickBase, localTickBase, timeSyncSeq;
    /// <summary>True once the server has sent a time sync (SetServerTickBase has been called).</summary>
    public bool HasServerTick { get; private set; }

    /// <summary>
    /// CMainSystem::GetServerTick (mainsystem.cpp:312): local tick - m_dwLocalTickBase + m_iServerTickBase. The base is set by
    /// OnTimeSyncResponse (networkservice.cpp, SetServerTickBase mainsystem.cpp:350) from the tick the server sends
    /// (kms2525 TimeSyncPacket.cs writes Environment.TickCount). Before the first sync it is the elapsed time.
    /// </summary>
    public int ServerTick => HasServerTick ? ElapsedMs - localTickBase + serverTickBase : ElapsedMs;

    /// <summary>Applies a time sync command (ResponseTimeSync body); returns true when the client must answer with a RequestTimeSync.</summary>
    public bool ApplyTimeSync(ByteReader r) {
        byte cmd = r.ReadByte();
        if (cmd == 2) return true;
        if (cmd > 1) return false;
        int time = r.ReadInt();
        serverTickBase = time; localTickBase = ElapsedMs; HasServerTick = true;
        return cmd == 1;
    }

    public ClientSession(MapleChannel channel) { this.channel = channel; }

    /// <summary>Reads the server hello, answers it with ResponseVersion, then loops until the stream ends or the token is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct = default, ResponseVersion hello = null) {
        await channel.ReadHandshakeAsync(ct);
        ReceivedCount = 1;
        await channel.SendAsync(hello ?? new ResponseVersion(Handshake.ClientVersion(channel.Profile)), ct);
        while (!ct.IsCancellationRequested) {
            var p = await channel.ReadPacketAsync(ct);
            ReceivedCount++;
            lock (OpcodeCounts) OpcodeCounts[p.Opcode] = OpcodeCounts.GetValueOrDefault(p.Opcode) + 1;
            if (channel.Profile.TryInbound(p.Opcode, out var op)) switch (op) {
                case Inbound.Ping:
                    Ping.Read(p.Reader());
                    await channel.SendAsync(new Pong(ReceivedCount), ct);
                    break;
                case Inbound.RequestHeartbeat:
                    await channel.SendAsync(new ResponseHeartbeat(TickRequest.Read(p.Reader()).Tick, ElapsedMs), ct);
                    break;
                case Inbound.RequestClientTickSync:
                    await channel.SendAsync(new ResponseClientTickSync(TickRequest.Read(p.Reader()).Tick, ElapsedMs), ct);
                    break;
                case Inbound.ResponseTimeSync:
                    if (ApplyTimeSync(p.Reader())) await channel.SendAsync(new RequestTimeSync(++timeSyncSeq), ct);
                    break;
            }
            PacketReceived?.Invoke(p);
        }
    }
}

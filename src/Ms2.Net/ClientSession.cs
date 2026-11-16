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
    public int ElapsedMs => (int) clock.ElapsedMilliseconds;

    public ClientSession(MapleChannel channel) { this.channel = channel; }

    /// <summary>Reads the server hello, answers it with ResponseVersion, then loops until the stream ends or the token is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct = default, ResponseVersion hello = null) {
        await channel.ReadHandshakeAsync(ct);
        ReceivedCount = 1;
        await channel.SendAsync(hello ?? new ResponseVersion(), ct);
        while (!ct.IsCancellationRequested) {
            var p = await channel.ReadPacketAsync(ct);
            ReceivedCount++;
            lock (OpcodeCounts) OpcodeCounts[p.Opcode] = OpcodeCounts.GetValueOrDefault(p.Opcode) + 1;
            switch ((Inbound) p.Opcode) {
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
            }
            PacketReceived?.Invoke(p);
        }
    }
}

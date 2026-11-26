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
    /// <summary>Where the answers the client sends through SendPacket go (FUN_140710590): the transport's hold path, so they are held during a reconnect. Null = straight to the channel (Pong and ResponseVersion always are).</summary>
    public Func<IOutPacket, CancellationToken, Task> Sender { get; set; }
    Task Send(IOutPacket p, CancellationToken ct) => Sender != null ? Sender(p, ct) : channel.SendAsync(p, ct);
    /// <summary>The reconnection manager (TSingleton CReconnectionManagerCli) this connection reports to; null where no reconnect is wanted (login connections, tests).</summary>
    public ReconnectionManager Manager { get; set; }
    /// <summary>CMainSystem::GetElapsedTime: milliseconds since this client started (the second tick of UserSync).</summary>
    public int ElapsedMs => (int) clock.ElapsedMilliseconds;

    int serverTickBase, localTickBase, timeSyncSeq;
    /// <summary>True once the server has sent a time sync (SetServerTickBase has been called).</summary>
    volatile bool hasServerTick;   // written last by the net thread, after the two bases
    public bool HasServerTick => hasServerTick;

    /// <summary>
    /// CMainSystem::GetServerTick (mainsystem.cpp:312): local tick - m_dwLocalTickBase + m_iServerTickBase. The base is set by
    /// OnTimeSyncResponse (networkservice.cpp, SetServerTickBase mainsystem.cpp:350) from the tick the server sends
    /// (kms2525 TimeSyncPacket.cs writes Environment.TickCount). Before the first sync it is the elapsed time.
    /// </summary>
    public int ServerTick => HasServerTick ? ElapsedMs - localTickBase + serverTickBase : ElapsedMs;

    /// <summary>m_tServerTimeBase - m_tLocalTimeBase: both are time() in the CMainSystem ctor (mainsystem.cpp:167-170), so 0 until a sync.</summary>
    long serverTimeOffset;

    /// <summary>
    /// CMainSystem::GetServerTime (mainsystem.cpp:390): time() - m_tLocalTimeBase + m_tServerTimeBase, the server's clock in
    /// seconds since 1970. SyncServerTime (mainsystem.cpp:398) sets the bases from the server time of a time sync of kind 0
    /// or 1 and the local time() then (networkservice.cpp:767, 777). Quest periods, schedules and resets read it.
    /// </summary>
    public long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Interlocked.Read(ref serverTimeOffset);

    /// <summary>
    /// m_tA0 (CMainSystem::FUN_141473530, mainsystem.cpp:360): the time a command 3 time sync sets. CMainSystem::FUN_141473560 counts down
    /// from it to the next day start. 0 until the server sends one.
    /// </summary>
    public long SetTime => Interlocked.Read(ref setTime);
    long setTime;

    /// <summary>
    /// The version time a command 4 time sync carried (UIUpperHudDialog::SetVersionText, networkservice.cpp:783). 0 until one arrives.
    /// STILL-OPEN: the version text itself is not drawn.
    /// </summary>
    public long VersionTime => Interlocked.Read(ref versionTime);
    long versionTime;

    /// <summary>
    /// CNetworkService::m_latency: the time sync round trips. Its average is the m_i74 that SetServerTickBase keeps
    /// (CMainSystem::FUN_141473460), the frame rate dialog's number in parentheses; 0 before the first answer.
    /// </summary>
    public readonly TimeSyncLatency Latency = new();

    /// <summary>SendTimeSyncRequest (networkservice.cpp:750): the next sequence, noted in m_latency.Sent, then RequestTimeSync.</summary>
    public RequestTimeSync NextTimeSyncRequest() {
        int seq = ++timeSyncSeq;
        Latency.Sent((uint)seq, (uint)ElapsedMs);
        return new RequestTimeSync(seq);
    }

    /// <summary>
    /// Applies a time sync command (ResponseTimeSync body, CNetworkService::OnTimeSyncResponse @0x14153a380, networkservice.cpp:756);
    /// returns true when the client must answer with a RequestTimeSync. The wire is the same on KMS and GMS2.
    /// Command 0: i32 tick, i64 server time, i32 zone, u8 flag, i32 latency sequence; sets the bases, no answer.
    /// Command 1: the same without the sequence; sets the bases and answers. Command 2: reads nothing (the GMS2 server appends a
    /// 17-byte body the client ignores) and answers. Commands 3 and 4: an i64 time.
    /// Command 0 adds the answered request's round trip to <see cref="Latency"/> (m_latency.Received); command 1 resets it.
    /// </summary>
    public bool ApplyTimeSync(ByteReader r) {
        byte cmd = r.ReadByte();
        switch (cmd) {
            case 0:
            case 1: {
                int time = r.ReadInt();
                long serverTime = r.ReadLong();
                int zone = r.ReadInt(); byte flag = r.ReadByte();
                if (cmd == 0) Latency.Received((uint)r.ReadInt(), (uint)ElapsedMs);   // the request this answers: its round trip
                else Latency.Reset();                     // SetServerTickBase(time, 0, 0, 0)
                Interlocked.Exchange(ref serverTimeOffset, serverTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds());   // SyncServerTime
                MsTime.SetTimeZone(zone, flag);           // CMsTime::SetTimeZone(zone, flag) (networkservice.cpp:756-779)
                serverTickBase = time; localTickBase = ElapsedMs; hasServerTick = true;   // volatile write publishes the bases
                return cmd == 1;
            }
            case 2:
                r.ReadBytes(r.Available);                 // the client reads nothing here; the appended body is dropped
                return true;
            case 3:
                Interlocked.Exchange(ref setTime, r.ReadLong());
                return false;
            case 4:
                Interlocked.Exchange(ref versionTime, r.ReadLong());
                return false;
            default:
                return false;
        }
    }

    public ClientSession(MapleChannel channel) {
        this.channel = channel;
        channel.SequenceReset += () => ReceivedCount = 0;   // CGameSocket::FUN_1412672d0 (gamesocket.cpp:406): m_sequence = 0
    }

    /// <summary>Reads the server hello, answers it with ResponseVersion, then loops until the stream ends or the token is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct = default, ResponseVersion hello = null) {
        await channel.ReadHandshakeAsync(ct);
        ReceivedCount = 1;
        await channel.SendAsync(hello ?? ResponseVersion.For(channel.Profile), ct);
        while (!ct.IsCancellationRequested) {
            var p = await channel.ReadPacketAsync(ct);
            ReceivedCount++;
            bool answered = false;   // a RequestKey the reconnect request answered does not reach the owner
            lock (OpcodeCounts) OpcodeCounts[p.Opcode] = OpcodeCounts.GetValueOrDefault(p.Opcode) + 1;
            if (channel.Profile.TryInbound(p.Opcode, out var op)) switch (op) {
                case Inbound.Ping:
                    channel.Out.DiscardAcked(Ping.Read(p.Reader()).Sequence);   // OnPingRequest (networkservice.cpp:876): the acked copies go
                    await channel.SendAsync(new Pong(ReceivedCount), ct);
                    break;
                case Inbound.ReconnectUnk:
                    ReceivedCount = ReconnectUnk.Read(p.Reader()).ServerSequence;   // CGameSocket::ProcessPacket (gamesocket.cpp:320): m_sequence = ReadInt32()
                    break;
                case Inbound.FinalizeReconnect: {
                    var rr = RetransmitRequest.Read(p.Reader());
                    ReceivedCount = rr.ServerSequence;                              // CGameSocket::ProcessPacket (gamesocket.cpp:321): m_sequence = first i32
                    await channel.RetransmitAsync(rr.Sequence, ct);                 // OnRetransmitRequest (networkservice.cpp:884)
                    break;
                }
                case Inbound.Gvg:
                    if (GvgOptions.FindReconnection(p.Reader()) is { } ro) { Manager?.SetOptions(ro); channel.SetReconnectOptions(ro); }   // OnGvg kind 43 (:820)
                    break;
                case Inbound.Reconnect:
                    if (Manager != null) Manager.OnPacket(ReconnectNotice.Read(p.Reader()));   // OnReconnect (networkservice.cpp:871) -> OnReconnectPacket
                    break;
                case Inbound.RequestKey:
                    // OnRequestKey (networkservice.cpp:859): while reconnecting the answer is the reconnect request, not a ResponseKey
                    if (Manager is { IsReconnecting: true }) { Manager.SendRequest(); answered = true; }
                    break;
                case Inbound.RequestHeartbeat:
                    await Send(new ResponseHeartbeat(TickRequest.Read(p.Reader()).Tick, ElapsedMs), ct);
                    break;
                case Inbound.RequestClientTickSync:
                    await Send(new ResponseClientTickSync(TickRequest.Read(p.Reader()).Tick, ElapsedMs), ct);
                    break;
                case Inbound.ResponseTimeSync:
                    if (ApplyTimeSync(p.Reader())) await Send(NextTimeSyncRequest(), ct);
                    break;
            }
            if (!answered) PacketReceived?.Invoke(p);
        }
    }
}

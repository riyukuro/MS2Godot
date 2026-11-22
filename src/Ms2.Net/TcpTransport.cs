using System.Collections.Concurrent;
using System.Net.Sockets;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Net;

/// <summary>
/// One TCP connection to a login or game server: connects, wraps the stream in a <see cref="MapleChannel"/> and runs a
/// <see cref="ClientSession"/> on a background task. Packets other than the keepalives (which the session answers on the
/// network thread) are queued in <see cref="Incoming"/> for the owner to drain on its own thread (the game's frame update).
/// No engine types here. A new instance per connection (the login to game hop opens a second one).
/// </summary>
public sealed class TcpTransport : IDisposable {
    TcpClient tcp = new() { NoDelay = true };
    readonly CancellationTokenSource cts = new();
    ClientSession session;
    Task run;
    int closed, disposed;
    OutPacketBuffer carry;
    string lastHost = ""; int lastPort;
    volatile bool wantAttempt, driving, connected;

    /// <summary>The reconnection manager this connection reports to (TSingleton CReconnectionManagerCli): the server's options and context land here, a dropped game connection is taken back through it.</summary>
    public ReconnectionManager Reconnection { get; }
    /// <summary>The scene is the game scene (IReconnectHost.InGame): a reconnect only starts there. The owner sets it once the field was entered.</summary>
    public Func<bool> InGame { get; set; } = () => false;
    /// <summary>UIReconnectTopLayerNotify (0x4f78): attempt and attempts while a reconnect runs; <see cref="ReconnectProgressHidden"/> when it ends.</summary>
    public event Action<int, int> ReconnectProgress;
    public event Action ReconnectProgressHidden;
    /// <summary>OnReconnectPacket kind 3 in game: the server ended the session (SendRequestQuitAndDisconnect, scene 0x6200009 and the 0x20f box in the client). STILL-OPEN in the owner.</summary>
    public event Action SessionEnded;

    public TcpTransport() { Reconnection = new ReconnectionManager(new ReconnectHost(this)); }

    /// <summary>Received packets in arrival order; TryDequeue from the consumer thread.</summary>
    public ConcurrentQueue<RawPacket> Incoming { get; } = new();

    /// <summary>The read loop's error when it ended on its own (stream closed by the peer, socket error, bad frame); null while open, after a
    /// clean Dispose, and when Dispose raced the error (the loop then ends by cancellation or a disposed socket, which is not a failure).
    /// <see cref="IsClosed"/> is true after either, and as soon as Dispose is called.</summary>
    public Exception Failure { get; private set; }
    public bool IsClosed => Volatile.Read(ref closed) != 0;
    public event Action Closed;

    /// <summary>The opcode numbers this connection speaks (the channel's profile).</summary>
    public ProtocolProfile Profile => session?.Channel.Profile ?? ProtocolProfile.Default;
    /// <summary>The named inbound op of a packet under this connection's profile; null for a number the profile does not know.</summary>
    public Inbound? OpOf(RawPacket p) => Profile.TryInbound(p.Opcode, out var op) ? op : null;
    public bool Is(RawPacket p, Inbound op) => Profile.Is(p.Opcode, op);

    /// <summary>The connected socket's local IPv4 as 4 bytes in network order; null when not connected or not IPv4.</summary>
    public byte[] LocalIPv4 => (tcp.Client?.LocalEndPoint as System.Net.IPEndPoint)?.Address is { } a && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? a.GetAddressBytes()
        : (tcp.Client?.LocalEndPoint as System.Net.IPEndPoint)?.Address is { IsIPv4MappedToIPv6: true } m ? m.MapToIPv4().GetAddressBytes() : null;

    public ClientSession Session => session;
    public MapleChannel Channel => session?.Channel;

    /// <summary>Connects and starts the read loop. The hello is answered by the session; after it, <see cref="Send"/> works.</summary>
    public async Task ConnectAsync(string host, int port, CancellationToken ct = default) {
        await tcp.ConnectAsync(host, port, ct);
        lastHost = host; lastPort = port;
        Attach(new MapleChannel(tcp.GetStream()));
    }

    void Attach(MapleChannel channel) {
        session = new ClientSession(channel) { Manager = Reconnection };
        session.Sender = SendAsync;   // the heartbeat, tick sync and time sync answers go through SendPacket's hold path (networkservice.cpp:746/753/796)
        carry = channel.Out;
        connected = true;
        session.PacketReceived += Incoming.Enqueue;
        var mine = session;
        run = Task.Run(() => RunAsync(mine));
    }

    async Task RunAsync(ClientSession mine) {
        try { await mine.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!cts.IsCancellationRequested && Volatile.Read(ref disposed) == 0) Failure = e; }   // after Dispose the socket error is expected: swallowed here, so the task never faults
        if (mine != session) return;   // a connection a reconnect already replaced
        connected = false;             // CGameSocket::m_state is no longer Connected: sends are held until the next connection
        if (Volatile.Read(ref disposed) == 0 && ReconnectAfterDrop()) return;
        Volatile.Write(ref closed, 1);
        try { Closed?.Invoke(); } catch { }
    }

    /// <summary>
    /// The read loop ended on a connection the client did not close. While a reconnect runs it is a failed attempt (OnReconnectFailed with the socket-closed code);
    /// else ShouldReconnect (reconnectionmanagercli.cpp @0x140aacf50) decides, and BeginReconnect starts the clock. True: the connection is not reported closed.
    /// </summary>
    bool ReconnectAfterDrop() {
        var m = Reconnection;
        if (m.IsReconnecting) { m.OnFailed(ReconnectionManager.ErrorSocketClosed); return m.IsReconnecting; }
        if (!m.ShouldReconnect(ReconnectionManager.ErrorSocketClosed)) return false;
        m.Begin(ReconnectionManager.ErrorSocketClosed);
        if (!m.IsReconnecting) return false;
        StartDriver();
        return true;
    }

    /// <summary>The main loop's part: the once-a-second Tick and the 0x46b attempt message.</summary>
    void StartDriver() {
        if (driving) return;
        driving = true;
        _ = Task.Run(async () => {
            try {
                while (Volatile.Read(ref disposed) == 0 && Volatile.Read(ref closed) == 0 && Reconnection.IsReconnecting) {
                    Reconnection.Tick();
                    if (wantAttempt) { wantAttempt = false; if (!Reconnection.TryConnect() && Reconnection.IsReconnecting) Reconnection.OnFailed(ReconnectionManager.ErrorSocketClosed); }
                    await Task.Delay(100);
                }
            } finally { driving = false; }
        });
    }

    /// <summary>The reconnect gave up (window message 0x465): the connection is closed for the owner now.</summary>
    void GaveUp(int code) {
        Failure ??= new IOException($"reconnect gave up (code {code})");
        Volatile.Write(ref closed, 1);
        try { Closed?.Invoke(); } catch { }
    }

    sealed class ReconnectHost(TcpTransport t) : IReconnectHost {
        public uint Now => unchecked((uint) Environment.TickCount64);
        public bool InGame => t.InGame();
        public bool HasConnection => t.session != null;
        public bool HasNetworkService => true;
        public bool SocketDisconnected => Volatile.Read(ref t.disposed) != 0;
        public bool Closing => Volatile.Read(ref t.disposed) != 0;   // g_socketClosing: the owner is closing the socket (the client's shutdown flag)
        public string SocketHost => t.lastHost;
        public int SocketPort => t.lastPort;
        public int SocketSequence => t.carry?.Sequence ?? 0;
        public bool IsConnected => t.tcp.Connected;
        public bool Connect(string host, int port) {
            try {
                var next = new TcpClient { NoDelay = true };
                using var wait = new CancellationTokenSource(GameClient.MoveConnectWaitMs);
                try { next.ConnectAsync(host, port, wait.Token).AsTask().GetAwaiter().GetResult(); }
                catch { next.Dispose(); throw; }
                try { t.tcp.Dispose(); } catch { }
                t.tcp = next;
                t.Attach(new MapleChannel(next.GetStream(), t.carry));
                return true;
            } catch (Exception) { return false; }
        }
        public void Close() { try { t.tcp.Dispose(); } catch { } }
        public void SendReconnectRequest(ReconnectRequest request) {
            t.SendAsync(request).ContinueWith(x => { if (x.Exception != null) Console.Error.WriteLine($"net: ReconnectRequest not sent: {x.Exception.GetBaseException().Message}"); }, TaskScheduler.Default);
        }
        public void ShowProgress(int attempt, int attempts) => t.ReconnectProgress?.Invoke(attempt, attempts);
        public void HideProgress() => t.ReconnectProgressHidden?.Invoke();
        public void Notice(string text) { }
        public void PostAttempt(int code) => t.wantAttempt = true;
        public void PostGaveUp(int code) => t.GaveUp(code);
        public void EndSession() => t.SessionEnded?.Invoke();
    }

    /// <summary>
    /// CGameSocket::SendPacket (gamesocket.cpp:279-290). Not connected: dropped when the server's HoldSends option is 1, else held (PushPendingPacket).
    /// Connected during a reconnect: only types 1, 5, 6, 7 and 8 go out, everything else is held. Held packets are sent, numbered, ahead of the next
    /// packet the channel writes (FUN_140208960), so a reconnect's retransmit and the held ones never overlap.
    /// </summary>
    public Task SendAsync(IOutPacket packet, CancellationToken ct = default) {
        var ch = Channel;
        if (ch == null) return Channel.SendAsync(packet, ct);   // never connected: the caller's error as before
        bool defer = Reconnection.IsReconnecting;
        if (connected && !defer) return ch.SendAsync(packet, ct);
        ushort type = (ushort) Profile.Number(packet.Op);
        if (connected && type is 1 or 5 or 6 or 7 or 8) return ch.SendAsync(packet, ct);
        if (!connected && Reconnection.Options[ReconnectionOptions.HoldSends] == 1) return Task.CompletedTask;
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        packet.Write(w);
        carry.PushPending(type, w.Buffer.AsSpan(0, w.Length));
        return Task.CompletedTask;
    }

    /// <summary>Cancels the read loop and closes the socket without waiting for the read task (safe on the main thread). Safe to call twice.</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref closed, 1);   // closed for the caller now; the read task ends on its own
        cts.Cancel();
        tcp.Dispose();   // unblocks a pending read
        // no wait: the read task ends by itself (its socket is closed) and releases the token source then
        if (run == null || run.IsCompleted) cts.Dispose();
        else run.ContinueWith(t => { _ = t.Exception; cts.Dispose(); }, TaskScheduler.Default);   // reads t.Exception: RunAsync swallows its errors, but an unobserved one must never reach UnobservedTaskException
    }
}

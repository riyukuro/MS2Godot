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
    readonly TcpClient tcp = new() { NoDelay = true };
    readonly CancellationTokenSource cts = new();
    ClientSession session;
    Task run;
    int closed, disposed;

    /// <summary>Received packets in arrival order; TryDequeue from the consumer thread.</summary>
    public ConcurrentQueue<RawPacket> Incoming { get; } = new();

    /// <summary>Set once the read loop has ended (stream closed, error or Dispose); null for a clean Dispose.</summary>
    public Exception Failure { get; private set; }
    public bool IsClosed => Volatile.Read(ref closed) != 0;
    public event Action Closed;

    public ClientSession Session => session;
    public MapleChannel Channel => session?.Channel;

    /// <summary>Connects and starts the read loop. The hello is answered by the session; after it, <see cref="Send"/> works.</summary>
    public async Task ConnectAsync(string host, int port, CancellationToken ct = default) {
        await tcp.ConnectAsync(host, port, ct);
        var channel = new MapleChannel(tcp.GetStream());
        session = new ClientSession(channel);
        session.PacketReceived += Incoming.Enqueue;
        run = Task.Run(() => RunAsync(channel));
    }

    async Task RunAsync(MapleChannel channel) {
        try { await session.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) when (!cts.IsCancellationRequested) { Failure = e; }
        finally {
            Volatile.Write(ref closed, 1);
            try { Closed?.Invoke(); } catch { }
        }
    }

    public Task SendAsync(IOutPacket packet, CancellationToken ct = default) => Channel.SendAsync(packet, ct);

    /// <summary>Cancels the read loop and closes the socket without waiting for the read task (safe on the main thread). Safe to call twice.</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref closed, 1);   // closed for the caller now; the read task ends on its own
        cts.Cancel();
        tcp.Dispose();   // unblocks a pending read
        // no wait: the read task ends by itself (its socket is closed) and releases the token source then
        if (run == null || run.IsCompleted) cts.Dispose();
        else run.ContinueWith(_ => cts.Dispose(), TaskScheduler.Default);
    }
}

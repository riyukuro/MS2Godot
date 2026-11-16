using System.Diagnostics;
using Maple2.PacketLib.Crypto;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Replay;

/// <summary>
/// A read-only stream that plays a recording back as a server would send it: a synthesized plain handshake frame, then every
/// recorded server packet as a real encrypted frame (so ClientSession and MapleChannel run unchanged), each released at its
/// recorded time. Recorded client packets are not sent anywhere; they are announced through <see cref="ClientPacket"/> at their
/// place in the timeline (a replay has no server to receive them). Writes (the client's answers) are counted and dropped.
/// There is no socket anywhere in this path.
/// </summary>
public sealed class ReplayStream : Stream {
    readonly List<MsbPacket> timeline;
    readonly long t0;
    readonly MapleCipher.Encryptor send;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly double speed;
    readonly double startOffset;
    byte[] cur;
    int curPos;
    int next;
    bool helloSent;

    /// <summary>Packets the client sent in the recording, raised on the reading thread when their time comes.</summary>
    public event Action<MsbPacket> ClientPacket;
    /// <summary>Seconds into the recording of the packet last handed out.</summary>
    public double PlayedSeconds { get; private set; }
    public double Duration { get; }
    public long BytesWrittenByClient { get; private set; }
    public int ServerFrames { get; private set; }

    readonly byte[] helloFrame;

    /// <param name="speed">1 = recorded pace, 2 = twice as fast; infinity = no waiting at all.</param>
    /// <param name="startOffsetSeconds">Packets earlier than this are delivered without waiting (fast forward to the interesting part).</param>
    public ReplayStream(MsbFile file, double speed = 1, double startOffsetSeconds = 0, uint siv = 0x3A5C91E7, uint riv = 0x1B2D4F63) {
        timeline = file.Packets.Where(p => !(p.ServerToClient && p.Opcode == 1)).ToList();
        t0 = file.Packets.Count > 0 ? file.Packets[0].Ticks : 0;
        Duration = timeline.Count > 0 ? (timeline[^1].Ticks - t0) / 1e7 : 0;
        this.speed = speed; startOffset = startOffsetSeconds;
        const uint version = 2525, block = 23;
        send = new MapleCipher.Encryptor(version, siv, block);
        var w = new ByteWriter();
        w.WriteShort((short) Inbound.RequestVersion);
        w.WriteInt((int) version); w.WriteInt((int) riv); w.WriteInt((int) siv); w.WriteInt((int) block); w.WriteByte(2);
        var f = send.WriteHeader(w.Buffer, 0, w.Length); // plain; advances the send iv like a real server
        helloFrame = f.Buffer.AsSpan(0, f.Length).ToArray();
    }

    double DueSeconds(MsbPacket p) => Math.Max(0, (p.Ticks - t0) / 1e7 - startOffset);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) {
        if (buffer.Length == 0) return 0;
        while (cur == null || curPos >= cur.Length) {
            if (!helloSent) { helloSent = true; cur = helloFrame; curPos = 0; ServerFrames++; break; }
            if (next >= timeline.Count) return 0;
            var p = timeline[next++];
            if (!double.IsPositiveInfinity(speed)) {
                double wait = DueSeconds(p) / speed - clock.Elapsed.TotalSeconds;
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            }
                PlayedSeconds = (p.Ticks - t0) / 1e7;
            if (!p.ServerToClient) { ClientPacket?.Invoke(p); continue; }
            var w = new ByteWriter();
            w.WriteShort((short) p.Opcode);
            w.WriteBytes(p.Data);
            var e = send.Encrypt(w);
            cur = e.Buffer.AsSpan(0, e.Length).ToArray(); curPos = 0; ServerFrames++;
        }
        int n = Math.Min(buffer.Length, cur.Length - curPos);
        cur.AsSpan(curPos, n).CopyTo(buffer.Span);
        curPos += n;
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) => BytesWrittenByClient += count;
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { BytesWrittenByClient += buffer.Length; return ValueTask.CompletedTask; }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

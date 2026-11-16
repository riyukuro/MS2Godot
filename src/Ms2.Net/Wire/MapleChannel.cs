using System.Buffers;
using Maple2.PacketLib.Crypto;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Wire;

/// <summary>One decrypted packet: opcode and the body after it.</summary>
public readonly record struct RawPacket(ushort Opcode, byte[] Body) {
    public ByteReader Reader() => new ByteReader(Body);
}

public interface IOutPacket {
    Outbound Op { get; }
    void Write(ByteWriter w);
}

/// <summary>
/// Frames and ciphers over any Stream (a socket stream in the game, a memory pipe in tests). It never opens a connection.
/// Frame = u16 seqbase + i32 len + len encrypted bytes; the opcode is inside the encrypted part.
/// Unknown opcodes are returned like any other, so the caller can count and skip them.
/// </summary>
public sealed class MapleChannel {
    readonly Stream stream;
    MapleCipher.Encryptor enc;
    MapleCipher.Decryptor dec;
    byte[] buf = new byte[8192];
    int have;
    readonly SemaphoreSlim sendLock = new(1, 1);

    public Handshake Hello { get; private set; }
    public bool Ready => dec != null;

    public MapleChannel(Stream stream) { this.stream = stream; }

    /// <summary>Reads the plain handshake frame and sets up both ciphers.</summary>
    public async Task<Handshake> ReadHandshakeAsync(CancellationToken ct = default) {
        while (true) {
            if (Handshake.TryParse(buf.AsSpan(0, have), out var hs, out int used)) {
                Consume(used);
                Hello = hs;
                (enc, dec) = hs.CreateCiphers();
                return hs;
            }
            await FillAsync(ct);
        }
    }

    /// <summary>For callers that already hold the keys (tests).</summary>
    public void SetCiphers(MapleCipher.Encryptor e, MapleCipher.Decryptor d) { enc = e; dec = d; }

    public async Task<RawPacket> ReadPacketAsync(CancellationToken ct = default) {
        while (true) {
            if (TryRead(out var pkt)) return pkt;
            await FillAsync(ct);
        }
    }

    /// <summary>Decrypts one frame from what is buffered, if it is complete.</summary>
    public bool TryRead(out RawPacket pkt) {
        pkt = default;
        if (dec == null) throw new InvalidOperationException("handshake not read");
        int n = dec.TryDecrypt(new ReadOnlySequence<byte>(buf, 0, have), out var reader);
        if (n == 0) return false;
        using (reader) {
            if (reader.Length < 2) throw new InvalidDataException("frame without opcode");
            ushort op = (ushort) reader.ReadShort();
            pkt = new RawPacket(op, reader.ReadBytes(reader.Length - 2));
        }
        Consume(n);
        return true;
    }

    public void Feed(ReadOnlySpan<byte> data) {
        if (have + data.Length > buf.Length) Array.Resize(ref buf, Math.Max(buf.Length * 2, have + data.Length));
        data.CopyTo(buf.AsSpan(have));
        have += data.Length;
    }

    async Task FillAsync(CancellationToken ct) {
        if (have == buf.Length) Array.Resize(ref buf, buf.Length * 2);
        int n = await stream.ReadAsync(buf.AsMemory(have), ct);
        if (n == 0) throw new EndOfStreamException("connection closed");
        have += n;
    }

    void Consume(int n) { Buffer.BlockCopy(buf, n, buf, 0, have - n); have -= n; }

    /// <summary>Encrypts and sends one packet. Callable from several threads: the iv advances per frame, so encrypt and write stay in order.</summary>
    public async Task SendAsync(Outbound op, Action<ByteWriter> body = null, CancellationToken ct = default) {
        var w = new ByteWriter();
        w.WriteShort((short) op);
        body?.Invoke(w);
        await sendLock.WaitAsync(ct);
        try {
            var e = enc.Encrypt(w);
            await stream.WriteAsync(e.Buffer.AsMemory(0, e.Length), ct);
            await stream.FlushAsync(ct);
        } finally { sendLock.Release(); }
    }

    public Task SendAsync(IOutPacket packet, CancellationToken ct = default) => SendAsync(packet.Op, packet.Write, ct);
}

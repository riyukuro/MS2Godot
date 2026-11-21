using System.Buffers;
using Maple2.PacketLib.Crypto;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;

namespace Ms2.Net.Wire;

/// <summary>One decrypted packet: opcode and the body after it.</summary>
public readonly record struct RawPacket(ushort Opcode, byte[] Body) {
    public ByteReader Reader() => new ByteReader(Body);
}

public interface IOutPacket {
    Outbound Op { get; }
    void Write(ByteWriter w);
}

/// <summary>An outbound packet whose layout depends on the protocol profile; the channel writes it with its own profile.</summary>
public interface IProfileOutPacket : IOutPacket {
    void Write(ByteWriter w, ProtocolProfile profile);
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

    public MapleChannel(Stream stream, OutPacketBuffer carry = null) { this.stream = stream; if (carry != null) Out = carry; }

    /// <summary>CGameSocket::m_out: numbers what is sent and keeps the copies a reconnect resends (OutPacketBuffer).</summary>
    public OutPacketBuffer Out { get; } = new();   // a reconnect's new socket carries the old one (CGameSocket keeps m_out across ConnectTo)
    /// <summary>The server's ReconnectionOptions (the reconnection manager's GetOptions) as the next ResponseKey's reset reads them.</summary>
    public ReconnectionOptions ReconnectOptions { get; } = new();

    /// <summary>
    /// CNetworkService::OnGvg kind 43 (networkservice.cpp:820-827): SetOptions, then the capture follows the enabled flag with the limit.
    /// </summary>
    public void SetReconnectOptions(ReconnectionOptions o) {
        ReconnectOptions.Assign(o);
        bool enabled = o[ReconnectionOptions.Enabled] == 1;
        if (enabled) Out.SetLimit(o[ReconnectionOptions.BufferLimit]);
        Out.SetCaptureEnabled(enabled);
    }

    /// <summary>Reads the plain handshake frame and sets up both ciphers.</summary>
    public async Task<Handshake> ReadHandshakeAsync(CancellationToken ct = default) {
        while (true) {
            if (Handshake.TryParse(buf.AsSpan(0, have), out var hs, out int used, Profile)) {
                Consume(used);
                Hello = hs;
                if (!profilePinned) profile = ProtocolProfile.ForVersion(hs.Version)
                    ?? throw new InvalidDataException($"server version {hs.Version} is not supported (known: {ProtocolProfile.KnownVersions})");
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

    ProtocolProfile profile = ProtocolProfile.Default;
    bool profilePinned;

    /// <summary>
    /// The opcode numbers this channel speaks. Until the hello is read it is the default; ReadHandshakeAsync then takes the profile of the
    /// hello's version (a version no profile knows is refused). Setting it pins the profile: the hello no longer changes it.
    /// </summary>
    public ProtocolProfile Profile { get => profile; set { profile = value; profilePinned = true; } }

    /// <summary>Encrypts and sends one packet. Callable from several threads: the iv advances per frame, so encrypt and write stay in order.</summary>
    public async Task SendAsync(Outbound op, Action<ByteWriter> body = null, CancellationToken ct = default) {
        var w = new ByteWriter();
        ushort type = (ushort) Profile.Number(op);
        w.WriteShort((short) type);
        body?.Invoke(w);
        await sendLock.WaitAsync(ct);
        try {
            foreach (var (pt, pb) in Out.TakePending()) {   // FUN_140208960: the packets held while the socket was down go first, each numbered and copied as it is sent
                var pw = new ByteWriter();
                pw.WriteShort((short) pt);
                pw.WriteBytes(pb);
                Out.Capture(pt, pb);
                var pe = enc.Encrypt(pw);
                await stream.WriteAsync(pe.Buffer.AsMemory(0, pe.Length), ct);
            }
            Out.Capture(type, w.Buffer.AsSpan(2, w.Length - 2));   // numbered and copied (only when kept) as it is sent
            var e = enc.Encrypt(w);
            await stream.WriteAsync(e.Buffer.AsMemory(0, e.Length), ct);
            await stream.FlushAsync(ct);
            if (op == Outbound.ResponseKey) { Out.Reset(); Out.Configure(ReconnectOptions); }   // FUN_1412672d0 runs after the ResponseKey is sent (networkservice.cpp:864)
        } finally { sendLock.Release(); }
    }

    /// <summary>
    /// CNetworkService::OnRetransmitRequest (networkservice.cpp:884-889): COutPacketBuffer::RetransmitClientPackets(socket, sequence): ReconnectUnk, the kept
    /// packets after <paramref name="sequence"/> in order, FinalizeReconnect; all written under the send lock, so no new packet lands between them.
    /// </summary>
    public async Task RetransmitAsync(int sequence, CancellationToken ct = default) {
        await sendLock.WaitAsync(ct);
        try {
            var frames = new List<(ushort Type, byte[] Body)>();
            Out.Retransmit(sequence, (t, b) => frames.Add((t, b)), Profile);
            foreach (var (t, b) in frames) {
                var w = new ByteWriter();
                w.WriteShort((short) t);
                w.WriteBytes(b);
                var e = enc.Encrypt(w);
                await stream.WriteAsync(e.Buffer.AsMemory(0, e.Length), ct);
            }
            await stream.FlushAsync(ct);
        } finally { sendLock.Release(); }
    }

    public Task SendAsync(IOutPacket packet, CancellationToken ct = default) =>
        packet is IProfileOutPacket pp ? SendAsync(packet.Op, w => pp.Write(w, Profile), ct) : SendAsync(packet.Op, packet.Write, ct);
}

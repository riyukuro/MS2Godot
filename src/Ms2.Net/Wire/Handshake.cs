using Maple2.PacketLib.Crypto;

namespace Ms2.Net.Wire;

/// <summary>The server's first frame, sent in the clear: u16 seqbase, i32 len, then u16 op=1, u32 version, u32 riv, u32 siv, u32 blockIV, u8 patch.</summary>
public readonly record struct Handshake(uint Version, uint ServerRecvIv, uint ServerSendIv, uint BlockIv, byte PatchType) {
    public const uint ClientVersion = 2525; // 0x9dd, CNetworkService::OnRequestVersion checks it

    public static bool TryParse(ReadOnlySpan<byte> frame, out Handshake hs, out int consumed) {
        hs = default; consumed = 0;
        if (frame.Length < 6) return false;
        int len = BitConverter.ToInt32(frame.Slice(2, 4));
        if (len < 0 || len > 1024) throw new InvalidDataException($"handshake length {len}");
        if (frame.Length < 6 + len) return false;
        var p = frame.Slice(6, len);
        if (len < 19 || BitConverter.ToUInt16(p) != (ushort) Inbound.RequestVersion) throw new InvalidDataException("not a RequestVersion frame");
        hs = new Handshake(BitConverter.ToUInt32(p.Slice(2)), BitConverter.ToUInt32(p.Slice(6)), BitConverter.ToUInt32(p.Slice(10)),
            BitConverter.ToUInt32(p.Slice(14)), p[18]);
        consumed = 6 + len;
        return true;
    }

    /// <summary>CRT rand step the server applies to an iv after every frame (also after the handshake frame).</summary>
    public static uint Step(uint iv) => unchecked(iv * 214013u + 2531011u);

    /// <summary>The client sends with the server's riv and receives with the server's siv, advanced once for the plain handshake frame.</summary>
    public (MapleCipher.Encryptor send, MapleCipher.Decryptor recv) CreateCiphers() =>
        (new MapleCipher.Encryptor(Version, ServerRecvIv, BlockIv), new MapleCipher.Decryptor(Version, Step(ServerSendIv), BlockIv));
}
